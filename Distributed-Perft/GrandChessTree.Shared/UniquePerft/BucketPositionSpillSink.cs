using System.Numerics;

namespace GrandChessTree.Shared;

// External-memory BFS support: streams full 26-byte canonical positions to K
// bucket files on disk. Each thread holds K small write buffers; flush is
// locked per bucket file. Record layout matches BoardStateSerialization (the
// engine's existing 26-byte packed format).
//
// Routing: bucket index = (hash >> shift), so positions are partitioned by
// the top bits of their Zobrist hash. Buckets are independent for sort+dedup.
public sealed class BucketPositionSpillSink : IDisposable
{
    public const int RecordSize = 26;

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
    public long TotalRecordsWritten => TotalBytesWritten / RecordSize;
    public int NumBuckets => _numBuckets;
    public string OutDir => _outDir;

    public BucketPositionSpillSink(string outDir, int numBuckets, int bufferSize = 1024 * 1024)
    {
        if (!BitOperations.IsPow2((uint)numBuckets))
            throw new ArgumentException("numBuckets must be a power of two");
        // Round buffer size down to the nearest multiple of RecordSize so records
        // never straddle the end-of-buffer boundary.
        bufferSize = (bufferSize / RecordSize) * RecordSize;
        if (bufferSize < RecordSize * 16)
            throw new ArgumentException($"bufferSize must be at least {RecordSize * 16} bytes");

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

    /// <summary>
    /// Append a serialized 26-byte position to the bucket determined by
    /// <paramref name="routingHash"/>. The record is buffered per-thread
    /// and flushed when the buffer fills.
    /// </summary>
    public void Record(ReadOnlySpan<byte> record26, ulong routingHash)
    {
        if (record26.Length != RecordSize)
            throw new ArgumentException($"record must be exactly {RecordSize} bytes", nameof(record26));

        int bucket = (int)(routingHash >> _shiftBits);
        var s = _state.Value!;
        var buf = s.Buffers[bucket];
        int pos = s.Lens[bucket];

        record26.CopyTo(buf.AsSpan(pos));
        pos += RecordSize;

        if (pos > _bufferSize - RecordSize)
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
    /// Flush only the calling thread's buffers. Safe to call while other threads
    /// are still in <see cref="Record"/>; FlushAll is NOT (it races against
    /// concurrent Record calls on the same thread state). Use this at per-input
    /// boundaries inside worker threads; reserve FlushAll for shutdown after all
    /// workers have joined.
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
