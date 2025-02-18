using System.Numerics;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public static unsafe class MoveGenerator
{
    public static int GenerateMoves(ref Span<uint> moves, ref Board board, bool whiteToMove)
    {
        var moveIndex = 0;
        if (whiteToMove)
        {
            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.GenerateWhiteKingMoves(ref moves, ref moveIndex, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return moveIndex;
            }

            board.MoveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF: checkers | *(AttackTables.LineBitBoardsInclusive + board.WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            var pinMask = board.WhiteKingPinnedRay();

            var positions = board.White & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhitePawnMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhitePawnMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                board.GenerateWhiteKnightMoves(ref moves, ref moveIndex, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhiteBishopMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhiteBishopMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhiteRookMoves(ref moves, ref moveIndex, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhiteRookMoves(ref moves, ref moveIndex, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhiteQueenMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateWhiteQueenMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.GenerateBlackKingMoves(ref moves, ref moveIndex, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return moveIndex;
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
                board.GenerateBlackPawnMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackPawnMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackKnightMoves(ref moves, ref moveIndex, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackBishopMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackBishopMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackRookMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackRookMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackQueenMoves(ref moves, ref moveIndex, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.GenerateBlackQueenMoves(ref moves, ref moveIndex, index,  0xFFFFFFFFFFFFFFFF);
            }
        }

        return moveIndex;
    }

  
}