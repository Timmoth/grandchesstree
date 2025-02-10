using GrandChessTree.Shared.Precomputed;
using ILGPU;
using static ILGPU.IntrinsicMath;

namespace GrandChessTree.Client.Worker
{
    public static class Classify
    {
        public static void ClassifyNodeKernel(Index1D index, BoardLayerBuffers layer, TotalStatsLayerBuffers stats, GpuAttackTable gpuAttackTables)
        {
            // Determine the node type and increment the board for each.
            int outputIndex = layer.PositionIndexes[index];

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
            byte enPassantFile = layer.EnPassantFile[index];

            var occupancy = white | black;
            var diagonalSliders = (bishop | queen);
            var straightSliders = (rook | queen);

            var checkers = (gpuAttackTables.PextBishopAttacks(occupancy, whitKing) & (black & diagonalSliders)) |
               (gpuAttackTables.PextRookAttacks(occupancy, whitKing) & (black & straightSliders)) |
               (gpuAttackTables.KnightAttackTable[whitKing] & black & knight) |
               (gpuAttackTables.WhitePawnAttackTable[whitKing] & black & pawn);

            var numCheckers = IntrinsicMath.PopCount(checkers);
            stats.Nodes[index] = 1;

            if (numCheckers == 0)
            {
                return;
            }

            var moveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF : checkers | gpuAttackTables.LineBitBoardsInclusive[whitKing * 64 + IntrinsicMath.TrailingZeroCount(checkers)];
            var pinMask =
                gpuAttackTables.DetectPinsDiagonal(whitKing, (black & diagonalSliders), occupancy) |
                gpuAttackTables.DetectPinsStraight(whitKing, (black & straightSliders), occupancy);

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

            if (numCheckers > 1)
            {
                // Multiple checkers
                var potentialMoves = gpuAttackTables.KingAttackTable[whitKing] & ~attackedSquares;
                if ((potentialMoves & ~white) == 0)
                {
                    // mate - king can't move
                    stats.DirectCheckmate[index] = 1;
                }
                else
                {
                    // check king can move
                    stats.DirectCheck[index] = 1;
                }
            }
            else
            {
                // Can evade check?
                bool canEvadeCheck = false;

                var potentialMoves = gpuAttackTables.KingAttackTable[whitKing] & ~attackedSquares;
                canEvadeCheck |= (potentialMoves & ~white) != 0;

                // todo - can any moves evade check?

                if (canEvadeCheck)
                {
                    // Check
                    stats.DirectCheck[index] = 1;
                }
                else
                {
                    // Mate
                    stats.DirectCheckmate[index] = 1;
                }
            }
        }
    }
}
