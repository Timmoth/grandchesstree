using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Moves;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public unsafe void GenerateBlackMoves(ref Span<uint> moves, ref int moveIndex)
    {        
        var checkers = WhiteCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        GenerateBlackKingMoves(ref moves, ref moveIndex, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            return;
        }

        MoveMask = 0xFFFFFFFFFFFFFFFF;
        if (numCheckers == 1)
        {
            MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + BlackKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
        }
        var pinMask = BlackKingPinnedRay();

        var positions = Black & Pawn & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackPawnMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackPawnMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = Black & Knight & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackKnightMoves(ref moves, ref moveIndex, index);
        }

        positions = Black & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackBishopMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackBishopMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Rook& pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackRookMoves(ref moves, ref moveIndex, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackRookMoves(ref moves, ref moveIndex, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackQueenMoves(ref moves, ref moveIndex, index,  AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            GenerateBlackQueenMoves(ref moves, ref moveIndex, index,  0xFFFFFFFFFFFFFFFF);
        }
    }
    public unsafe void GenerateBlackPawnMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pushPinMask, ulong capturePinMask)
    {
                var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSecondRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;

            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.KnightCapturePromotion);
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.BishopCapturePromotion);
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.RookCapturePromotion);
                moves[moveIndex++] = MoveExtensions.EncodeCapturePromotionMove(index, toSquare, Constants.QueenCapturePromotion);
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.KnightPromotion);
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.BishopPromotion);
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.RookPromotion);
                moves[moveIndex++] = MoveExtensions.EncodePromotionMove(index, toSquare, Constants.QueenPromotion);
            }
        }
        else
        {
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Pawn, index, toSquare);
            }

            if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                var newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.BlackEnpassantOffset + EnPassantFile;

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhiteSliders(newBoard.BlackKingPos))
                {
                    moves[moveIndex++] = MoveExtensions.EncodeBlackEnpassantMove(index, toSquare);
                }
            }

            // Generate valid push moves for a Black pawn from "index"
            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                
                // For Black, a double push is available if the pawn is on its starting rank (7th rank, i.e. rank index 6)
                // and the destination is two squares ahead (rank index 4) *and* the intermediate square is empty.
                // The intermediate square is computed as the average of source and destination.
                int intermediateSquare = (index + toSquare) / 2;
                if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 4)
                {
                    if (((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue;
                    }
                    moves[moveIndex++] = MoveExtensions.EncodeDoublePush(index, toSquare);

                }
                else
                {
                    moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Pawn, index, toSquare);
                }

            }

        }
    }

    public unsafe void GenerateBlackKnightMoves(ref Span<uint> moves, ref int moveIndex, int index)
    {
                int toSquare;

        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Knight, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();

            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Knight, index, toSquare);
        }
    }

    public unsafe void GenerateBlackBishopMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pinMask)
    {
        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;

        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB(); 
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Bishop, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Bishop, index, toSquare);
        }
    }

    public unsafe void GenerateBlackRookMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pinMask)
    {
        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Rook, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Rook, index, toSquare);
        }
    }

    public unsafe void GenerateBlackQueenMoves(ref Span<uint> moves, ref int moveIndex, int index, ulong pinMask)
    {
        var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                             AttackTables.PextRookAttacks(White | Black, index)) & MoveMask & pinMask;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.Queen, index, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.Queen, index, toSquare);
        }
    }

    public unsafe void GenerateBlackKingMoves(ref Span<uint> moves, ref int moveIndex, bool inCheck)
    {
        var attackedSquares = BlackKingDangerSquares();
        var potentialMoves = *(AttackTables.KingAttackTable + BlackKingPos) & ~attackedSquares;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            toSquare = captureMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeCaptureMove(Constants.King, BlackKingPos, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            toSquare = emptyMoves.PopLSB();
            moves[moveIndex++] = MoveExtensions.EncodeNormalMove(Constants.King, BlackKingPos, toSquare);
        }

        if (BlackKingPos != 60 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return;


        // King Side Castle
        if ((CastleRights & CastleRights.BlackKingSide) != 0 &&
            (Black & Rook & Constants.BlackKingSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.BlackKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 61)) == 0 &&
            (attackedSquares & (1ul << 62)) == 0)
        {
            moves[moveIndex++] = MoveExtensions.EncodeCastleMove(BlackKingPos, 62);
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.BlackQueenSide) != 0 &&
            (Black & Rook & Constants.BlackQueenSideCastleRookPosition) > 0 &&
            ((White | Black) & Constants.BlackQueenSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 58)) == 0 &&
            (attackedSquares & (1ul << 59)) == 0)
        {
            moves[moveIndex++] = MoveExtensions.EncodeCastleMove(BlackKingPos, 58);
        }
        return;

    }
}