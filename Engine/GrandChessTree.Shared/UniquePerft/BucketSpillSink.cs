using System.Buffers.Binary;
using System.Numerics;

namespace GrandChessTree.Shared;

// Streams (h1, h2) leaf records to K bucket files on disk.
// Each thread holds K small write buffers (one per bucket); flush is locked per bucket.
// RAM is bounded by threads * K * bufferSize. Disk grows with leaf encounters.
public sealed class BucketSpillSink : IDisposable
{
    private sealed class ThreadState
    {
        public byte[][] Buffers = null!;
        public int[] Lens = null!;
    }

    private readonly int _numBuckets;
    private readonly int _shiftBits;
    private readonly int _bufferSize;
    private readonly string _outDir;
    private readonly FileStream?[] _files;
    private readonly object[] _locks;
    private readonly ThreadLocal<ThreadState> _state;
    private readonly int _bucketLo;
    private readonly int _bucketHi;
    private long _totalBytesWritten;

    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public int NumBuckets => _numBuckets;
    public string OutDir => _outDir;

    public long TotalRecordsWritten => TotalBytesWritten / 16;

    // Auto-size budget — 32 MB per thread across K buckets (smaller than the
    // 34-byte sink because 16-byte records use less RAM per slot, and the
    // count-mode path needs less peak buffering).
    private const long DefaultPerThreadBudget = 32L * 1024 * 1024;

    public BucketSpillSink(string outDir, int numBuckets, int bufferSize = -1,
        int bucketLo = 0, int bucketHi = -1)
    {
        if (!BitOperations.IsPow2((uint)numBuckets))
            throw new ArgumentException("numBuckets must be a power of two");
        if (bucketHi < 0) bucketHi = numBuckets;
        if (bucketLo < 0 || bucketHi > numBuckets || bucketLo >= bucketHi)
            throw new ArgumentException($"bucket range [{bucketLo}, {bucketHi}) out of [0, {numBuckets})");
        if (bufferSize < 0)
            bufferSize = (int)(DefaultPerThreadBudget / numBuckets);
        // Align to 16 (record size) so records never straddle the boundary.
        bufferSize = (bufferSize / 16) * 16;
        if (bufferSize < 32 || (bufferSize % 16) != 0)
            throw new ArgumentException($"bufferSize must be a positive multiple of 16 bytes (was {bufferSize}; K={numBuckets} may be too large)");

        _numBuckets = numBuckets;
        _shiftBits = 64 - BitOperations.Log2((uint)numBuckets);
        _bufferSize = bufferSize;
        _outDir = outDir;
        _bucketLo = bucketLo;
        _bucketHi = bucketHi;

        Directory.CreateDirectory(outDir);
        _files = new FileStream?[numBuckets];
        _locks = new object[numBuckets];
        for (int i = 0; i < numBuckets; i++)
        {
            _locks[i] = new object();
            if (i < bucketLo || i >= bucketHi) continue;
            _files[i] = new FileStream(
                Path.Combine(outDir, $"bucket_{i:D4}.bin"),
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, useAsync: false);
        }

        _state = new ThreadLocal<ThreadState>(CreateState, trackAllValues: true);
    }

    private ThreadState CreateState()
    {
        var s = new ThreadState
        {
            Buffers = new byte[_numBuckets][],
            Lens = new int[_numBuckets],
        };
        for (int i = 0; i < _numBuckets; i++) s.Buffers[i] = new byte[_bufferSize];
        return s;
    }

    public void Record(ulong h1, ulong h2)
    {
        int bucket = (int)(h1 >> _shiftBits);
        if (bucket < _bucketLo || bucket >= _bucketHi) return;
        var s = _state.Value!;
        var buf = s.Buffers[bucket];
        int pos = s.Lens[bucket];

        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(pos), h1);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(pos + 8), h2);
        pos += 16;

        if (pos > _bufferSize - 16)
        {
            FlushBuffer(bucket, buf, pos);
            pos = 0;
        }
        s.Lens[bucket] = pos;
    }

    private void FlushBuffer(int bucket, byte[] buf, int len)
    {
        var fs = _files[bucket];
        if (fs == null) return;
        lock (_locks[bucket])
        {
            fs.Write(buf, 0, len);
        }
        Interlocked.Add(ref _totalBytesWritten, len);
    }

    public void FlushAll()
    {
        foreach (var s in _state.Values)
        {
            if (s == null) continue;
            for (int i = _bucketLo; i < _bucketHi; i++)
            {
                if (s.Lens[i] > 0)
                {
                    FlushBuffer(i, s.Buffers[i], s.Lens[i]);
                    s.Lens[i] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Flush only the calling thread's buffers. Safe while peers are calling
    /// Record concurrently; FlushAll is not (it races with concurrent Record
    /// on the same thread state). Use at per-input boundaries in workers.
    /// </summary>
    public void FlushOwn()
    {
        var s = _state.Value;
        if (s == null) return;
        for (int i = _bucketLo; i < _bucketHi; i++)
        {
            if (s.Lens[i] > 0)
            {
                FlushBuffer(i, s.Buffers[i], s.Lens[i]);
                s.Lens[i] = 0;
            }
        }
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FlushAll();
        for (int i = 0; i < _numBuckets; i++)
        {
            var fs = _files[i];
            if (fs == null) continue;
            fs.Flush(flushToDisk: true);
            fs.Dispose();
        }
        _state.Dispose();
    }
}
