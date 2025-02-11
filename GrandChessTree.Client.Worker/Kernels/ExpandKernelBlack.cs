using GrandChessTree.Shared.Precomputed;
using ILGPU;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared;
using ILGPU.Runtime;
using System.Threading.Tasks;

namespace GrandChessTree.Client.Worker.Kernels
{
    public static class ExpandKernelBlack
    {
        public static void ExpandLayerKernel(Index1D index,
            BoardLayerBuffers layer,
            ArrayView1D<ulong, Stride1D.Dense> l3Moves,
            GpuAttackTable gpuAttackTables, TotalStatsLayerBuffers stats)
        {
            ulong nodeCount = 0;
            ulong castleCount = 0;
            ulong promotionCount = 0;
            ulong captureCount = 0;
            ulong DirectCheck = 0;
            ulong SingleDiscoveredCheck = 0;
            ulong DirectDiscoveredCheck = 0;
            ulong DoubleDiscoveredCheck = 0;
            ulong DirectCheckmate = 0;
            ulong SingleDiscoveredCheckmate = 0;
            ulong DirectDiscoverdCheckmate = 0;
            ulong DoubleDiscoverdCheckmate = 0;

            var boardIndex = layer.L3BoardIndexes[index];

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

            // Apply L1
            var moves = l3Moves[index];

            uint move1 = (uint)((moves >> 40) & 0xFFFFF);
            uint move2 = (uint)((moves >> 20) & 0xFFFFF);
            uint move3 = (uint)(moves & 0xFFFFF);

            MoveExtensionsBlack.ApplyMoves(move1, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);
            MoveExtensionsBlack.ApplyMoves(move2, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);
            MoveExtensionsBlack.ApplyMoves(move3, ref pawn, ref knight, ref bishop, ref rook, ref queen, ref white, ref black, ref whiteKingPos, ref blackKingPos, ref castleRights, ref enPassantFile);

            var occupancy = white | black;
            var diagonalSliders = bishop | queen;
            var straightSliders = rook | queen;

            var checkers = gpuAttackTables.PextBishopAttacks(occupancy, blackKingPos) & white & diagonalSliders |
               gpuAttackTables.PextRookAttacks(occupancy, blackKingPos) & white & straightSliders |
               gpuAttackTables.KnightAttackTable[blackKingPos] & white & knight |
               gpuAttackTables.BlackPawnAttackTable[blackKingPos] & white & pawn;

            var numCheckers = IntrinsicMath.PopCount(checkers);

            var moveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF : checkers | gpuAttackTables.LineBitBoardsInclusive[blackKingPos * 64 + IntrinsicMath.TrailingZeroCount(checkers)];
            var pinMask =
                gpuAttackTables.DetectPinsDiagonal(blackKingPos, white & diagonalSliders, occupancy) |
                gpuAttackTables.DetectPinsStraight(blackKingPos, white & straightSliders, occupancy);

            var attackedSquares = 0ul;

            var occupancyMinusKing = occupancy ^ 1ul << blackKingPos;

            var positions = white & pawn;
            while (positions != 0) attackedSquares |= gpuAttackTables.WhitePawnAttackTable[positions.PopLSB()];

            positions = white & knight;
            while (positions != 0) attackedSquares |= gpuAttackTables.KnightAttackTable[positions.PopLSB()];

            positions = white & diagonalSliders;
            while (positions != 0) attackedSquares |= gpuAttackTables.PextBishopAttacks(occupancyMinusKing, positions.PopLSB());

            positions = white & straightSliders;
            while (positions != 0) attackedSquares |= gpuAttackTables.PextRookAttacks(occupancyMinusKing, positions.PopLSB());

            attackedSquares |= gpuAttackTables.KingAttackTable[whiteKingPos];

            // Generate all possible moves and write them to the output

            var kingMoves = gpuAttackTables.KingAttackTable[blackKingPos] & ~attackedSquares;
            positions = kingMoves & white;
            while (positions != 0)
            {
                var toSquare = positions.PopLSB();
                var captureMask = 1UL << toSquare;

                var mask = 1UL << blackKingPos | 1UL << toSquare;

                // King capture
                Classify.ClassifyNodeKernel(
                    pawn & ~captureMask,
                    knight & ~captureMask,
                    bishop & ~captureMask,
                    rook & ~captureMask,
                    queen & ~captureMask,
                    white ^ mask,
                    black & ~captureMask,
                    (byte)toSquare,
                    blackKingPos,
                    (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide)),
                    8,
                    gpuAttackTables,
                    ref DirectCheck,
                    ref SingleDiscoveredCheck,
                    ref DirectDiscoveredCheck,
                    ref DoubleDiscoveredCheck,
                    ref DirectCheckmate,
                    ref SingleDiscoveredCheckmate,
                    ref DirectDiscoverdCheckmate,
                    ref DoubleDiscoverdCheckmate
                     );

                captureCount += 1;
                nodeCount++;
            }

            positions = kingMoves & ~occupancy;
            while (positions != 0)
            {
                var toSquare = positions.PopLSB();
                var mask = 1UL << whiteKingPos | 1UL << toSquare;

                Classify.ClassifyNodeKernel(
    pawn,
    knight,
    bishop,
    rook,
    queen,
    white ^ mask,
    black,
    (byte)toSquare,
    blackKingPos,
    (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide)),
    8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                nodeCount++;
            }

            if (whiteKingPos == 4 && numCheckers == 0)
            {
                if ((castleRights & (byte)CastleRights.WhiteKingSide) != 0 &&
                   (white & rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
                   (occupancy & Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
                   (attackedSquares & 1ul << 6) == 0 &&
                   (attackedSquares & 1ul << 5) == 0)
                {
                    var mask = 1UL << whiteKingPos | 1UL << 5;

                    Classify.ClassifyNodeKernel(
    pawn,
    knight,
    bishop,
    rook,
    queen,
    white ^ mask,
    black,
    5,
    blackKingPos,
    (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide)),
    8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );
                    castleCount += 1;
                    nodeCount++;
                }

                // Queen Side Castle
                if ((castleRights & (byte)CastleRights.WhiteQueenSide) != 0 &&
                    (white & rook & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
                    (occupancy & Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
                    (attackedSquares & 1ul << 2) == 0 &&
                    (attackedSquares & 1ul << 3) == 0)
                {
                    var mask = 1UL << whiteKingPos | 1UL << 3;
                    //outPawn = pawn;
                    //outKnight = knight; // move
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move
                    //outBlack = black;
                    //outWhiteKingPos = 3;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide));
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    castleCount += 1;
                    nodeCount++;
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
                    var captureMask = 1UL << toSquare;

                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    // Knight capture
                    //outPawn = pawn & ~captureMask; // remove captured piece
                    //outKnight = knight & ~(1UL << fromSquare); // only remove the knight from it's initial sqaure
                    //outBishop = bishop & ~captureMask; // removed captured piece
                    //outRook = rook & ~captureMask; // remove captured piece
                    //outQueen = queen & ~captureMask; // remove captured piece
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black & ~captureMask; // remove captured piece
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
    pawn & ~captureMask,
    knight & ~captureMask,
    bishop & ~captureMask,
    rook & ~captureMask,
    queen & ~captureMask,
    white ^ mask,
    black & ~captureMask,
    (byte)toSquare,
    blackKingPos,
    (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide)),
    8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;
                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    nodeCount++;
                }
            }

            positions = white & bishop & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(whiteKingPos, fromSquare);
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;
                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
                        pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
                        gpuAttackTables,
                        ref DirectCheck,
                        ref SingleDiscoveredCheck,
                        ref DirectDiscoveredCheck,
                        ref DoubleDiscoveredCheck,
                        ref DirectCheckmate,
                        ref SingleDiscoveredCheckmate,
                        ref DirectDiscoverdCheckmate,
                        ref DoubleDiscoverdCheckmate
                         );


                    nodeCount++;
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
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;

                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    nodeCount++;
                }
            }

            positions = white & rook & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = gpuAttackTables.PextRookAttacks(occupancy, fromSquare) & moveMask & gpuAttackTables.GetRayToEdgeStraight(whiteKingPos, fromSquare);
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;

                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    nodeCount++;
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
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;

                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    nodeCount++;
                }
            }

            positions = white & queen & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();

                var potentialMoves = (gpuAttackTables.PextBishopAttacks(occupancy, fromSquare) |
                         gpuAttackTables.PextRookAttacks(occupancy, fromSquare)) & moveMask & gpuAttackTables.GetRayToEdgeDiagonal(whiteKingPos, fromSquare) | gpuAttackTables.GetRayToEdgeStraight(whiteKingPos, fromSquare);
                var captures = potentialMoves & black;
                while (captures != 0)
                {
                    var toSquare = captures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;

                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    nodeCount++;
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
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    captureCount += 1;
                    nodeCount++;
                }

                var nonCaptures = potentialMoves & ~occupancy;
                while (nonCaptures != 0)
                {
                    var toSquare = nonCaptures.PopLSB();
                    var mask = 1UL << fromSquare | 1UL << toSquare;

                    //outPawn = pawn;
                    //outKnight = knight ^ mask; // move knight
                    //outBishop = bishop;
                    //outRook = rook;
                    //outQueen = queen;
                    //outWhite = white ^ mask; // move knight
                    //outBlack = black;
                    //outWhiteKingPos = whitKing;
                    //outBlackKingPos = blackKing;
                    //outCastleRights = castleRights;
                    //outEnpassantFile = 8; // reset
                    Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                    nodeCount++;
                }
            }

            positions = white & pawn & pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var rankIndex = fromSquare.GetRankIndex();

                if (rankIndex.IsSeventhRank())
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black & gpuAttackTables.GetRayToEdgeDiagonal(whiteKingPos, fromSquare);
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = 1UL << toSquare;

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // knight promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight; // replaced captured with promoted
                        //outBishop = bishop & ~captureMask; // remove captured piece
                        //outRook = rook & ~captureMask; // remove captured piece
                        //outQueen = queen & ~captureMask;// remove captured piece
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset

                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;

                        // bishop promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight & ~captureMask; // remove captured piece
                        //outBishop = bishop; // replaced captured with promoted
                        //outRook = rook & ~captureMask; // remove captured piece
                        //outQueen = queen & ~captureMask;// remove captured piece
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;

                        // rook promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight & ~captureMask; // remove captured piece
                        //outBishop = bishop & ~captureMask; // remove captured piece
                        //outRook = rook; // replaced captured with promoted
                        //outQueen = queen & ~captureMask;// remove captured piece
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;

                        // queen promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight & ~captureMask; // remove captured piece
                        //outBishop = bishop & ~captureMask; // remove captured piece
                        //outRook = rook; // remove captured piece
                        //outQueen = queen; // replaced captured with promoted
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~occupancy & gpuAttackTables.GetRayToEdgeStraight(whiteKingPos, fromSquare);
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // knight promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight | 1UL << toSquare; // promote
                        //outBishop = bishop;
                        //outRook = rook;
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        promotionCount += 1;
                        nodeCount++;

                        // bishop promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight;
                        //outBishop = bishop | 1UL << toSquare; // promote
                        //outRook = rook;
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        //promotionCount += 1;
                        nodeCount++;

                        // rook promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight;
                        //outBishop = bishop;
                        //outRook = rook | 1UL << toSquare; // promote
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        promotionCount += 1;
                        nodeCount++;

                        // queen promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight;
                        //outBishop = bishop;
                        //outRook = rook;
                        //outQueen = queen | 1UL << toSquare; // promote
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        //promotionCount += 1;
                        nodeCount++;
                    }
                }
                else
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black & gpuAttackTables.GetRayToEdgeDiagonal(whiteKingPos, fromSquare);
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = 1UL << toSquare;

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // capture
                        //outPawn = pawn ^ mask; // move
                        //outKnight = knight & ~captureMask; // removed captured piece
                        //outBishop = bishop & ~captureMask; // removed captured piece
                        //outRook = rook & ~captureMask; // remove captured piece
                        //outQueen = queen & ~captureMask; // remove captured piece
                        //outWhite = white ^ mask; // move
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8; // reset
                        Classify.ClassifyNodeKernel(
    pawn & ~captureMask,
    knight & ~captureMask,
    bishop & ~captureMask,
    rook & ~captureMask,
    queen & ~captureMask,
    white ^ mask,
    black & ~captureMask,
    (byte)toSquare,
    blackKingPos,
    (byte)(castleRights & ~(int)(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide)),
    8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                        captureCount += 1;
                        nodeCount++;

                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~occupancy & gpuAttackTables.GetRayToEdgeStraight(whiteKingPos, fromSquare);
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // move
                        //outPawn = pawn ^ mask; // move
                        //outKnight = knight;
                        //outBishop = bishop;
                        //outRook = rook;
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8; // reset
                        Classify.ClassifyNodeKernel(
 pawn,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
    gpuAttackTables,
    ref DirectCheck,
    ref SingleDiscoveredCheck,
    ref DirectDiscoveredCheck,
    ref DoubleDiscoveredCheck,
    ref DirectCheckmate,
    ref SingleDiscoveredCheckmate,
    ref DirectDiscoverdCheckmate,
    ref DoubleDiscoverdCheckmate
     );

                        nodeCount++;

                    }
                }

            }

            positions = white & pawn & ~pinMask;
            while (positions != 0)
            {
                var fromSquare = positions.PopLSB();
                var rankIndex = fromSquare.GetRankIndex();

                if (rankIndex.IsSeventhRank())
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black;
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = 1UL << toSquare;

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // knight promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight; // replaced captured with promoted
                        //outBishop = bishop & ~captureMask; // remove captured piece
                        //outRook = rook & ~captureMask; // remove captured piece
                        //outQueen = queen & ~captureMask;// remove captured piece
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;

                        // bishop promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight & ~captureMask; // remove captured piece
                        //outBishop = bishop; // replaced captured with promoted
                        //outRook = rook & ~captureMask; // remove captured piece
                        //outQueen = queen & ~captureMask;// remove captured piece
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;

                        // rook promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight & ~captureMask; // remove captured piece
                        //outBishop = bishop & ~captureMask; // remove captured piece
                        //outRook = rook; // replaced captured with promoted
                        //outQueen = queen & ~captureMask;// remove captured piece
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;

                        // queen promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight & ~captureMask; // remove captured piece
                        //outBishop = bishop & ~captureMask; // remove captured piece
                        //outRook = rook; // remove captured piece
                        //outQueen = queen; // replaced captured with promoted
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black & ~captureMask; // remove captured piece
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        captureCount += 1;
                        promotionCount += 1;
                        nodeCount++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~occupancy;
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // knight promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight | 1UL << toSquare; // promote
                        //outBishop = bishop;
                        //outRook = rook;
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset

                        promotionCount += 1;
                        nodeCount++;

                        // bishop promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight;
                        //outBishop = bishop | 1UL << toSquare; // promote
                        //outRook = rook;
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        promotionCount += 1;
                        nodeCount++;

                        // rook promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight;
                        //outBishop = bishop;
                        //outRook = rook | 1UL << toSquare; // promote
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        promotionCount += 1;
                        nodeCount++;

                        // queen promotion
                        //outPawn = pawn & ~(1UL << fromSquare); // remove pawn
                        //outKnight = knight;
                        //outBishop = bishop;
                        //outRook = rook;
                        //outQueen = queen | 1UL << toSquare; // promote
                        //outWhite = white ^ mask; // move piece
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8;// reset
                        promotionCount += 1;
                        nodeCount++;
                    }
                }
                else
                {
                    var captureMoves = gpuAttackTables.WhitePawnAttackTable[fromSquare] & moveMask & black;
                    while (captureMoves != 0)
                    {
                        var toSquare = captureMoves.PopLSB();
                        var captureMask = 1UL << toSquare;

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // capture
                        Classify.ClassifyNodeKernel(
                        pawn ^ mask,
                        knight & ~captureMask,
                        bishop & ~captureMask,
                        rook & ~captureMask,
                        queen & ~captureMask,
                        white ^ mask,
                        black & ~captureMask,
                        whiteKingPos,
                        blackKingPos,
                       castleRights,
                        8,
                        gpuAttackTables,
                        ref DirectCheck,
                        ref SingleDiscoveredCheck,
                        ref DirectDiscoveredCheck,
                        ref DoubleDiscoveredCheck,
                        ref DirectCheckmate,
                        ref SingleDiscoveredCheckmate,
                        ref DirectDiscoverdCheckmate,
                        ref DoubleDiscoverdCheckmate
                         );

                        captureCount += 1;
                        nodeCount++;
                    }

                    var pushMoves = gpuAttackTables.WhitePawnPushTable[fromSquare] & moveMask & ~occupancy;
                    while (pushMoves != 0)
                    {
                        var toSquare = pushMoves.PopLSB();

                        var mask = 1UL << fromSquare | 1UL << toSquare;

                        // move
                        //outPawn = pawn ^ mask; // move
                        //outKnight = knight;
                        //outBishop = bishop;
                        //outRook = rook;
                        //outQueen = queen;
                        //outWhite = white ^ mask; // move
                        //outBlack = black;
                        //outWhiteKingPos = whitKing;
                        //outBlackKingPos = blackKing;
                        //outCastleRights = castleRights;
                        //outEnpassantFile = 8; // reset
                        Classify.ClassifyNodeKernel(
                        pawn ^ mask,
                        knight,
                        bishop,
                        rook,
                        queen,
                        white ^ mask,
                        black,
                        whiteKingPos,
                        blackKingPos,
                        castleRights,
                        8,
                        gpuAttackTables,
                        ref DirectCheck,
                        ref SingleDiscoveredCheck,
                        ref DirectDiscoveredCheck,
                        ref DoubleDiscoveredCheck,
                        ref DirectCheckmate,
                        ref SingleDiscoveredCheckmate,
                        ref DirectDiscoverdCheckmate,
                        ref DoubleDiscoverdCheckmate
                         );

                        nodeCount++;
                    }
                }


                // TODO Enpassant
            }


            stats.Nodes[index] = nodeCount;
            stats.Castles[index] = castleCount;
            stats.Captures[index] = captureCount;
            stats.Promotions[index] = promotionCount;
        }

    }
}
