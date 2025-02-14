using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    private unsafe ulong AccumulateWhiteMovesBulkCount()
    {
        var checkers = BlackCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        var nodes = AccumulateWhiteKingMovesBulkCount(numCheckers > 0);

        if (numCheckers > 1)
        {
            return nodes;
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
            nodes += AccumulateWhitePawnMovesBulkCount(index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhitePawnMovesBulkCount(index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Knight & ~pinMask;
        while (positions != 0)
        {
            nodes += AccumulateWhiteKnightMovesBulkCount(positions.PopLSB());
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteBishopMovesBulkCount(index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteBishopMovesBulkCount(index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteRookMovesBulkCount(index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteRookMovesBulkCount(index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteQueenMovesBulkCount(index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }

        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteQueenMovesBulkCount(index, 0xFFFFFFFFFFFFFFFF);
        }

        return nodes;
    }

    public unsafe ulong AccumulateWhitePawnMovesBulkCount( int index, ulong pushPinMask, ulong capturePinMask)
    {
        ulong nodes = 0;
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSeventhRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.WhitePawnAttackTable +index) & MoveMask & Black & capturePinMask;
            nodes += (ulong)BitOperations.PopCount(validMoves) * 4;

            validMoves = *(AttackTables.WhitePawnPushTable + index) & MoveMask & ~(White | Black) & pushPinMask;
            nodes += (ulong)BitOperations.PopCount(validMoves) * 4;
        }
        else
        {
            var validMoves = *(AttackTables.WhitePawnAttackTable + index) & MoveMask & Black & capturePinMask;
            nodes += (ulong)BitOperations.PopCount(validMoves);

            if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                var newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

                newBoard.WhitePawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                {
                    nodes++;
                }
            }

            validMoves = AttackTables.WhitePawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();

                if (rankIndex.IsSecondRank() && toSquare.GetRankIndex() == 3)
                {
                    // Double push: Check intermediate square
                    var intermediateSquare = (index + toSquare) / 2; // Midpoint between start and destination
                    if (((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue; // Intermediate square is blocked, skip this move
                    }
                }

                nodes++;
            }
        }
             return nodes;

    }

    public unsafe ulong AccumulateWhiteKnightMovesBulkCount( int index)
    {
        ulong nodes = 0;
        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & Black;
        nodes += (ulong)BitOperations.PopCount(captureMoves);


        var emptyMoves = potentialMoves & ~(White | Black);
        nodes += (ulong)BitOperations.PopCount(emptyMoves);

        return nodes;

    }

    public unsafe ulong AccumulateWhiteBishopMovesBulkCount( int index, ulong pinMask)
    {
        ulong nodes = 0;
        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;
        var captureMoves = potentialMoves & Black;
        nodes += (ulong)BitOperations.PopCount(captureMoves);

        var emptyMoves = potentialMoves & ~(White | Black);
        nodes += (ulong)BitOperations.PopCount(emptyMoves);

        return nodes;

    }

    public unsafe ulong AccumulateWhiteRookMovesBulkCount( int index, ulong pinMask)
    {
        ulong nodes = 0;
        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        var captureMoves = potentialMoves & Black;
        nodes += (ulong)BitOperations.PopCount(captureMoves);


        var emptyMoves = potentialMoves & ~(White | Black);
        nodes += (ulong)BitOperations.PopCount(emptyMoves);

        return nodes;

    }

    public unsafe ulong AccumulateWhiteQueenMovesBulkCount( int index, ulong pinMask)
    {
        ulong nodes = 0;

        var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                             AttackTables.PextRookAttacks(White | Black, index)) & MoveMask & pinMask;
        
        var captureMoves = potentialMoves & Black;
        nodes += (ulong)BitOperations.PopCount(captureMoves);


        var emptyMoves = potentialMoves & ~(White | Black);
        nodes += (ulong)BitOperations.PopCount(emptyMoves);

        return nodes;

    }

    public unsafe ulong AccumulateWhiteKingMovesBulkCount( bool inCheck)
    {
        ulong nodes = 0;
        var attackedSquares = WhiteKingDangerSquares();

        var potentialMoves = *(AttackTables.KingAttackTable + WhiteKingPos) & ~attackedSquares;
        var captureMoves = potentialMoves & Black;
        nodes += (ulong)BitOperations.PopCount(captureMoves);


        var emptyMoves = potentialMoves & ~(White | Black);
        nodes += (ulong)BitOperations.PopCount(emptyMoves);


        if (WhiteKingPos != 4 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return nodes;


        if ((CastleRights & CastleRights.WhiteKingSide) != 0 &&
            (White & Rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 6)) == 0 &&
            (attackedSquares & (1ul << 5)) == 0)
        {
            nodes++;
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.WhiteQueenSide) != 0 &&
            (White & Rook & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
              (attackedSquares & (1ul << 2)) == 0 &&
            (attackedSquares & (1ul << 3)) == 0)
        {
            nodes++;
        }

        return nodes;
    }
}