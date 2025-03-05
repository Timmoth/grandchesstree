using System.Collections;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared;

namespace GrandChessTree.Client.Stats
{
    public struct FullTaskCacheEntry {
        public ulong Hash;
        public byte Depth;
        public Summary Summary;
    }

    public unsafe class SubTaskHashTable
    {
        private FullTaskCacheEntry* HashTable;
        public uint HashTableMask;
        public int HashTableSize;
        public static ulong AllocatedMb = 0;
        private static unsafe uint CalculateHashTableEntries(int sizeInMb)
        {
            var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(FullTaskCacheEntry);
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
            AllocatedMb = (ulong)newHashTableSize * (ulong)sizeof(FullTaskCacheEntry) / 1024ul / 1024ul;

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

            var bytes = ((nuint)sizeof(FullTaskCacheEntry) * (nuint)HashTableSize);
            var block = NativeMemory.AlignedAlloc(bytes, alignment);
            NativeMemory.Clear(block, bytes);

            HashTable = (FullTaskCacheEntry*)block;

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
                Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(FullTaskCacheEntry) * (HashTableMask + 1)));
            }
        }

        private readonly int stripeCount = 32;
        public SubTaskHashTable(int capacity)
        {
            AllocateHashTable(capacity);

            // Initialize your hash table...
            _locks = new object[stripeCount];
            for (int i = 0; i < stripeCount; i++)
            {
                _locks[i] = new object();
            }
        }

        private readonly object[] _locks;
        public void Add(ulong hash, int depth, Summary value)
        {
            var ptr = (HashTable + (hash & HashTableMask));
            FullTaskCacheEntry entry = default;
            entry.Hash = hash;
            entry.Depth = (byte)depth;
            entry.Summary = value;
            lock (_locks[hash % (ulong)_locks.Length])
            {
                *ptr = entry;
            }
        }

        public bool TryGetValue(ulong hash, int depth, out Summary value)
        {
            var ptr = (HashTable + (hash & HashTableMask));
            FullTaskCacheEntry hashEntry;
            lock (_locks[hash % (ulong)_locks.Length])
            {
                hashEntry = Unsafe.Read<FullTaskCacheEntry>(ptr);
            }

            if (hashEntry.Hash != hash || hashEntry.Depth != depth)
            {
                value = default;
                return false;
            }

            value = hashEntry.Summary;
            return true;
        }

    }
}
