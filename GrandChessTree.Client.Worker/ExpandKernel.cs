using GrandChessTree.Shared.Precomputed;
using ILGPU;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared;

namespace GrandChessTree.Client.Worker
{
    public static class ExpandKernel
    {

        public static void ExpandLayerKernel(Index1D index, BoardLayerBuffers layer, BoardLayerBuffers outputLayer, GpuAttackTable gpuAttackTables, TotalStatsLayerBuffers stats)
        {
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

            // Generate all possible moves and write them to the output

            var kingMoves = gpuAttackTables.KingAttackTable[whitKing] & ~attackedSquares;
            positions = kingMoves & black;
            while (positions != 0)
            {
                var toSquare = positions.PopLSB();
                var captureMask = (1UL << toSquare);

                var mask = (1UL << whitKing) | (1UL << toSquare);

                // King capture
                outputLayer.PawnOccupancy[outputIndex] = pawn & ~captureMask; // remove captured piece
                outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // removed captured piece
                outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask; // remove captured piece
                outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move king
                outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                outputLayer.WhiteKingPos[outputIndex] = (byte)toSquare;// Move king
                outputLayer.BlackKingPos[outputIndex] = blackKing;
                outputLayer.CastleRights[outputIndex] = (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide));
                outputLayer.EnPassantFile[outputIndex] = 8; // reset

                stats.Captures[outputIndex] = 1;
                outputIndex++;
            }

            positions = kingMoves & ~occupancy;
            while (positions != 0)
            {
                var toSquare = positions.PopLSB();
                var mask = (1UL << whitKing) | (1UL << toSquare);

                outputLayer.PawnOccupancy[outputIndex] = pawn;
                outputLayer.KnightOccupancy[outputIndex] = knight; // move
                outputLayer.BishopOccupancy[outputIndex] = bishop;
                outputLayer.RookOccupancy[outputIndex] = rook;
                outputLayer.QueenOccupancy[outputIndex] = queen;
                outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                outputLayer.BlackOccupancy[outputIndex] = black;
                outputLayer.WhiteKingPos[outputIndex] = (byte)toSquare;
                outputLayer.BlackKingPos[outputIndex] = blackKing;
                outputLayer.CastleRights[outputIndex] = (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide));
                outputLayer.EnPassantFile[outputIndex] = 8; // reset
                outputIndex++;
            }

            if (whitKing == 4 && numCheckers == 0)
            {
                if ((castleRights & (byte)CastleRights.WhiteKingSide) != 0 &&
                   (white & rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
                   (occupancy & Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
                   (attackedSquares & (1ul << 6)) == 0 &&
                   (attackedSquares & (1ul << 5)) == 0)
                {
                    var mask = (1UL << whitKing) | (1UL << 5);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight; // move
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = (byte)5;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide));
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;

                    stats.Castles[outputIndex] = 1;

                }

                // Queen Side Castle
                if ((castleRights & (byte)CastleRights.WhiteQueenSide) != 0 &&
                    (white & rook & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
                    (occupancy & Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
                    (attackedSquares & (1ul << 2)) == 0 &&
                    (attackedSquares & (1ul << 3)) == 0)
                {
                    var mask = (1UL << whitKing) | (1UL << 3);
                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight; // move
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = (byte)3;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide));
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                    stats.Castles[outputIndex] = 1;
                }
            }

            positions = white & knight & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var potentialMoves = gpuAttackTables.KnightAttackTable[fromSquare] & moveMask & ~white;
                var captureMoves = potentialMoves & black;

                while (captureMoves != 0)
                {
                    var toSquare = captureMoves.PopLSB();
                    var captureMask = (1UL << toSquare);

                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    // Knight capture
                    outputLayer.PawnOccupancy[outputIndex] = pawn & ~captureMask; // remove captured piece
                    outputLayer.KnightOccupancy[outputIndex] = knight & ~(1UL << fromSquare); // only remove the knight from it's initial sqaure
                    outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // removed captured piece
                    outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                    outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask; // remove captured piece
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;
                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                }
            }

            positions = white & bishop & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, fromSquare);
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;
                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset

                    outputIndex++;
                }
            }

            positions = white & bishop & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) & moveMask;
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;

                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                }
            }

            positions = white & rook & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, fromSquare) & moveMask & gpuAttackTables.GetRayToEdgeStraight(whitKing, fromSquare);
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;

                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                }
            }

            positions = white & rook & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, fromSquare) & moveMask;
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;

                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                }
            }

            positions = white & queen & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) |
                         gpuAttackTables.PextRookAttacks(occupancy, fromSquare)) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, fromSquare) | gpuAttackTables.GetRayToEdgeStraight(whitKing, fromSquare);
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;

                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                }
            }

            positions = white & queen & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) |
                         gpuAttackTables.PextRookAttacks(occupancy, fromSquare)) & moveMask;
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    stats.Captures[outputIndex] = 1;

                    outputIndex++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = (1UL << fromSquare) | (1UL << toSquare);

                    outputLayer.PawnOccupancy[outputIndex] = pawn;
                    outputLayer.KnightOccupancy[outputIndex] = knight ^ mask; // move knight
                    outputLayer.BishopOccupancy[outputIndex] = bishop;
                    outputLayer.RookOccupancy[outputIndex] = rook;
                    outputLayer.QueenOccupancy[outputIndex] = queen;
                    outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move knight
                    outputLayer.BlackOccupancy[outputIndex] = black;
                    outputLayer.WhiteKingPos[outputIndex] = whitKing;
                    outputLayer.BlackKingPos[outputIndex] = blackKing;
                    outputLayer.CastleRights[outputIndex] = castleRights;
                    outputLayer.EnPassantFile[outputIndex] = 8; // reset
                    outputIndex++;
                }
            }

            positions = white & pawn & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var rankIndex = SquareHelpers.GetRankIndex(fromSquare);

                if (rankIndex.IsSeventhRank())
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, fromSquare);
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = (1UL << toSquare);

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // knight promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight; // replaced captured with promoted
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // remove captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask;// remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // bishop promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop; // replaced captured with promoted
                        outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask;// remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // rook promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // remove captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook; // replaced captured with promoted
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask;// remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // queen promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // remove captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen; // replaced captured with promoted
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~(occupancy) & gpuAttackTables.GetRayToEdgeStraight(whitKing, fromSquare);
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // knight promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight | (1UL << toSquare); // promote
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // bishop promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop | (1UL << toSquare); // promote
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // rook promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook | (1UL << toSquare); // promote
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // queen promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen | (1UL << toSquare); // promote
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;
                    }
                }
                else
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black & gpuAttackTables.GetRayToEdgeDiagonal(whitKing, fromSquare);
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = (1UL << toSquare);

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // capture
                        outputLayer.PawnOccupancy[outputIndex] = pawn ^ mask; // move
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // removed captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // removed captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask; // remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8; // reset
                        stats.Captures[outputIndex] = 1;
                        outputIndex++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~(occupancy) & gpuAttackTables.GetRayToEdgeStraight(whitKing, fromSquare);
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // move
                        outputLayer.PawnOccupancy[outputIndex] = pawn ^ mask; // move
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8; // reset
                        outputIndex++;

                    }
                }

            }

            positions = white & pawn & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var rankIndex = SquareHelpers.GetRankIndex(fromSquare);

                if (rankIndex.IsSeventhRank())
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black;
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = (1UL << toSquare);

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // knight promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight; // replaced captured with promoted
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // remove captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask;// remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // bishop promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop; // replaced captured with promoted
                        outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask;// remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // rook promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // remove captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook; // replaced captured with promoted
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask;// remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // queen promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // remove captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // remove captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen; // replaced captured with promoted
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Captures[outputIndex] = 1;
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~(occupancy);
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // knight promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight | (1UL << toSquare); // promote
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // bishop promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop | (1UL << toSquare); // promote
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // rook promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook | (1UL << toSquare); // promote
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;

                        // queen promotion
                        outputLayer.PawnOccupancy[outputIndex] = pawn & ~(1UL << fromSquare); // remove pawn
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen | (1UL << toSquare); // promote
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move piece
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8;// reset
                        stats.Promotions[outputIndex] = 1;
                        outputIndex++;
                    }
                }
                else
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black;
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = (1UL << toSquare);

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // capture
                        outputLayer.PawnOccupancy[outputIndex] = pawn ^ mask; // move
                        outputLayer.KnightOccupancy[outputIndex] = knight & ~captureMask; // removed captured piece
                        outputLayer.BishopOccupancy[outputIndex] = bishop & ~captureMask; // removed captured piece
                        outputLayer.RookOccupancy[outputIndex] = rook & ~captureMask; // remove captured piece
                        outputLayer.QueenOccupancy[outputIndex] = queen & ~captureMask; // remove captured piece
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                        outputLayer.BlackOccupancy[outputIndex] = black & ~captureMask; // remove captured piece
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8; // reset
                        stats.Captures[outputIndex] = 1;
                        outputIndex++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~(occupancy);
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = (1UL << fromSquare) | (1UL << toSquare);

                        // move
                        outputLayer.PawnOccupancy[outputIndex] = pawn ^ mask; // move
                        outputLayer.KnightOccupancy[outputIndex] = knight;
                        outputLayer.BishopOccupancy[outputIndex] = bishop;
                        outputLayer.RookOccupancy[outputIndex] = rook;
                        outputLayer.QueenOccupancy[outputIndex] = queen;
                        outputLayer.WhiteOccupancy[outputIndex] = white ^ mask; // move
                        outputLayer.BlackOccupancy[outputIndex] = black;
                        outputLayer.WhiteKingPos[outputIndex] = whitKing;
                        outputLayer.BlackKingPos[outputIndex] = blackKing;
                        outputLayer.CastleRights[outputIndex] = castleRights;
                        outputLayer.EnPassantFile[outputIndex] = 8; // reset
                        outputIndex++;

                    }
                }


                // TODO Enpassant
            }

        }

    }
}
