using System.Runtime.InteropServices;

namespace GrandChessTree.Shared;

// Lock-free open-addressed hashset over 128-bit composite keys (h1, h2).
// Slot layout: two consecutive longs per slot. Empty = (0, 0).
// Protocol on insert:
//   1. CAS the h1 word from 0 to non-zero (claim slot).
//   2. Volatile.Write the h2 word.
// Readers spin briefly on h2 == 0 when h1 matches (in-flight insert window).
// 0-valued hashes are remapped to a fixed sentinel to avoid spinning forever.
public sealed unsafe class LockFreeHashSet128 : IDisposable
{
    private long* _table;
    private readonly long _capacity;
    private readonly long _mask;
    private long _count;

    public long Count => Interlocked.Read(ref _count);
    public float PercentFull => (float)Count / _capacity;
    public long Capacity => _capacity;

    public LockFreeHashSet128(long capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a positive power of two.");

        _capacity = capacity;
        _mask = capacity - 1;
        var bytes = (nuint)capacity * 16; // 16 bytes per slot
        _table = (long*)NativeMemory.AlignedAlloc(bytes, 64);
        NativeMemory.Clear(_table, bytes);
    }

    private const ulong ZeroSentinel = 0x8000000000000001UL;

    // Cap linear-probe distance per insert. Past this point the table is too
    // full to be useful as a filter — return TableFull and let the caller fall
    // through to spill (the external merger dedups). Without this cap, a
    // near-full table (e.g. load factor ≥ 0.9) collapses throughput by orders
    // of magnitude: avg unsuccessful-probe length grows as 1/(1-α)² and each
    // probe is a DRAM-latency cache miss into a multi-GB table. False
    // "TableFull" beyond the cap only loses some dedup efficiency — never
    // affects the final unique count, which the merger establishes.
    private const int MaxProbeDistance = 64;

    private static long Mix(ulong h1, ulong h2)
    {
        ulong v = h1 ^ (h2 * 0x9E3779B97F4A7C15UL);
        v ^= v >> 32;
        v *= 0xD6E8FEB86659FD93UL;
        v ^= v >> 32;
        return (long)v;
    }

    public bool Add(ulong h1, ulong h2)
    {
        // Backward-compatible 2-state Add. Returns true on insert, false on
        // either "already present" or "table full". Callers that need to
        // distinguish those cases must use TryAdd instead — conflating them
        // silently drops records when the table fills.
        return TryAdd(h1, h2) == AddResult.Added;
    }

    public enum AddResult : byte
    {
        Added = 0,
        AlreadyPresent = 1,
        TableFull = 2,
    }

    /// <summary>
    /// Probe the table and either insert (Added), confirm an existing entry
    /// (AlreadyPresent), or report that the table is too full to determine
    /// either (TableFull). Callers that use the set as a best-effort filter
    /// should treat TableFull the same as Added (i.e. fall through to spill).
    /// </summary>
    public AddResult TryAdd(ulong h1, ulong h2)
    {
        if (h1 == 0) h1 = ZeroSentinel;
        if (h2 == 0) h2 = ZeroSentinel;

        long index = Mix(h1, h2) & _mask;
        long key1 = (long)h1;
        long key2 = (long)h2;

        for (int i = 0; i < MaxProbeDistance; i++)
        {
            long pos = (index + i) & _mask;
            long* slot = _table + pos * 2;

            long curH1 = Volatile.Read(ref slot[0]);

            if (curH1 == key1)
            {
                long curH2;
                while ((curH2 = Volatile.Read(ref slot[1])) == 0) { /* spin */ }
                if (curH2 == key2) return AddResult.AlreadyPresent;
                continue; // same h1 but different h2 — keep probing
            }

            if (curH1 == 0)
            {
                long old = Interlocked.CompareExchange(ref slot[0], key1, 0);
                if (old == 0)
                {
                    Volatile.Write(ref slot[1], key2);
                    Interlocked.Increment(ref _count);
                    return AddResult.Added;
                }
                if (old == key1)
                {
                    long curH2;
                    while ((curH2 = Volatile.Read(ref slot[1])) == 0) { /* spin */ }
                    if (curH2 == key2) return AddResult.AlreadyPresent;
                    continue;
                }
                // Different key claimed this slot; keep probing.
            }
        }
        return AddResult.TableFull;
    }

    public void Clear()
    {
        NativeMemory.Clear(_table, (nuint)_capacity * 16);
        Interlocked.Exchange(ref _count, 0);
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_table != null)
        {
            NativeMemory.AlignedFree(_table);
            _table = null;
        }
    }

    ~LockFreeHashSet128() => Dispose();
}
