using System;
using System.Reflection.Emit;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;
using ILGPU;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker.Kernels
{
    public static class GenerateMovesBlack
    {

        public static void GenerateL1Moves(Index1D boardIndex, BoardLayerBuffers boards, GpuAttackTable gpuAttackTables, ArrayView1D<ulong, Stride1D.Dense> l1Moves)
        {
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

            // Get the L1 move offset
            int firstL1MoveIndex = boards.L1MoveIndexes[boardIndex];

            var prevMoves = 0ul;

            // Generate the L1 moves at the given offset
            int nextL1MoveIndex = GenerateMoves(firstL1MoveIndex, pawn, knight, bishop, rook, queen, white, black, whiteKingPos, blackKingPos, castleRights, enPassantFile, gpuAttackTables, l1Moves, prevMoves);
            
            // Set the board index for each L1 move
            for(int i = firstL1MoveIndex; i < nextL1MoveIndex; i++)
            {
                boards.L1BoardIndexes[i] = boardIndex;
            }
        }

        public static void GenerateL2Moves(Index1D l1Index,
            BoardLayerBuffers layer,
            ArrayView1D<ulong, Stride1D.Dense> l1Moves,
            GpuAttackTable gpuAttackTables, 
            ArrayView1D<ulong, Stride1D.Dense> l2Moves)
        {
            // Get index of the board for this l1 move
            var boardIndex = layer.L1BoardIndexes[l1Index];

            // Get the board
            ulong pawn = layer.PawnOccupancy[boardIndex];
            ulong knight = layer.KnightOccupancy[boardIndex];
            ulong bishop = layer.BishopOccupancy[boardIndex];
            ulong rook = layer.RookOccupancy[boardIndex];
            ulong queen = layer.QueenOccupancy[boardIndex];
            ulong white = layer.WhiteOccupancy[boardIndex];
            ulong black = layer.BlackOccupancy[boardIndex];
            var nonOccupancyState = layer.NonOccupancyState[boardIndex];
            byte castleRights = (byte)((nonOccupancyState >> 24) & 0xFF);
            byte enPassantFile = (byte)((nonOccupancyState >> 16) & 0xFF);
            byte whiteKingPos = (byte)((nonOccupancyState >> 8) & 0xFF);
            byte blackKingPos = (byte)(nonOccupancyState & 0xFF);

            // Apply the L1 move
            var moves = l1Moves[l1Index];
            var l1Move = (uint)(moves & 0xFFFFF);

            MoveExtensionsBlack.ApplyMoves(l1Move, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);

            var prevMoves = moves << 20;

            // Get the L2 move offset
            int firstL2MoveIndex = layer.L2MoveIndexes[l1Index];
            // Generate the L2 moves at the given offset
            int nextL2MoveIndex = GenerateMoves(firstL2MoveIndex, pawn, knight, bishop, rook, queen, white, black, whiteKingPos, blackKingPos, castleRights, enPassantFile, gpuAttackTables, l2Moves, prevMoves);
            // Set the board index for each L2 move
            for (int i = firstL2MoveIndex; i < nextL2MoveIndex; i++)
            {
                layer.L2BoardIndexes[i] = boardIndex;
            }
        }

        public static void GenerateL3Moves(Index1D index,
            BoardLayerBuffers layer,
            ArrayView1D<ulong, Stride1D.Dense> l2Moves,
            GpuAttackTable gpuAttackTables,
            ArrayView1D<ulong, Stride1D.Dense> l3Moves)
        {
            var boardIndex = layer.L2BoardIndexes[index];

            ulong pawn = layer.PawnOccupancy[boardIndex];
            ulong knight = layer.KnightOccupancy[boardIndex];
            ulong bishop = layer.BishopOccupancy[boardIndex];
            ulong rook = layer.RookOccupancy[boardIndex];
            ulong queen = layer.QueenOccupancy[boardIndex];
            ulong white = layer.WhiteOccupancy[boardIndex];
            ulong black = layer.BlackOccupancy[boardIndex];
            var nonOccupancyState = layer.NonOccupancyState[boardIndex];
            byte castleRights = (byte)((nonOccupancyState >> 24) & 0xFF);
            byte enPassantFile = (byte)((nonOccupancyState >> 16) & 0xFF);
            byte whiteKingPos = (byte)((nonOccupancyState >> 8) & 0xFF);
            byte blackKingPos = (byte)(nonOccupancyState & 0xFF);

            // Apply Moves
            var moves = l2Moves[index];
            var move1 = (uint)(moves >> 20) & 0xFFFFF;
            var move2 = (uint)moves & 0xFFFFF;


            MoveExtensionsBlack.ApplyMoves(move1, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);
            MoveExtensionsBlack.ApplyMoves(move2, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);

            var prevMoves = moves << 20;

            // Get the L3 move offset
            int firstL3MoveIndex = layer.L3MoveIndexes[index];
            // Generate the L3 moves at the given offset
            int nextL3MoveIndex = GenerateMoves(firstL3MoveIndex, pawn, knight, bishop, rook, queen, white, black, whiteKingPos, blackKingPos, castleRights, enPassantFile, gpuAttackTables, l3Moves, prevMoves);
            // Set the board index for each L3 move
            for (int i = firstL3MoveIndex; i < nextL3MoveIndex; i++)
            {
                layer.L3BoardIndexes[i] = boardIndex;
            }
        }

        public static int GenerateMoves(int outputIndex,
            ulong pawn, ulong knight, ulong bishop, ulong rook, ulong queen, ulong white, ulong black, byte whitKing, byte blackKing, byte castleRights, byte EnpassantFile,
            GpuAttackTable gpuAttackTables,
            ArrayView1D<ulong, Stride1D.Dense> moves,
            ulong prevMoves)
        {
            var occupancy = white | black;
            var diagonalSliders = bishop | queen;
            var straightSliders = rook | queen;

            var checkers = gpuAttackTables.PextBishopAttacks(occupancy, blackKing) & white & diagonalSliders |
               gpuAttackTables.PextRookAttacks(occupancy, blackKing) & white & straightSliders |
               gpuAttackTables.KnightAttackTable[blackKing] & white & knight |
               gpuAttackTables.BlackPawnAttackTable[blackKing] & white & pawn;

            var numCheckers = IntrinsicMath.PopCount(checkers);

            var moveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF : checkers | gpuAttackTables.LineBitBoardsInclusive[blackKing * 64 + IntrinsicMath.TrailingZeroCount(checkers)];
            var pinMask =
                gpuAttackTables.DetectPinsDiagonal(blackKing, white & diagonalSliders, occupancy) |
                gpuAttackTables.DetectPinsStraight(blackKing, white & straightSliders, occupancy);

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

            attackedSquares |= gpuAttackTables.KingAttackTable[whitKing];

            // Generate all possible moves and write them to the output

            var kingMoves = gpuAttackTables.KingAttackTable[blackKing] & ~attackedSquares;
            positions = kingMoves & white;
            while (positions != 0)
            {
                // King capture
                moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.King, blackKing, (byte)positions.PopLSB());
            }

            positions = kingMoves & ~occupancy;
            while (positions != 0)
            {
                moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.King, blackKing, (byte)positions.PopLSB());
            }

            if (blackKing == 4 && numCheckers == 0)
            {
                if ((castleRights & (byte)CastleRights.BlackKingSide) != 0 &&
                   (black & rook & Constants.BlackKingSideCastleRookPosition) > 0 &&
                   (occupancy & Constants.BlackKingSideCastleEmptyPositions) == 0 &&
                   (attackedSquares & 1ul << 6) == 0 &&
                   (attackedSquares & 1ul << 5) == 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCastleMove(blackKing, 5);
                }

                // Queen Side Castle
                if ((castleRights & (byte)CastleRights.BlackQueenSide) != 0 &&
                    (black & rook & Constants.BlackQueenSideCastleRookPosition) > 0 &&
                    (occupancy & Constants.BlackQueenSideCastleEmptyPositions) == 0 &&
                    (attackedSquares & 1ul << 2) == 0 &&
                    (attackedSquares & 1ul << 3) == 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCastleMove(blackKing, 3);
                }
            }

            positions = black & knight & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var potentialMoves = gpuAttackTables.KnightAttackTable[fromSquare] & moveMask & ~black;
                var captureMoves = potentialMoves & white;

                while (captureMoves != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Knight, (byte)fromSquare, (byte)captureMoves.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Knight, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & bishop & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, fromSquare);
                var captures = potentialMoves & white;
                while (captures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Bishop, (byte)fromSquare, (byte)captures.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Bishop, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & bishop & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) & moveMask;
                var captures = potentialMoves & white;
                while (captures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Bishop, (byte)fromSquare, (byte)captures.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Bishop, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & rook & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, fromSquare) & moveMask & gpuAttackTables.GetRayToEdgeStraight(blackKing, fromSquare);
                var captures = potentialMoves & white;
                while (captures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Rook, (byte)fromSquare, (byte)captures.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Rook, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & rook & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, fromSquare) & moveMask;
                var captures = potentialMoves & white;
                while (captures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Rook, (byte)fromSquare, (byte)captures.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Rook, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & queen & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) |
                         gpuAttackTables.PextRookAttacks(occupancy, fromSquare)) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, fromSquare) | gpuAttackTables.GetRayToEdgeStraight(blackKing, fromSquare);
                var captures = potentialMoves & white;
                while (captures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Queen, (byte)fromSquare, (byte)captures.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Queen, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & queen & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) |
                         gpuAttackTables.PextRookAttacks(occupancy, fromSquare)) & moveMask;
                var captures = potentialMoves & white;
                while (captures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Queen, (byte)fromSquare, (byte)captures.PopLSB());
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Queen, (byte)fromSquare, (byte)nonCaptures.PopLSB());
                }
            }

            positions = black & pawn & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var rankIndex = fromSquare.GetRankIndex();

                if (rankIndex.IsSeventhRank())
                {
                    var captureMoves = gpuAttackTables.BlackPawnAttackTable[fromSquare] & moveMask & white & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, fromSquare);
                    while (captureMoves != 0)
                    {
                        var toSquare = (byte)captureMoves.PopLSB();
                        // knight promotion
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.KnightCapturePromotion);
                        // bishop promotion                                                                              
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.BishopCapturePromotion);
                        // rook promotion                                                                                
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.RookCapturePromotion);
                        // queen promotion                                                                               
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.QueenCapturePromotion);
                    }

                    var pushMoves = gpuAttackTables.BlackPawnPushTable[fromSquare] & moveMask & ~occupancy & gpuAttackTables.GetRayToEdgeStraight(whitKing, fromSquare);
                    while (pushMoves != 0)
                    {
                        var toSquare = (byte)pushMoves.PopLSB();
                        // knight promotion
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.KnightPromotion);
                        // bishop promotion                                                              
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.BishopPromotion);
                        // rook promotion                                                                
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.RookPromotion);
                        // queen promotion                                                               
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.QueenPromotion);

                    }
                }
                else
                {
                    var captureMoves = gpuAttackTables.BlackPawnAttackTable[fromSquare] & moveMask & white & gpuAttackTables.GetRayToEdgeDiagonal(blackKing, fromSquare);
                    while (captureMoves != 0)
                    {
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Pawn, (byte)fromSquare, (byte)captureMoves.PopLSB());
                    }

                    var pushMoves = gpuAttackTables.BlackPawnPushTable[fromSquare] & moveMask & ~occupancy & gpuAttackTables.GetRayToEdgeStraight(blackKing, fromSquare);
                    while (pushMoves != 0)
                    {
                        // todo - double push
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Pawn, (byte)fromSquare, (byte)pushMoves.PopLSB());
                    }
                }

            }

            positions = black & pawn & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var rankIndex = fromSquare.GetRankIndex();

                if (rankIndex.IsSecondRank())
                {
                    var captureMoves = gpuAttackTables.BlackPawnAttackTable[fromSquare] & moveMask & white;
                    while (captureMoves != 0)
                    {
                        var toSquare = (byte)captureMoves.PopLSB();

                        // knight promotion
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.KnightCapturePromotion);
                        // bishop promotion                                                              
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.BishopCapturePromotion);
                        // rook promotion                                                                
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.RookCapturePromotion);
                        // queen promotion                                                               
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.QueenCapturePromotion);
                    }

                    var pushMoves = gpuAttackTables.BlackPawnPushTable[fromSquare] & moveMask & ~occupancy;
                    while (pushMoves != 0)
                    {
                        var toSquare = (byte)pushMoves.PopLSB();

                        // knight promotion
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.KnightPromotion);
                        // bishop promotion                                                              
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.BishopPromotion);
                        // rook promotion                                                                
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.RookPromotion);
                        // queen promotion                                                               
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodePromotionMove((byte)fromSquare, toSquare, Constants.QueenPromotion);
                    }
                }
                else
                {
                    var captureMoves = gpuAttackTables.BlackPawnAttackTable[fromSquare] & moveMask & white;
                    while (captureMoves != 0)
                    {
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodeCaptureMove(Constants.Pawn, (byte)fromSquare, (byte)captureMoves.PopLSB());
                    }

                    var pushMoves = gpuAttackTables.BlackPawnPushTable[fromSquare] & moveMask & ~occupancy;
                    while (pushMoves != 0)
                    {
                        // todo - double push
                        moves[outputIndex++] = prevMoves | MoveExtensions.EncodeNormalMove(Constants.Pawn, (byte)fromSquare, (byte)pushMoves.PopLSB());
                    }
                }


                // TODO Enpassant
            }
            return outputIndex;
        }
    }
}
