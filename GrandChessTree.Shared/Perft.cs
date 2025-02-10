using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public static unsafe class Perft
{
    #region HashTable
    public static readonly uint HashTableMask;   
    public static readonly int HashTableSize;

    [ThreadStatic] public static Summary* HashTable;

    static Perft()
    {
        HashTableSize = (int)CalculateHashTableEntries(512);
        HashTableMask = (uint)HashTableSize - 1;
    }
    private static uint CalculateHashTableEntries(int sizeInMb)
    {
        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(Summary);
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

    public static Summary* AllocateHashTable()
    {
        const nuint alignment = 64;

        var bytes = ((nuint)sizeof(Summary) * (nuint)HashTableSize);
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (Summary*)block;
    }

    public static void FreeHashTable()
    {
        if (HashTable != null)
        {
            NativeMemory.AlignedFree(HashTable);
            HashTable = null;
        }
    }


    public static void ClearTable(Summary* HashTable)
    {
        Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(Summary) * (HashTableMask + 1)));
    }

    #endregion
    
    public static void PerftRoot(ref Board board, ref Summary summary, int depth, bool whiteToMove)
    {
        if (depth == 0)
        {
            // perft(0) = 1
            summary.Nodes++;
            return;
        }

        if (whiteToMove)
        {
            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateWhiteKingMoves( ref summary, depth, numCheckers > 0);

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
                board.AccumulateWhitePawnMoves( ref summary, depth, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board. AccumulateWhitePawnMoves( ref summary, depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                board.AccumulateWhiteKnightMoves( ref summary, depth, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopMoves( ref summary, depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopMoves( ref summary, depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookMoves( ref summary, depth, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board. AccumulateWhiteRookMoves( ref summary, depth, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenMoves( ref summary, depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenMoves( ref summary, depth, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateBlackKingMoves( ref summary, depth, numCheckers > 0);

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
                board.AccumulateBlackPawnMoves(ref summary, depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackPawnMoves( ref summary, depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackKnightMoves( ref summary, depth, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopMoves( ref summary, depth, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopMoves( ref summary, depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookMoves( ref summary, depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookMoves( ref summary, depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenMoves( ref summary, depth, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenMoves( ref summary, depth, index,  0xFFFFFFFFFFFFFFFF);
            }
        }
    }

  
}