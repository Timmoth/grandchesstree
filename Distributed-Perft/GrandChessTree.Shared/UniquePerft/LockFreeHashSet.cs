using System.Runtime.InteropServices;
using GrandChessTree.Shared;

public sealed unsafe class LockFreeHashSet : IDisposable
{
    // Slots stored in unmanaged memory so we can go past Array.MaxLength (~2 GB).
    // Each slot is a 64-bit value; 0 is the empty sentinel.
    private long* _table;
    private readonly long _capacity;
    private readonly long _mask;

    private long _count;
    public long Count => Interlocked.Read(ref _count);
    public float PercentFull => (float)Count / _capacity;
    public long Capacity => _capacity;

    public KeyDumpSink? DumpSink;

    private readonly object _clearLock = new();

    // Backwards-compat constructor: takes int capacity (power of two).
    public LockFreeHashSet(int capacity) : this((long)capacity) { }

    public LockFreeHashSet(long capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a positive power of two.");

        _capacity = capacity;
        _mask = capacity - 1;
        var bytes = (nuint)capacity * (nuint)sizeof(long);
        _table = (long*)NativeMemory.AlignedAlloc(bytes, 64);
        NativeMemory.Clear(_table, bytes);
    }

    private static long Hash(ulong value) =>
        (long)(value ^ (value >> 32));

    public bool Add(ulong value)
    {
        long index = Hash(value) & _mask;

        for (long i = 0; i < _capacity; i++)
        {
            long pos = (index + i) & _mask;
            long current = Volatile.Read(ref _table[pos]);

            if ((ulong)current == value) return false;

            if (current == 0)
            {
                long original = Interlocked.CompareExchange(ref _table[pos], (long)value, 0);
                if (original == 0)
                {
                    Interlocked.Increment(ref _count);
                    DumpSink?.Record(value);
                    return true;
                }
                if ((ulong)original == value) return false;
            }
        }
        return false;
    }

    public bool Contains(ulong value)
    {
        long index = Hash(value) & _mask;

        for (long i = 0; i < _capacity; i++)
        {
            long pos = (index + i) & _mask;
            long current = Volatile.Read(ref _table[pos]);

            if ((ulong)current == value) return true;
            if (current == 0) return false;
        }
        return false;
    }

    public void Clear()
    {
        lock (_clearLock)
        {
            NativeMemory.Clear(_table, (nuint)_capacity * (nuint)sizeof(long));
            Interlocked.Exchange(ref _count, 0);
        }
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

    ~LockFreeHashSet() => Dispose();
}
