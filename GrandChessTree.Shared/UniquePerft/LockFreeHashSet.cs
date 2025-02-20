using System.Collections.Concurrent;

public class LockFreeHashSet
{
    // We store our values in a long[] and interpret them as ulong.
    // We reserve the value 0 to indicate an empty slot.
    private long[] table;
    private readonly int capacity;

    // Count of successfully inserted items.
    private int count;
    public int Count => Volatile.Read(ref count);
    public float PercentFull => (float)Count / capacity;

    // A lock object used only for the Clear operation.
    private readonly object clearLock = new object();

    public LockFreeHashSet(int capacity)
    {
        // Ensure capacity is a power of two.
        if ((capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a power of two.");

        this.capacity = capacity;
        table = new long[capacity];
        // Array elements are zero-initialized by default.
        count = 0;
    }

    // A simple hash function that mixes the bits of the ulong value.
    private int Hash(ulong value)
    {
        return (int)(value ^ (value >> 32));
    }

    /// <summary>
    /// Attempts to insert the value into the set.
    /// Returns true if the value was inserted, or false if it was already present or the table is full.
    /// If insertion succeeds, the Count is incremented atomically.
    /// </summary>
    public bool Add(ulong value)
    {
        int hash = Hash(value);
        int index = hash & (capacity - 1);

        for (int i = 0; i < capacity; i++)
        {
            int pos = (index + i) & (capacity - 1);
            long current = Volatile.Read(ref table[pos]);

            // Value already present.
            if ((ulong)current == value)
                return false;

            // Empty slot found.
            if (current == 0)
            {
                // Atomically try to install the new value.
                long original = Interlocked.CompareExchange(ref table[pos], (long)value, 0);
                if (original == 0)
                {
                    // Successful insertion; increment the count.
                    Interlocked.Increment(ref count);
                    return true;
                }
                // If another thread inserted the same value concurrently, we return false.
                if ((ulong)original == value)
                    return false;
            }
            // Otherwise, keep probing.
        }

        // The table is full.
        return false;
    }

    /// <summary>
    /// Checks if the set contains the specified value.
    /// </summary>
    public bool Contains(ulong value)
    {
        int hash = Hash(value);
        int index = hash & (capacity - 1);

        for (int i = 0; i < capacity; i++)
        {
            int pos = (index + i) & (capacity - 1);
            long current = Volatile.Read(ref table[pos]);

            if ((ulong)current == value)
                return true;
            if (current == 0)
                return false;
        }
        return false;
    }

    /// <summary>
    /// Clears the hash set by resetting all table slots and count.
    /// Note: This method uses a lock to ensure that the reset occurs safely.
    /// It is best to call Clear() only when concurrent inserts are not occurring.
    /// </summary>
    public void Clear()
    {
        lock (clearLock)
        {
            for (int i = 0; i < capacity; i++)
            {
                // Reset each slot to the EMPTY sentinel.
                table[i] = 0;
            }
            // Reset the count atomically.
            Interlocked.Exchange(ref count, 0);
        }
    }
}