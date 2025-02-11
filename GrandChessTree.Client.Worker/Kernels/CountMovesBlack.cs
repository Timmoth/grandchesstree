using GrandChessTree.Shared.Precomputed;
using ILGPU;
using static ILGPU.IntrinsicMath;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker.Kernels
{
    public static class CountMovesBlack
    {
        public static void CountL1MovesKernel(Index1D boardIndex, BoardLayerBuffers boards, GpuAttackTable gpuAttackTables)
        {
            // Calculate the number of moves for each board in the input layer
            ulong pawn = boards.PawnOccupancy[boardIndex];
            ulong knight = boards.KnightOccupancy[boardIndex];
            ulong bishop = boards.BishopOccupancy[boardIndex];
            ulong rook = boards.RookOccupancy[boardIndex];
            ulong queen = boards.QueenOccupancy[boardIndex];
            ulong white = boards.WhiteOccupancy[boardIndex];
            ulong black = boards.BlackOccupancy[boardIndex];
            var nonOccupancyState = boards.NonOccupancyState[boardIndex];
            byte castleRights = (byte)((nonOccupancyState >> 24) & 0xFF);
            byte enPassantFile = (byte)((nonOccupancyState >> 16) & 0xFF);
            byte whiteKingPos = (byte)((nonOccupancyState >> 8) & 0xFF);
            byte blackKingPos = (byte)(nonOccupancyState & 0xFF);

            // The count of the number of initial moves for this board
            boards.L1MoveIndexes[boardIndex] = CountMoves(pawn, knight, bishop, rook, queen, white, black, whiteKingPos, blackKingPos, castleRights, enPassantFile, gpuAttackTables);
        }

        public static void CountL2MovesKernel(Index1D l1Index, BoardLayerBuffers boards, ArrayView1D<ulong, Stride1D.Dense> l1Moves, GpuAttackTable gpuAttackTables)
        {
            // Get index of the board for this l1 move
            var boardIndex = boards.L1BoardIndexes[l1Index];

            // Get the board
            ulong pawn = boards.PawnOccupancy[boardIndex];
            ulong knight = boards.KnightOccupancy[boardIndex];
            ulong bishop = boards.BishopOccupancy[boardIndex];
            ulong rook = boards.RookOccupancy[boardIndex];
            ulong queen = boards.QueenOccupancy[boardIndex];
            ulong white = boards.WhiteOccupancy[boardIndex];
            ulong black = boards.BlackOccupancy[boardIndex];
            var nonOccupancyState = boards.NonOccupancyState[boardIndex];
            byte castleRights = (byte)((nonOccupancyState >> 24) & 0xFF);
            byte enPassantFile = (byte)((nonOccupancyState >> 16) & 0xFF);
            byte whiteKingPos = (byte)((nonOccupancyState >> 8) & 0xFF);
            byte blackKingPos = (byte)(nonOccupancyState & 0xFF);

            // Apply the L1 move
            var l1Move = (uint)(l1Moves[l1Index] & 0xFFFFF);
            MoveExtensionsBlack.ApplyMoves(l1Move, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);

            // Calculate the possible moves at l2
            boards.L2MoveIndexes[l1Index] = CountMoves(pawn, knight, bishop, rook, queen, white, black, whiteKingPos, blackKingPos, castleRights, enPassantFile, gpuAttackTables);
        }

        public static void CountL3MovesKernel(Index1D l2Index, BoardLayerBuffers boards, ArrayView1D<ulong, Stride1D.Dense> l2Moves, GpuAttackTable gpuAttackTables)
        {
            // Get index of the board for this l2 move
            var boardIndex = boards.L2BoardIndexes[l2Index];

            // Get the board
            ulong pawn = boards.PawnOccupancy[boardIndex];
            ulong knight = boards.KnightOccupancy[boardIndex];
            ulong bishop = boards.BishopOccupancy[boardIndex];
            ulong rook = boards.RookOccupancy[boardIndex];
            ulong queen = boards.QueenOccupancy[boardIndex];
            ulong white = boards.WhiteOccupancy[boardIndex];
            ulong black = boards.BlackOccupancy[boardIndex];
            var nonOccupancyState = boards.NonOccupancyState[boardIndex];
            byte castleRights = (byte)((nonOccupancyState >> 24) & 0xFF);
            byte enPassantFile = (byte)((nonOccupancyState >> 16) & 0xFF);
            byte whiteKingPos = (byte)((nonOccupancyState >> 8) & 0xFF);
            byte blackKingPos = (byte)(nonOccupancyState & 0xFF);

            // Apply L1 & L2 moves
            var moves = l2Moves[l2Index];
            uint move1 = (uint)((moves >> 20) & 0xFFFFF);
            uint move2 = (uint)(moves & 0xFFFFF);

            MoveExtensionsBlack.ApplyMoves(move1, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);
            MoveExtensionsBlack.ApplyMoves(move2, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);

            // Calculate the number of moves possible at L3
            boards.L3MoveIndexes[l2Index] = CountMoves(pawn, knight, bishop, rook, queen, white, black, whiteKingPos, blackKingPos, castleRights, enPassantFile, gpuAttackTables);
        }


        public static int CountMoves(ulong pawn, ulong knight, ulong bishop, ulong rook, ulong queen, ulong white, ulong black, byte whitKing, byte blackKing, byte castleRights, byte EnpassantFile,
            GpuAttackTable gpuAttackTables)
        {
            int moveCount = 0;

            var occupancy = white | black;
            var diagonalSliders = bishop | queen;
            var straightSliders = rook | queen;

            var checkers = gpuAttackTables.PextBishopAttacks(occupancy, blackKing) & white & diagonalSliders |
               gpuAttackTables.PextRookAttacks(occupancy, blackKing) & white & straightSliders |
               gpuAttackTables.KnightAttackTable[blackKing] & white & knight |
               gpuAttackTables.BlackPawnAttackTable[blackKing] & white & pawn;

            var numCheckers = PopCount(checkers);

            var moveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF : checkers | gpuAttackTables.LineBitBoardsInclusive[whitKing * 64 + TrailingZeroCount(checkers)];
            var pinMask =
                gpuAttackTables.DetectPinsDiagonal(blackKing, white & diagonalSliders, occupancy) |
                gpuAttackTables.DetectPinsStraight(blackKing, white & straightSliders, occupancy);

            // count the number of possible moves

            var attackedSquares = 0ul;

            var occupancyMinusKing = occupancy ^ 1ul << blackKing;

            var positions = white & pawn;
            while (positions != 0) attackedSquares |= gpuAttackTables.WhitePawnAttackTable[positions.PopLSB()];

            positions = white & knight;
            while (positions != 0) attackedSquares |= gpuAttackTables.KnightAttackTable[positions.PopLSB()];

            positions = white & diagonalSliders;
            while (positions != 0) attackedSquares |= gpuAttackTables.PextBishopAttacks(occupancyMinusKing, positions.PopLSB());

            positions = white & straightSliders;
            while (positions != 0) attackedSquares |= gpuAttackTables.PextRookAttacks(occupancyMinusKing, positions.PopLSB());

            attackedSquares |= gpuAttackTables.KingAttackTable[blackKing];

            var kingMoves = gpuAttackTables.KingAttackTable[blackKing] & ~attackedSquares;
            moveCount += PopCount(kingMoves & white);
            moveCount += PopCount(kingMoves & ~occupancy);

            if (blackKing == 4 && numCheckers == 0)
            {
                if ((castleRights & (byte)CastleRights.BlackKingSide) != 0 &&
                   (black & rook & Constants.BlackKingSideCastleRookPosition) > 0 &&
                   (occupancy & Constants.BlackKingSideCastleEmptyPositions) == 0 &&
                   (attackedSquares & 1ul << 6) == 0 &&
                   (attackedSquares & 1ul << 5) == 0)
                {
                    moveCount++;
                }

                // Queen Side Castle
                if ((castleRights & (byte)CastleRights.BlackQueenSide) != 0 &&
                    (black & rook & Constants.BlackQueenSideCastleRookPosition) > 0 &&
                    (occupancy & Constants.BlackQueenSideCastleEmptyPositions) == 0 &&
                      (attackedSquares & 1ul << 2) == 0 &&
                    (attackedSquares & 1ul << 3) == 0)
                {
                    moveCount++;
                }
            }

            positions = black & knight & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();
                var potentialMoves = gpuAttackTables.KnightAttackTable[square] & moveMask;
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & bishop & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, square) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, square);
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & bishop & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, square) & moveMask;
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & rook & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, square) & moveMask & gpuAttackTables.GetRayToEdgeStraight(blackKing, square);
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & rook & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, square) & moveMask;
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & queen & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, square) |
                         gpuAttackTables.PextRookAttacks(occupancy, square)) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, square) | gpuAttackTables.GetRayToEdgeStraight(blackKing, square);
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & queen & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, square) |
                         gpuAttackTables.PextRookAttacks(occupancy, square)) & moveMask;
                moveCount += PopCount(potentialMoves & ~black);
            }

            positions = black & pawn & pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();
                var validMoves = gpuAttackTables.BlackPawnAttackTable[square] & moveMask & white & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, square);
                moveCount += PopCount(validMoves);
                validMoves = gpuAttackTables.BlackPawnPushTable[square] & moveMask & ~occupancy & gpuAttackTables.GetRayToEdgeStraight(blackKing, square);
                var rankIndex = square.GetRankIndex();
                while (validMoves != 0)
                {
                    var toSquare = validMoves.PopLSB();

                    if (rankIndex.IsSecondRank() && toSquare.GetRankIndex() == 3)
                    {
                        // Double push: Check intermediate square
                        var intermediateSquare = (square + toSquare) / 2; // Midpoint between start and destination
                        if ((occupancy & 1UL << intermediateSquare) != 0)
                        {
                            continue; // Intermediate square is blocked, skip this move
                        }
                    }

                    // single push
                    moveCount++;
                }


                if (EnpassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                    Math.Abs(square.GetFileIndex() - EnpassantFile) == 1)
                {
                    // Todo - some illegal moves possible here (pinned / discovered)
                    moveCount++;
                }
            }

            positions = black & pawn & ~pinMask;
            while (positions != 0)
            {
                var square = positions.PopLSB();
                var validMoves = gpuAttackTables.BlackPawnAttackTable[square] & moveMask & white;
                moveCount += PopCount(validMoves);

                validMoves = gpuAttackTables.BlackPawnPushTable[square] & moveMask & ~occupancy;

                var rankIndex = square.GetRankIndex();
                while (validMoves != 0)
                {
                    var toSquare = validMoves.PopLSB();

                    if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 3)
                    {
                        // Double push: Check intermediate square
                        var intermediateSquare = (square + toSquare) / 2; // Midpoint between start and destination
                        if ((occupancy & 1UL << intermediateSquare) != 0)
                        {
                            continue; // Intermediate square is blocked, skip this move
                        }
                    }

                    // single push
                    moveCount++;
                }

                if (EnpassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                    Math.Abs(square.GetFileIndex() - EnpassantFile) == 1)
                {
                    // Todo - some illegal moves possible here (pinned / discovered)
                    moveCount++;
                }
            }

            return moveCount;
        }


    }
}
