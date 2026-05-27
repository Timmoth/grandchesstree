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
    private readonly FileStream[] _files;
    private readonly object[] _locks;
    private readonly ThreadLocal<ThreadState> _state;
    private long _totalBytesWritten;

    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public int NumBuckets => _numBuckets;
    public string OutDir => _outDir;

    public long TotalRecordsWritten => TotalBytesWritten / 16;

    public BucketSpillSink(string outDir, int numBuckets, int bufferSize = 256 * 1024)
    {
        if (!BitOperations.IsPow2((uint)numBuckets))
            throw new ArgumentException("numBuckets must be a power of two");
        if (bufferSize < 32 || (bufferSize % 16) != 0)
            throw new ArgumentException("bufferSize must be a multiple of 16 bytes");

        _numBuckets = numBuckets;
        _shiftBits = 64 - BitOperations.Log2((uint)numBuckets);
        _bufferSize = bufferSize;
        _outDir = outDir;

        Directory.CreateDirectory(outDir);
        _files = new FileStream[numBuckets];
        _locks = new object[numBuckets];
        for (int i = 0; i < numBuckets; i++)
        {
            _files[i] = new FileStream(
                Path.Combine(outDir, $"bucket_{i:D4}.bin"),
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, useAsync: false);
            _locks[i] = new object();
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
        lock (_locks[bucket])
        {
            _files[bucket].Write(buf, 0, len);
        }
        Interlocked.Add(ref _totalBytesWritten, len);
    }

    public void FlushAll()
    {
        foreach (var s in _state.Values)
        {
            if (s == null) continue;
            for (int i = 0; i < _numBuckets; i++)
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
        for (int i = 0; i < _numBuckets; i++)
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
            _files[i].Flush(flushToDisk: true);
            _files[i].Dispose();
        }
        _state.Dispose();
    }
}
