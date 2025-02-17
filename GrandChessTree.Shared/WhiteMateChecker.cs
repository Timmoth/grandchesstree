using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public partial struct Board
{
    private unsafe bool WhiteCanEvadeCheck()
    {
        if (CanWhiteKingMove())
            return true;

        var pinMask = WhiteKingPinnedRay();

        var positions = White & Knight;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            if ((pinMask & (1ul << index)) != 0)
            {
                continue;
            }

            var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
            // Return true if there are any valid capture or push moves available.
            if ((potentialMoves & ~White) != 0)
            {
                return true;
            }
        }


        positions = White & Bishop;
        while (positions != 0)
        {
            var index = positions.PopLSB();

            var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask;

            if ((pinMask & (1ul << index)) != 0)
            {
                potentialMoves &= AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index);
            }
            // Return true if there are any valid capture or push moves available.
            if ((potentialMoves & ~White) != 0)
            {
                return true;
            }
        }

        positions = White & Rook;
        while (positions != 0)
        {
            var index = positions.PopLSB();

            var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask;

            if ((pinMask & (1ul << index)) != 0)
            {
                potentialMoves &= AttackTables.GetRayToEdgeStraight(WhiteKingPos, index);
            }

            if ((potentialMoves & ~White) != 0)
            {
                return true;
            }
        }

        positions = White & Queen;
        while (positions != 0)
        {
            var index = positions.PopLSB();

            var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                     AttackTables.PextRookAttacks(White | Black, index)) & MoveMask;

            if ((pinMask & (1ul << index)) != 0)
            {
                potentialMoves &= AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index);
            }
            // Return true if there are any valid capture or push moves available.
            if ((potentialMoves & ~White) != 0)
            {
                return true;
            }
        }

        positions = White & Pawn;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            if (CanWhitePawnMove(index, (pinMask & (1ul << index)) != 0))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe bool CanWhitePawnMove(int index, bool isPinned)
    {
        var validMoves = *(AttackTables.WhitePawnAttackTable+index) & MoveMask & Black;
        if (isPinned)
        {
            validMoves &= AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index);
        }

        if (validMoves != 0)
        {
            return true;
        }

        validMoves = *(AttackTables.WhitePawnPushTable+index) & MoveMask & ~(White | Black);
        if (isPinned)
        {
            validMoves &= AttackTables.GetRayToEdgeStraight(WhiteKingPos, index);
        }

        var rankIndex = SquareHelpers.GetRankIndex(index);
        while (validMoves != 0)
        {
            var toSquare = validMoves.PopLSB();

            if (rankIndex.IsSecondRank() && SquareHelpers.GetRankIndex(toSquare) == 3)
            {
                // Double push: Check intermediate square
                var intermediateSquare = (index + toSquare) / 2; // Midpoint between start and destination
                if (((White | Black) & (1UL << intermediateSquare)) != 0)
                {
                    continue; // Intermediate square is blocked, skip this move
                }
                return true;
            }

            // single push
            return true;
        }

        if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
            Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
        {
            Board newBoard = default;
            newBoard = Unsafe.As<Board, Board>(ref this);

            var toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

            newBoard.WhitePawn_Enpassant(index, toSquare);
            if (!newBoard.IsAttackedByBlackSliders(newBoard.WhiteKingPos))
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe bool CanWhiteKingMove()
    {
        var potentialMoves = *(AttackTables.KingAttackTable + WhiteKingPos) & ~WhiteKingDangerSquares();
        // Return true if there are any valid capture or push moves available.
        return (potentialMoves & ~White) != 0;
    }
 
}