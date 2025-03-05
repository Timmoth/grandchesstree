using GrandChessTree.Client.Stats;
using GrandChessTree.Shared;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public struct FastTaskCacheEntry
{
    public ulong Hash;
    public byte Depth;
    public ulong Nodes;
}

public unsafe class NodesSubTaskHashTable
{
    private FastTaskCacheEntry* HashTable;
    public uint HashTableMask;
    public int HashTableSize;
    public static ulong AllocatedMb = 0;

    private static unsafe uint CalculateHashTableEntries(int sizeInMb)
    {
        Console.WriteLine(sizeof(FastTaskCacheEntry));

        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(FastTaskCacheEntry);
        if (!BitOperations.IsPow2(transpositionCount))
        {
            transpositionCount = BitOperations.RoundUpToPowerOf2(transpositionCount) >> 1;
        }

        if (transpositionCount > int.MaxValue)
        {
            throw new ArgumentException("Hash table too large");
        }

        return (uint)transpositionCount;
    }
    public void AllocateHashTable(int sizeInMb = 512)
    {
        var newHashTableSize = (int)CalculateHashTableEntries(sizeInMb);
        AllocatedMb = (ulong)newHashTableSize * (ulong)sizeof(FastTaskCacheEntry) / 1024ul / 1024ul;

        if (HashTable != null && HashTableSize == newHashTableSize)
        {
            ClearTable();
            return;
        }

        if (HashTable != null)
        {
            FreeHashTable();
        }

        HashTableSize = newHashTableSize;
        HashTableMask = (uint)HashTableSize - 1;

        const nuint alignment = 64;

        var bytes = ((nuint)sizeof(FastTaskCacheEntry) * (nuint)HashTableSize);
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        HashTable = (FastTaskCacheEntry*)block;

    }
    public void FreeHashTable()
    {
        if (HashTable != null)
        {
            NativeMemory.AlignedFree(HashTable);
            HashTable = null;
        }
    }
    public void ClearTable()
    {
        if (HashTable != null)
        {
            Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(FastTaskCacheEntry) * (HashTableMask + 1)));
        }
    }

    private readonly int stripeCount = 32;

    public NodesSubTaskHashTable(int capacity)
    {
        AllocateHashTable(capacity);

        _locks = new object[stripeCount];
        for (int i = 0; i < stripeCount; i++)
        {
            _locks[i] = new object();
        }
    }

    private readonly object[] _locks;
    public void Add(ulong hash, int depth, ulong value)
    {
        var ptr = (HashTable + (hash & HashTableMask));
        FastTaskCacheEntry entry = default;
        entry.Hash = hash;
        entry.Depth = (byte)depth;
        entry.Nodes = value;
        lock (_locks[hash % (ulong)_locks.Length])
        {
            *ptr = entry;
        }
    }

    public bool TryGetValue(ulong hash, int depth, out ulong value)
    {
        var ptr = (HashTable + (hash & HashTableMask));
        FastTaskCacheEntry hashEntry;
        lock (_locks[hash % (ulong)_locks.Length])
        {
            hashEntry = Unsafe.Read<FastTaskCacheEntry>(ptr);
        }

        if (hashEntry.Hash != hash || hashEntry.Depth != depth)
        {
            value = default;
            return false;
        }

        value = hashEntry.Nodes;
        return true;
    }
}