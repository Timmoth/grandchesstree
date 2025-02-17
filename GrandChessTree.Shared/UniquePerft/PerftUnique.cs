using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

using System;
public class LockFreeHashSet
{
    private readonly ulong[] _keys;
    private readonly int[] _states; // 0 = empty, 1 = occupied
    private int _count;
    private readonly int _mask;

    /// <summary>
    /// Creates a lock-free hash set with the specified capacity.
    /// Capacity must be a power of 2.
    /// </summary>
    /// <param name="capacity">The size of the internal table (power of 2).</param>
    public LockFreeHashSet(int capacity = 1 << 30)
    {
        if ((capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a power of 2.", nameof(capacity));

        _keys = new ulong[capacity];
        _states = new int[capacity];  // All slots start as 0 (empty)
        _mask = capacity - 1;
    }

    /// <summary>
    /// Adds a key to the hash set if it isn’t already present.
    /// Returns true if the key was added (i.e. it was not present before).
    /// Throws an exception if the table is full.
    /// </summary>
    public bool Add(ulong key)
    {
        // Since the input key is already a hash, use it directly.
        int startIndex = (int)(key & (ulong)_mask);
        int index = startIndex;
        // Limit the number of probes to the table size to avoid infinite loops.
        for (int probes = 0; probes < _keys.Length; probes++)
        {
            int state = Volatile.Read(ref _states[index]);
            if (state == 0)
            {
                // The slot appears empty.
                // Try to claim it by atomically setting the state from 0 to 1.
                if (Interlocked.CompareExchange(ref _states[index], 1, 0) == 0)
                {
                    // Successfully claimed this slot.
                    // Write the key. Volatile.Write ensures proper memory ordering.
                    Volatile.Write(ref _keys[index], key);
                    // Update the count.
                    Interlocked.Increment(ref _count);
                    return true;
                }
                // If we lose the race, fall through and check the slot again.
            }
            else
            {
                // The slot is occupied—check if it already holds our key.
                if (Volatile.Read(ref _keys[index]) == key)
                {
                    // Key already exists in the set.
                    return false;
                }
            }
            // Move to the next slot (with wrapping).
            index = (index + 1) & _mask;
        }
        // If we scanned the entire table, then it's full.
        throw new InvalidOperationException("HashSet is full. Consider resizing or increasing the capacity.");
    }


    /// <summary>
    /// Gets the count of unique keys that have been added.
    /// </summary>
    public int Count => Volatile.Read(ref _count);
}


public struct PerftUniqueHashEntry
{
    public ulong FullHash;
    public ulong Nodes;
    public int Depth;
}
public static unsafe class PerftUnique
{
    #region HashTable
    public static uint HashTableMask;   
    public static int HashTableSize;

    [ThreadStatic] public static PerftUniqueHashEntry* HashTable;

    static PerftUnique()
    {

    }
    private static uint CalculateHashTableEntries(int sizeInMb)
    {
        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(PerftUniqueHashEntry);
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

    public static PerftUniqueHashEntry* AllocateHashTable(int sizeInMb = 512)
    {
        HashTableSize = (int)CalculateHashTableEntries(sizeInMb);
        HashTableMask = (uint)HashTableSize - 1;

        const nuint alignment = 64;

        var bytes = ((nuint)sizeof(PerftUniqueHashEntry) * (nuint)HashTableSize);
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (PerftUniqueHashEntry*)block;
    }

    public static void FreeHashTable()
    {
        if (HashTable != null)
        {
            NativeMemory.AlignedFree(HashTable);
            HashTable = null;
        }
    }


    public static void ClearTable(PerftUniqueHashEntry* HashTable)
    {
        if (HashTable != null)
        {
            Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(PerftUniqueHashEntry) * (HashTableMask + 1)));
        }
    }

    #endregion

    public static HashSet<ulong> UniquePositions = new ();

    public static void PerftRootUnique(ref Board board, int depth, bool whiteToMove)
    {
        if (depth == 0)
        {
            // perft(0) = 1
            return;
        }

        if (whiteToMove)
        {
            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateWhiteKingMovesUnique(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return;
            }

            board.MoveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF: checkers | *(AttackTables.LineBitBoardsInclusive + board.WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            var pinMask = board.WhiteKingPinnedRay();

            var positions = board.White & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhitePawnMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhitePawnMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                board.AccumulateWhiteKnightMovesUnique(depth, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookMovesUnique(depth, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookMovesUnique(depth, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateBlackKingMovesUnique(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return;
            }
            
            board.MoveMask = 0xFFFFFFFFFFFFFFFF;
            if (numCheckers == 1)
            {
                board.MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + board.BlackKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            }
            var pinMask = board.BlackKingPinnedRay();

            var positions = board.Black & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackPawnMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackPawnMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackKnightMovesUnique(depth, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenMovesUnique(depth, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenMovesUnique(depth, index,  0xFFFFFFFFFFFFFFFF);
            }
        }

        return;
    }

  
}