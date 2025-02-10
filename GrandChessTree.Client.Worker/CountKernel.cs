using GrandChessTree.Shared.Precomputed;
using ILGPU;
using static ILGPU.IntrinsicMath;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared;

namespace GrandChessTree.Client.Worker
{
    public static class CountKernel
   {

        public static void CountMovesKernel(Index1D index, BoardLayerBuffers layer, GpuAttackTable gpuAttackTables)
        {
            int moveCount = 0;

            ulong pawn = layer.PawnOccupancy[index];
            ulong knight = layer.KnightOccupancy[index];
            ulong bishop = layer.BishopOccupancy[index];
            ulong rook = layer.RookOccupancy[index];
            ulong queen = layer.QueenOccupancy[index];
            ulong white = layer.WhiteOccupancy[index];
            ulong black = layer.BlackOccupancy[index];
            byte whitKing = layer.WhiteKingPos[index];
            byte blackKing = layer.BlackKingPos[index];
            byte castleRights = layer.CastleRights[index];
            byte EnPassantFile = layer.EnPassantFile[index];

            var occupancy = white | black;
            var diagonalSliders = (bishop | queen);
            var straightSliders = (rook | queen);


            var checkers = (gpuAttackTables.PextBishopAttacks(occupancy, whitKing) & (black & diagonalSliders)) |
               (gpuAttackTables.PextRookAttacks(occupancy, whitKing) & (black & straightSliders)) |
               (gpuAttackTables.KnightAttackTable[whitKing] & black & knight) |
               (gpuAttackTables.WhitePawnAttackTable[whitKing] & black & pawn);

            var numCheckers = IntrinsicMath.PopCount(checkers);

            var moveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF : checkers | gpuAttackTables.LineBitBoardsInclusive[whitKing * 64 + IntrinsicMath.TrailingZeroCount(checkers)];
            var pinMask =
                gpuAttackTables.DetectPinsDiagonal(whitKing, (black & diagonalSliders), occupancy) |
                gpuAttackTables.DetectPinsStraight(whitKing, (black & straightSliders), occupancy);

            // count the number of possible moves

            var attackedSquares = 0ul;

            var occupancyMinusKing = (occupancy) ^ (1ul << whitKing);

            var positions = black & pawn;
            while (positions != 0) attackedSquares |= gpuAttackTables.BlackPawnAttackTable[positions.PopLSB()];

            positions = black & knight;
            while (positions != 0) attackedSquares |= gpuAttackTables.KnightAttackTable[positions.PopLSB()];

            positions = black & diagonalSliders;
            while (positions != 0) attackedSquares |= gpuAttackTables.PextBishopAttacks(occupancyMinusKing, positions.PopLSB());

            positions = black & straightSliders;
            while (positions != 0) attackedSquares |= gpuAttackTables.PextRookAttacks(occupancyMinusKing, positions.PopLSB());

            attackedSquares |= gpuAttackTables.KingAttackTable[blackKing];

            var kingMoves = gpuAttackTables.KingAttackTable[whitKing] & ~attackedSquares;
            moveCount += IntrinsicMath.PopCount(kingMoves & black);
            moveCount += IntrinsicMath.PopCount(kingMoves & ~occupancy);

            if (whitKing == 4 && numCheckers == 0)
            {
                if ((castleRights & (byte)CastleRights.WhiteKingSide) != 0 &&
                   (white & rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
                   (occupancy & Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
                   (attackedSquares & (1ul << 6)) == 0 &&
                   (attackedSquares & (1ul << 5)) == 0)
                {
                    moveCount++;
                }

                // Queen Side Castle
                if ((castleRights & (byte)CastleRights.WhiteQueenSide) != 0 &&
                    (white & rook & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
                    (occupancy & Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
                      (attackedSquares & (1ul << 2)) == 0 &&
                    (attackedSquares & (1ul << 3)) == 0)
                {
                    moveCount++;
                }
            }

            positions = white & knight & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();
                var potentialMoves = gpuAttackTables.KnightAttackTable[square] & moveMask;
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & bishop & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, square) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, square);
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & bishop & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, square) & moveMask;
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & rook & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, square) & moveMask & gpuAttackTables.GetRayToEdgeStraight(whitKing, square);
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & rook & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, square) & moveMask;
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & queen & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, square) |
                         gpuAttackTables.PextRookAttacks(occupancy, square)) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, square) | gpuAttackTables.GetRayToEdgeStraight(whitKing, square);
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & queen & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, square) |
                         gpuAttackTables.PextRookAttacks(occupancy, square)) & moveMask;
                moveCount += IntrinsicMath.PopCount(potentialMoves & ~white);
            }

            positions = white & pawn & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();
                var validMoves = gpuAttackTables.WhitePawnAttackTable[square] & moveMask & black & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, square);
                moveCount += IntrinsicMath.PopCount(validMoves);
                validMoves = gpuAttackTables.WhitePawnPushTable[square] & moveMask & ~(occupancy) & gpuAttackTables.GetRayToEdgeStraight(whitKing, square);
                var rankIndex = SquareHelpers.GetRankIndex(square);
                while (validMoves != 0)
                {
                    var toSquare = validMoves.PopLSB();

                    if (rankIndex.IsSecondRank() && SquareHelpers.GetRankIndex(toSquare) == 3)
                    {
                        // Double push: Check intermediate square
                        var intermediateSquare = (square + toSquare) / 2; // Midpoint between start and destination
                        if (((occupancy) & (1UL << intermediateSquare)) != 0)
                        {
                            continue; // Intermediate square is blocked, skip this move
                        }
                    }

                    // single push
                    moveCount++;
                }


                if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                    Math.Abs(square.GetFileIndex() - EnPassantFile) == 1)
                {
                    // Todo - some illegal moves possible here (pinned / discovered)
                    moveCount++;
                }
            }

            positions = white & pawn & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();
                var validMoves = gpuAttackTables.WhitePawnAttackTable[square] & moveMask & black;
                moveCount += IntrinsicMath.PopCount(validMoves);

                validMoves = gpuAttackTables.WhitePawnPushTable[square] & moveMask & ~(occupancy);

                var rankIndex = SquareHelpers.GetRankIndex(square);
                while (validMoves != 0)
                {
                    var toSquare = validMoves.PopLSB();

                    if (rankIndex.IsSecondRank() && SquareHelpers.GetRankIndex(toSquare) == 3)
                    {
                        // Double push: Check intermediate square
                        var intermediateSquare = (square + toSquare) / 2; // Midpoint between start and destination
                        if (((occupancy) & (1UL << intermediateSquare)) != 0)
                        {
                            continue; // Intermediate square is blocked, skip this move
                        }
                    }

                    // single push
                    moveCount++;
                }

                if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                    Math.Abs(square.GetFileIndex() - EnPassantFile) == 1)
                {
                    // Todo - some illegal moves possible here (pinned / discovered)
                    moveCount++;
                }
            }

            layer.PositionIndexes[index] = moveCount;
        }


    }
}
