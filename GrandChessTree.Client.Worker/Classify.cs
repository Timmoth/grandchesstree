using GrandChessTree.Client.Worker.Kernels;
using GrandChessTree.Shared.Precomputed;
using ILGPU;
using ILGPU.Backends.OpenCL;
using static ILGPU.IntrinsicMath;

namespace GrandChessTree.Client.Worker
{
    public static class Classify
    {
        public static void ClassifyNodeKernel(ulong pawn, ulong knight, ulong bishop, ulong rook, ulong queen,
           ulong white, ulong black, byte whitKing, byte blackKing, byte castleRights, byte EnpassantFile,
           GpuAttackTable gpuAttackTables,
           ref ulong DirectCheck,
           ref ulong SingleDiscoveredCheck,
           ref ulong DirectDiscoveredCheck
            , ref ulong DoubleDiscoveredCheck
            , ref ulong DirectCheckmate
            , ref ulong SingleDiscoveredCheckmate
            , ref ulong DirectDiscoverdCheckmate
            , ref ulong DoubleDiscoverdCheckmate)
        {
            var occupancy = white | black;
            var diagonalSliders = (bishop | queen);
            var straightSliders = (rook | queen);

            var checkers = (gpuAttackTables.PextBishopAttacks(occupancy, whitKing) & (black & diagonalSliders)) |
               (gpuAttackTables.PextRookAttacks(occupancy, whitKing) & (black & straightSliders)) |
               (gpuAttackTables.KnightAttackTable[whitKing] & black & knight) |
               (gpuAttackTables.WhitePawnAttackTable[whitKing] & black & pawn);

            var numCheckers = IntrinsicMath.PopCount(checkers);

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
                    DirectCheckmate++;
                }
                else
                {
                    // check king can move
                    DirectCheck++;
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
                    DirectCheck++;
                }
                else
                {
                    // Mate
                    DirectCheckmate++;
                }
            }
        }
    }
}
