using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Moves;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    private unsafe void GenerateWhiteMoves(ref Span<uint> moves, ref int moveIndex)
    {
        var checkers = BlackCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

         GenerateWhiteKingMoves(ref moves, ref moveIndex, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            return;
        }

        MoveMask = 0xFFFFFFFFFFFFFFFF;
        if (numCheckers == 1)
        {
            MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
        }
        var pinMask = WhiteKingPinnedRay();

        var positions = White & Pawn & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhitePawnMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhitePawnMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Knight & ~pinMask;
        while (positions != 0)
        {
             GenerateWhiteKnightMoves(ref moves, ref moveIndex, positions.PopLSB());
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhiteBishopMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhiteBishopMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhiteRookMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhiteRookMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhiteQueenMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }

        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             GenerateWhiteQueenMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
        }
    }

    public unsafe void GenerateWhitePawnMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pushPinMask, ulong capturePinMask)
    {
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSeventhRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.WhitePawnAttackTable +index) & MoveMask & Black & capturePinMask;

            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.KnightCapturePromotion);
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.BishopCapturePromotion);
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.RookCapturePromotion);
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.QueenCapturePromotion);
            }

            validMoves = *(AttackTables.WhitePawnPushTable + index) & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.KnightPromotion);
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.BishopPromotion);
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.RookPromotion);
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.QueenPromotion);
            }
        }
        else
        {
            var validMoves = *(AttackTables.WhitePawnAttackTable + index) & MoveMask & Black & capturePinMask;

            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Pawn, index, toSquare);
            }

            if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                var newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

                newBoard.WhitePawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByBlackSliders(newBoard.WhiteKingPos))
                {
                    moves[moveIndex++] = MoveExtensions.EncodeWhiteEnpassantMove(index, toSquare);
                }
            }

// Compute all valid pawn push moves for this pawn (including double pushes)
            validMoves = AttackTables.WhitePawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;

// Loop over each destination square (each set bit in validMoves)
            while (validMoves != 0)
            {
                // Pop the least-significant set bit; that gives us a destination square.
                toSquare = validMoves.PopLSB();
    
                // Create a new board instance for this move.

                // For White double-pushes, the pawn must be on its initial rank (second rank, index == 1)
                // and the destination must be two squares forward (rank 3). In that case, the intermediate
                // square is one rank ahead (index + 8) and must be unoccupied.
                int intermediateSquare = index + 8;
    
                // Compute a flag for whether this move is a double push.
                // (Using a ternary operator to yield 1 if true, 0 if false.)
                // Note: For white, a double push happens when the pawn starts on rank 1 (second rank)
                // and lands on rank 3, and the intermediate square is empty.
                if((rankIndex.IsSecondRank() && toSquare.GetRankIndex() == 3))
                {
                    if(((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue;
                    }
                    moves[moveIndex++] = MoveExtensions.EncodeDoublePush(index, toSquare);
                }
                else
                {
                    moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Pawn, index, toSquare);
                }
            }
          
        }
    }

    public unsafe void GenerateWhiteKnightMoves(ref Span<uint> moves, ref int moveIndex, int index)
    {
       
        int toSquare;
        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {    
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Knight, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Knight, index, toSquare);
        }
    }

    public unsafe void GenerateWhiteBishopMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pinMask)
    {
        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;

        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Bishop, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Bishop, index, toSquare);
        }
    }

    public unsafe void GenerateWhiteRookMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pinMask)
    {
        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Rook, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Rook, index, toSquare);
        }
    }

    public unsafe void GenerateWhiteQueenMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pinMask)
    {
        var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                             AttackTables.PextRookAttacks(White | Black, index)) & MoveMask & pinMask;
        
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Rook, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Queen, index, toSquare);
        }
    }

    public unsafe void GenerateWhiteKingMoves(ref Span<uint> moves, ref int moveIndex, bool inCheck)
    {
        var attackedSquares = WhiteKingDangerSquares();

        var potentialMoves = *(AttackTables.KingAttackTable + WhiteKingPos) & ~attackedSquares;
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.King, WhiteKingPos, toSquare);

        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.King, WhiteKingPos, toSquare);
        }

        if (WhiteKingPos != 4 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return;


        if ((CastleRights & CastleRights.WhiteKingSide) != 0 &&
            (White & Rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 6)) == 0 &&
            (attackedSquares & (1ul << 5)) == 0)
        {
            moves[moveIndex++] = MoveExtensions.EncodeCastleMove(WhiteKingPos, 6);
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.WhiteQueenSide) != 0 &&
            (White & Rook & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
              (attackedSquares & (1ul << 2)) == 0 &&
            (attackedSquares & (1ul << 3)) == 0)
        {
            moves[moveIndex++] = MoveExtensions.EncodeCastleMove(WhiteKingPos, 2);
        }

        return;
    }
}