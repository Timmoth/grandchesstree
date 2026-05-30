using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public partial struct Board
{
    public unsafe bool BlackCanEvadeSingleCheck()
    {
        if (CanBlackKingMove())
            return true;

        var pinMask = BlackKingPinnedRay();

        var positions = Black & Knight;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            if ((pinMask & (1ul << index)) != 0)
            {
                continue;
            }

            var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
            // Return true if there are any valid capture or push moves available.
            if ((potentialMoves & ~Black) != 0)
            {
                return true;
            }
        }


        positions = Black & Bishop;
        while (positions != 0)
        {
            var index = positions.PopLSB();

            var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask;

            if ((pinMask & (1ul << index)) != 0)
            {
                potentialMoves &= AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index);
            }
            // Return true if there are any valid capture or push moves available.
            if ((potentialMoves & ~Black) != 0)
            {
                return true;
            }
        }

        positions = Black & Rook;
        while (positions != 0)
        {
            var index = positions.PopLSB();

            var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask;

            if ((pinMask & (1ul << index)) != 0)
            {
                potentialMoves &= AttackTables.GetRayToEdgeStraight(BlackKingPos, index);
            }

            if ((potentialMoves & ~Black) != 0)
            {
                return true;
            }
        }

        positions = Black & Queen;
        while (positions != 0)
        {
            var index = positions.PopLSB();

            var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                     AttackTables.PextRookAttacks(White | Black, index)) & MoveMask;

            if ((pinMask & (1ul << index)) != 0)
            {
                potentialMoves &= AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(BlackKingPos, index);
            }
            // Return true if there are any valid capture or push moves available.
            if ((potentialMoves & ~Black) != 0)
            {
                return true;
            }
        }

        positions = Black & Pawn;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            if (CanBlackPawnMove(index, (pinMask & (1ul << index)) != 0))
                return true;
        }


        return false;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe bool CanBlackPawnMove(int index, bool isPinned)
    {
        var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White;
        if (isPinned)
        {
            validMoves &= AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index);
        }

        if (validMoves != 0)
        {
            return true;
        }

        validMoves = *(AttackTables.BlackPawnPushTable + index) & MoveMask & ~(White | Black);
        if (isPinned)
        {
            validMoves &= AttackTables.GetRayToEdgeStraight(BlackKingPos, index);
        }
        var rankIndex = index.GetRankIndex();

        while (validMoves != 0)
        {
            var toSquare = validMoves.PopLSB();

            if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 4)
            {
                // Double push: Check intermediate square
                var intermediateSquare = (index + toSquare) / 2; // Midpoint between start and destination
                if (((White | Black) & (1UL << intermediateSquare)) != 0)
                {
                    continue; // Intermediate square is blocked, skip this move
                }

                return true;
            }
            else
            {
                // single push
                return true;
            }
        }

        if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
            Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
        {
            Board newBoard = default;
            newBoard = Unsafe.As<Board, Board>(ref this);

            var toSquare = Constants.BlackEnpassantOffset + EnPassantFile;

            newBoard.BlackPawn_Enpassant(index, toSquare);
            if (!newBoard.IsAttackedByWhiteSliders(newBoard.BlackKingPos))
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool CanBlackKingMove()
    {
        var potentialMoves = *(AttackTables.KingAttackTable + BlackKingPos) & ~BlackKingDangerSquares();
        // Return true if there are any valid capture or push moves available.
        return (potentialMoves & ~Black) != 0;
    }
}