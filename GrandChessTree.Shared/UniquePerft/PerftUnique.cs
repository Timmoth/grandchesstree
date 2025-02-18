using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

using System;
using System;
using System.Threading;
using System;
using System.Threading;
using System.Collections.Generic;

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