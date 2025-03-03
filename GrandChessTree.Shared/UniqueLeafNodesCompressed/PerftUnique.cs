using System.Numerics;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public class UniqueLeafNodeGeneratorCompressedEntry
{
    public string board;
    public int order;
    public int occurrences;
}
public static unsafe class UniqueLeafNodeGeneratorCompressed
{
    [ThreadStatic] public static Dictionary<ulong, UniqueLeafNodeGeneratorCompressedEntry> boards;
    [ThreadStatic] public static int Order = 0;
    public static void PerftRootCompressedUniqueLeafNodes(ref Board board, int depth, bool whiteToMove)
    {
        if(boards == null)
        {
            boards = new();
        }

        boards.Clear();
        Order = 0;

        if (depth == 0)
        {
            // perft(0) = 1
            return;
        }

        if (whiteToMove)
        {
            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateWhiteKingCompressedUniqueLeafNodes(depth, numCheckers > 0);

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
                board.AccumulateWhitePawnCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhitePawnCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                board.AccumulateWhiteKnightCompressedUniqueLeafNodes(depth, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookCompressedUniqueLeafNodes(depth, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookCompressedUniqueLeafNodes(depth, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateBlackKingCompressedUniqueLeafNodes(depth, numCheckers > 0);

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
                board.AccumulateBlackPawnCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackPawnCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackKnightCompressedUniqueLeafNodes(depth, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenCompressedUniqueLeafNodes(depth, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenCompressedUniqueLeafNodes(depth, index,  0xFFFFFFFFFFFFFFFF);
            }
        }

        return;
    }


}