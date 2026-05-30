using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;

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
    // Record layout: 34 bytes = canonical 26-byte position (per BoardStateSerialization)
    // followed by an 8-byte little-endian u64 DFS-emission counter ("sortKey").
    //
    // Why this layout (DFS-sort-compression experiment):
    //   - Per-bucket dedup still sorts/dedups by the 26-byte position prefix —
    //     unchanged semantics from the prior 26-byte layout.
    //   - The sortKey rides along so a Phase-3 cross-bucket k-way merge can
    //     reorder the deduped global stream by emission order. Adjacent records
    //     in that stream tend to share a parent (one chess move apart), which
    //     xz --delta exploits to push compression well past the ~5× ceiling we
    //     see with hash-bucketed position-byte order.
    //
    // Earlier failed attempt (don't repeat): a prior sortKey scheme sorted by
    // sortKey *within* a hash bucket. The bucket is hash-randomised, so
    // sortKey order inside it gave a sparse subset of the global DFS counter
    // range — no chess-adjacency, sortKey was pure overhead. This layout
    // succeeds because the global merge across buckets restores adjacency.
    public const int RecordSize = 34;

    private sealed class ThreadState
    {
        public BucketPositionSpillSink Owner = null!;
        public byte[][] Buffers = null!;
        public int[] Lens = null!;
    }

    private readonly int _numBuckets;
    private readonly int _shiftBits;
    private readonly int _bufferSize;
    private readonly string _outDir;
    private readonly FileStream?[] _files;
    private readonly object[] _locks;
    // Per-thread state via [ThreadStatic]. Replaces ThreadLocal<T> (whose
    // .Value getter went through __tls_get_addr — 16 % of total CPU in the
    // d8 perf+PerfMap profile). [ThreadStatic] compiles to an inline
    // fs:[offset] read on Linux x64, no glibc TLS resolver call.
    //
    // Constraint: only one BucketPositionSpillSink may be active per process
    // at a time (the [ThreadStatic] field is shared across instances of
    // BucketPositionSpillSink). The engine respects this — wave_expand sets
    // PerftUnique.PositionSpillSink to exactly one instance and clears it on
    // shutdown. The Owner-check in GetState defends against accidental
    // overlap by re-initialising when the sink changes.
    [ThreadStatic] private static ThreadState? _tlsState;
    private readonly ConcurrentBag<ThreadState> _allStates = new();
    private readonly int _bucketLo;
    private readonly int _bucketHi;
    private long _totalBytesWritten;

    // Async writer pipeline. Producers (the Record callers in engine worker
    // threads) never block on disk I/O: they swap their full buffer for a
    // spare and hand the full one to the channel. Writer threads drain the
    // channel and run the actual fs.Write under the per-bucket lock — so the
    // only lock contention is among the writer threads themselves, off the
    // hot path. Profiling pre-change showed 8 % of total wall in
    // Monitor.Enter_Slowpath called from FlushBuffer; this moves that wait
    // off the producer threads entirely.
    private readonly Channel<(int bucket, byte[] buf, int len)> _workChannel;
    private readonly Thread[] _writerThreads;
    private readonly ConcurrentBag<byte[]> _bufferPool;

    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public long TotalRecordsWritten => TotalBytesWritten / RecordSize;
    public int NumBuckets => _numBuckets;
    public string OutDir => _outDir;

    /// <summary>
    /// Create the sink. By default writes to all K bucket files. When
    /// <paramref name="bucketLo"/> / <paramref name="bucketHi"/> are passed, only
    /// the file slots in [lo, hi) are created — other files in <paramref name="outDir"/>
    /// are not touched (so a prior multi-pass invocation's bucket files survive).
    /// </summary>
    // Auto-size budget: total per-thread buffer memory across all K buckets.
    // 64 MB / K → 1 MB buffers at K=64 (matches the historical default),
    // 256 KB at K=256, 16 KB at K=4096. Across 32 threads that's 2 GB of
    // buffers regardless of K — safe to fit anywhere.
    private const long DefaultPerThreadBudget = 64L * 1024 * 1024;

    public BucketPositionSpillSink(string outDir, int numBuckets, int bufferSize = -1,
        int bucketLo = 0, int bucketHi = -1)
    {
        if (!BitOperations.IsPow2((uint)numBuckets))
            throw new ArgumentException("numBuckets must be a power of two");
        if (bucketHi < 0) bucketHi = numBuckets;
        if (bucketLo < 0 || bucketHi > numBuckets || bucketLo >= bucketHi)
            throw new ArgumentException($"bucket range [{bucketLo}, {bucketHi}) out of [0, {numBuckets})");
        // Auto-size if not explicitly given. Critical for high-K runs (ply 11+
        // wants K ≥ 4096); the legacy 1 MB-per-bucket default would consume
        // 128 GB of RAM at K=4096 × 32 threads.
        if (bufferSize < 0)
            bufferSize = (int)(DefaultPerThreadBudget / numBuckets);
        // Round buffer size down to the nearest multiple of RecordSize so records
        // never straddle the end-of-buffer boundary.
        bufferSize = (bufferSize / RecordSize) * RecordSize;
        if (bufferSize < RecordSize * 16)
            throw new ArgumentException($"bufferSize must be at least {RecordSize * 16} bytes (was {bufferSize}; K={numBuckets} may be too large)");

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
            // Only open (and truncate) files for buckets this pass owns.
            if (i < bucketLo || i >= bucketHi) continue;
            _files[i] = new FileStream(
                Path.Combine(outDir, $"bucket_{i:D4}.bin"),
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, useAsync: false);
        }


        // Bounded channel — provides back-pressure so producers can't outrun
        // the disk and OOM the box. At d8 scale (26 GB spill) the unbounded
        // version was fine; at d9+ scale (300+ GB spill) the producer/writer
        // throughput gap let the channel grow until OOM-kill. 128 items in
        // flight = ~128 MB worst case at K=64, ~2 MB at K=4096 — safe at
        // any K and still way more buffering than producers actually need.
        _workChannel = Channel.CreateBounded<(int bucket, byte[] buf, int len)>(
            new BoundedChannelOptions(capacity: 128)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        _bufferPool = new ConcurrentBag<byte[]>();
        // Writer count: a handful is enough — NVMe sustained-write tops out
        // around 2-3 GB/s and one writer can issue ~1 GB/s of buffered
        // synchronous writes. More than ~4 writers just adds lock contention
        // among themselves with no throughput gain.
        int writerCount = Math.Max(2, Math.Min(4, Math.Max(1, numBuckets / 4)));
        _writerThreads = new Thread[writerCount];
        for (int i = 0; i < writerCount; i++)
        {
            var t = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = $"BucketSpillWriter-{i}",
            };
            t.Start();
            _writerThreads[i] = t;
        }
    }

    private byte[] AcquireBuffer()
    {
        if (_bufferPool.TryTake(out var buf)) return buf;
        return new byte[_bufferSize];
    }

    private void WriterLoop()
    {
        var reader = _workChannel.Reader;
        // Block-and-drain loop. WaitToReadAsync().AsTask().GetAwaiter()...
        // would work too but Wait() is simpler and there's nothing async to
        // bubble up. The channel completes when Dispose runs.
        while (true)
        {
            if (!reader.TryRead(out var work))
            {
                // Block until an item is available or the channel completes.
                var wait = reader.WaitToReadAsync().AsTask();
                wait.Wait();
                if (!wait.Result) return;
                continue;
            }
            var fs = _files[work.bucket];
            if (fs != null)
            {
                lock (_locks[work.bucket])
                {
                    fs.Write(work.buf, 0, work.len);
                }
                Interlocked.Add(ref _totalBytesWritten, work.len);
            }
            _bufferPool.Add(work.buf);
        }
    }

    private ThreadState CreateState()
    {
        var s = new ThreadState
        {
            Owner = this,
            Buffers = new byte[_numBuckets][],
            Lens = new int[_numBuckets],
        };
        for (int i = 0; i < _numBuckets; i++) s.Buffers[i] = new byte[_bufferSize];
        _allStates.Add(s);
        return s;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private ThreadState GetOrCreateState()
    {
        var s = _tlsState;
        if (s == null || s.Owner != this)
        {
            s = CreateState();
            _tlsState = s;
        }
        return s;
    }

    /// <summary>
    /// Append a serialized record (position + sortKey, see RecordSize) to the
    /// bucket determined by <paramref name="routingHash"/>. The record is
    /// buffered per-thread and flushed when the buffer fills.
    /// </summary>
    public void Record(ReadOnlySpan<byte> record, ulong routingHash)
    {
        if (record.Length != RecordSize)
            throw new ArgumentException($"record must be exactly {RecordSize} bytes", nameof(record));

        int bucket = (int)(routingHash >> _shiftBits);
        // Defensive: bucket-range filter at the leaf usually catches these, but
        // guard here too so a wrong-range record doesn't NullRef on _files[bucket].
        if (bucket < _bucketLo || bucket >= _bucketHi) return;

        var s = GetOrCreateState();
        var buf = s.Buffers[bucket];
        int pos = s.Lens[bucket];

        record.CopyTo(buf.AsSpan(pos));
        pos += RecordSize;

        if (pos > _bufferSize - RecordSize)
        {
            EnqueueFlush(bucket, buf, pos);
            // Swap in a fresh buffer immediately so the next Record on this
            // (thread, bucket) does not touch the one in flight.
            s.Buffers[bucket] = AcquireBuffer();
            pos = 0;
        }
        s.Lens[bucket] = pos;
    }

    private void EnqueueFlush(int bucket, byte[] buf, int len)
    {
        if (_files[bucket] == null) return; // out-of-range; defensive.
        var writer = _workChannel.Writer;
        if (writer.TryWrite((bucket, buf, len))) return;
        // Bounded channel is full — producer blocks until a writer drains a
        // slot. This is the intentional back-pressure that keeps memory
        // bounded at d9+ scale (300 GB+ spill total, producers outpacing
        // disk writers).
        writer.WriteAsync((bucket, buf, len)).AsTask().Wait();
    }

    public void FlushAll()
    {
        foreach (var s in _allStates)
        {
            if (s.Owner != this) continue;
            for (int i = _bucketLo; i < _bucketHi; i++)
            {
                if (s.Lens[i] > 0)
                {
                    EnqueueFlush(i, s.Buffers[i], s.Lens[i]);
                    s.Buffers[i] = AcquireBuffer();
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
        var s = _tlsState;
        if (s == null || s.Owner != this) return;
        for (int i = _bucketLo; i < _bucketHi; i++)
        {
            if (s.Lens[i] > 0)
            {
                EnqueueFlush(i, s.Buffers[i], s.Lens[i]);
                s.Buffers[i] = AcquireBuffer();
                s.Lens[i] = 0;
            }
        }
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Hand every still-buffered record to the writer threads, then close
        // the channel so the writers see "no more work" and exit. Joining
        // them before touching _files guarantees no in-flight write is
        // racing with the FileStream.Dispose below.
        FlushAll();
        _workChannel.Writer.Complete();
        foreach (var t in _writerThreads) t.Join();
        for (int i = 0; i < _numBuckets; i++)
        {
            var fs = _files[i];
            if (fs == null) continue;
            fs.Flush(flushToDisk: true);
            fs.Dispose();
        }
    }
}
