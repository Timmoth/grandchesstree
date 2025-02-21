namespace GrandChessTree.Shared;
using System;
using System.Threading;

public class UniqueUlongHashSet
{
    private readonly long[][] _buckets;
    private readonly int _bucketCount;
    private readonly int _bucketSize;
    private readonly int _bucketHashMask;
    private readonly int _hashesPerKey;

    // Unique key count.
    private int _count;

    public UniqueUlongHashSet(int bucketCount = 4, int bucketSizeExp = 30, int hashesPerKey = 4)
    {
        _bucketCount = bucketCount;
        _bucketSize = 1 << bucketSizeExp;
        _bucketHashMask = _bucketSize - 1;
        _hashesPerKey = hashesPerKey;

        _buckets = new long[bucketCount][];
        for (int i = 0; i < bucketCount; i++)
        {
            _buckets[i] = new long[_bucketSize];
        }
    }

    public int Count => Volatile.Read(ref _count);

    public void Add(ulong input)
    {
        bool isUnique = false;

        // Hash the ulong
        ulong baseValue = PrimaryHash(input);

        // Each hash goes into a set number of buckets
        for (int i = 0; i < _hashesPerKey; i++)
        {
            // Use a different portion of the hash each iteration
            int rotation = (i * 17) % 64;
            ulong mutated = RotateRight(baseValue, rotation);

            // Choose a bucket from the pool by using the Lower bits
            int bucketIndex = (int)(mutated % (ulong)_bucketCount);

            // Use the next bits to pick the bucket element index
            // Use the 6 lowest bits for the flag index.
            int elementIndex = (int)((mutated >> 6) & (ulong)_bucketHashMask);
            int bit = (int)(mutated & 0x3F);
            long mask = 1L << bit;

            // Set the bit flag in the selected bucket's element.
            long original = _buckets[bucketIndex][elementIndex];

            // If the bit was already set, then this must be a unique element
            if ((original & mask) == 0)
            {
                isUnique = true;
                _buckets[bucketIndex][elementIndex] |= mask;
            }
        }

        if (isUnique)
        {
            // At least one bit was not set, must be unique
            _count++;
        }
    }

    /// <summary>
    /// Rotates a 64-bit value to the right by the specified number of bits.
    /// </summary>
    private static ulong RotateRight(ulong value, int bits)
    {
        return (value >> bits) | (value << (64 - bits));
    }

    /// <summary>
    /// A primary 64-bit hash function to improve the distribution of input keys.
    /// </summary>
    private static ulong PrimaryHash(ulong key)
    {
        key ^= key >> 33;
        key *= 0xff51afd7ed558ccdUL;
        key ^= key >> 33;
        key *= 0xc4ceb9fe1a85ec53UL;
        key ^= key >> 33;
        return key;
    }
}
