using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public unsafe ulong AccumulateWhiteMovesBulkCount()
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
        var occupancy = White | Black;

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
            nodes += (ulong)BitOperations.PopCount(*(AttackTables.KnightAttackTable + positions.PopLSB()) & MoveMask & ~White);
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextBishopAttacks(occupancy, index) & MoveMask & AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) & ~White);
        }

        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextBishopAttacks(occupancy, index) & MoveMask & ~White);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextRookAttacks(occupancy, index) & MoveMask & AttackTables.GetRayToEdgeStraight(WhiteKingPos, index) & ~White);

        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextRookAttacks(occupancy, index) & MoveMask & ~White);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount((AttackTables.PextBishopAttacks(occupancy, index) |
                      AttackTables.PextRookAttacks(occupancy, index)) & MoveMask & (AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index)) & ~White);

        }

        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount((AttackTables.PextBishopAttacks(occupancy, index) |
                      AttackTables.PextRookAttacks(occupancy, index)) & MoveMask & ~White);
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
                // Inline legality check: simulate the EP capture's effect on occupancy and
                // probe black slider attacks on the king. Avoids a full Board copy + the
                // hash/piece updates that WhitePawn_Enpassant performs.
                ulong fromBit = 1UL << index;
                ulong toBit = 1UL << (Constants.WhiteEnpassantOffset + EnPassantFile);
                ulong captureBit = 1UL << (4 * 8 + EnPassantFile); // black pawn on rank 4 (0-idx)
                ulong occAfter = ((White ^ fromBit) | toBit) | (Black & ~captureBit);
                ulong blackAfter = Black & ~captureBit;
                if ((AttackTables.PextBishopAttacks(occAfter, WhiteKingPos) & (blackAfter & (Bishop | Queen))) == 0 &&
                    (AttackTables.PextRookAttacks(occAfter, WhiteKingPos) & (blackAfter & (Rook | Queen))) == 0)
                {
                    nodes++;
                }
            }

            // Branchless: validMoves can contain (index+8) single-push and/or (index+16) double-push
            // for rank-2 pawns. Double-push requires the intermediate (index+8) to be empty —
            // independent of MoveMask, since the check-resolving mask might pass the landing
            // square (rank-4) while excluding the intermediate (rank-3).
            validMoves = AttackTables.WhitePawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            if (rankIndex.IsSecondRank() && ((White | Black) & (1UL << (index + 8))) != 0)
            {
                validMoves &= ~(1UL << (index + 16));
            }
            nodes += (ulong)BitOperations.PopCount(validMoves);
        }
             return nodes;

    }

    public unsafe ulong AccumulateWhiteKingMovesBulkCount( bool inCheck)
    {
        ulong nodes = 0;
        var attackedSquares = WhiteKingDangerSquares();

        var potentialMoves = *(AttackTables.KingAttackTable + WhiteKingPos) & ~attackedSquares & ~White;
        nodes += (ulong)BitOperations.PopCount(potentialMoves);


        if (WhiteKingPos != 4 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return nodes;


        if ((CastleRights & CastleRights.WhiteKingSide) != 0 &&
            ((White | Black)& Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & ((1ul << 6) | 1ul << 5)) == 0)

        {
            nodes++;
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.WhiteQueenSide) != 0 &&
            ((White | Black)& Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
            (attackedSquares & ((1ul << 2) | 1ul << 3)) == 0)

        {
            nodes++;
        }

        return nodes;
    }
}