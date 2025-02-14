using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public struct PerftBulkHashEntry
{
    public ulong FullHash;
    public ulong Nodes;
    public int Depth;
}
public static unsafe class PerftBulk
{
    #region HashTable
    public static uint HashTableMask;   
    public static int HashTableSize;

    [ThreadStatic] public static PerftBulkHashEntry* HashTable;

    static PerftBulk()
    {

    }
    private static uint CalculateHashTableEntries(int sizeInMb)
    {
        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(PerftBulkHashEntry);
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

    public static PerftBulkHashEntry* AllocateHashTable(int sizeInMb = 512)
    {
        HashTableSize = (int)CalculateHashTableEntries(sizeInMb);
        HashTableMask = (uint)HashTableSize - 1;

        const nuint alignment = 64;

        var bytes = ((nuint)sizeof(PerftBulkHashEntry) * (nuint)HashTableSize);
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (PerftBulkHashEntry*)block;
    }

    public static void FreeHashTable()
    {
        if (HashTable != null)
        {
            NativeMemory.AlignedFree(HashTable);
            HashTable = null;
        }
    }


    public static void ClearTable(PerftBulkHashEntry* HashTable)
    {
        if (HashTable != null)
        {
            Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(PerftBulkHashEntry) * (HashTableMask + 1)));
        }
    }

    #endregion
    
    public static ulong PerftRootBulk(ref Board board, int depth, bool whiteToMove)
    {
        if (depth == 0)
        {
            // perft(0) = 1
            return 1;
        }

        ulong nodes = 0;

        if (whiteToMove)
        {
            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            nodes += board.AccumulateWhiteKingMovesBulk(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return nodes;
            }

            board.MoveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF: checkers | *(AttackTables.LineBitBoardsInclusive + board.WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            var pinMask = board.WhiteKingPinnedRay();

            var positions = board.White & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhitePawnMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhitePawnMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                nodes += board.AccumulateWhiteKnightMovesBulk(depth, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteBishopMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteBishopMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteRookMovesBulk(depth, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteRookMovesBulk(depth, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteQueenMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteQueenMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            nodes += board.AccumulateBlackKingMovesBulk(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return nodes;
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
                nodes += board.AccumulateBlackPawnMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackPawnMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackKnightMovesBulk(depth, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackBishopMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackBishopMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackRookMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackRookMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackQueenMovesBulk(depth, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackQueenMovesBulk(depth, index,  0xFFFFFFFFFFFFFFFF);
            }
        }

        return nodes;
    }

  
}