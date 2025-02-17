using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public unsafe ulong AccumulateBlackMovesBulkCount()
    {
        var checkers = WhiteCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

   
        var nodes = AccumulateBlackKingMovesBulkCount( numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            return nodes;
        }

        MoveMask = 0xFFFFFFFFFFFFFFFF;
        if (numCheckers == 1)
        {
            MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + BlackKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
        }
        var pinMask = BlackKingPinnedRay();
        var occupancy = White | Black;
        var positions = Black & Pawn & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackPawnMovesBulkCount( index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackPawnMovesBulkCount( index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = Black & Knight & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(*(AttackTables.KnightAttackTable + index) & MoveMask & ~Black);
        }

        positions = Black & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextBishopAttacks(occupancy, index) & MoveMask & AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) & ~Black);
        }

        positions = Black & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextBishopAttacks(occupancy, index) & MoveMask & ~Black);
        }
        
        positions = Black & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextRookAttacks(occupancy, index) & MoveMask & AttackTables.GetRayToEdgeStraight(BlackKingPos, index) & ~Black);
        }
        
        positions = Black & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount(AttackTables.PextRookAttacks(occupancy, index) & MoveMask & ~Black);
        }
        
        positions = Black & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount((AttackTables.PextBishopAttacks(occupancy, index) |
                     AttackTables.PextRookAttacks(occupancy, index)) & MoveMask & (AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(BlackKingPos, index)) & ~Black);
        }
        
        positions = Black & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += (ulong)BitOperations.PopCount((AttackTables.PextBishopAttacks(occupancy, index) |
                     AttackTables.PextRookAttacks(occupancy, index)) & MoveMask & ~Black);
        }

        return nodes;
    }
    public unsafe ulong AccumulateBlackPawnMovesBulkCount(int index, ulong pushPinMask, ulong capturePinMask)
    {
        ulong nodes = 0;
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSecondRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;
            nodes += (ulong)BitOperations.PopCount(validMoves) * 4;


            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            nodes += (ulong)BitOperations.PopCount(validMoves) * 4;
        }
        else
        {
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;
            nodes += (ulong)BitOperations.PopCount(validMoves);

            if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                var newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.BlackEnpassantOffset + EnPassantFile;

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhiteSliders(newBoard.BlackKingPos))
                {
                    nodes++;
                }
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();

                if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 4)
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

    public unsafe ulong AccumulateBlackKingMovesBulkCount(bool inCheck)
    {
        ulong nodes = 0;

        var attackedSquares = BlackKingDangerSquares();
        var potentialMoves = *(AttackTables.KingAttackTable + BlackKingPos) & ~attackedSquares & ~Black;
        nodes += (ulong)BitOperations.PopCount(potentialMoves);

        if (BlackKingPos != 60 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return nodes;


        // King Side Castle
        if ((CastleRights & CastleRights.BlackKingSide) != 0 &&
            ((White | Black)& Constants.BlackKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & ((1ul << 61) | 1ul << 62)) == 0)
        {
            nodes++;
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.BlackQueenSide) != 0 &&
            ((White | Black) & Constants.BlackQueenSideCastleEmptyPositions) == 0 &&
            (attackedSquares & ((1ul << 58) | (1ul << 59))) == 0)
        {
            nodes++;
        }
        return nodes;

    }
}