using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public unsafe void AccumulateBlackMoves( ref Summary summary, int depth, int prevDestination)
    {
        var checkers = WhiteCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);
        if (depth == 0)
        {
            if (numCheckers == 1)
            {
                summary.Nodes++;
                MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + BlackKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
                var isDiscovered = (checkers & (1UL << prevDestination)) == 0;
                if (!BlackCanEvadeSingleCheck())
                {
                    // Mate
                    if (isDiscovered)
                    {
                        summary.SingleDiscoveredCheckmate++;
                    }
                    else
                    {
                        summary.DirectCheckmate++;
                    }
                }
                else
                {
                    // Check
                    if (isDiscovered)
                    {
                        summary.SingleDiscoveredCheck++;
                    }
                    else
                    {
                        summary.DirectCheck++;
                    }
                }

                return;
            }

            if (numCheckers > 1)
            {
                summary.Nodes++;
                var isDiscovered = (checkers & (1UL << prevDestination)) == 0;
                if (!CanBlackKingMove())
                {
                    // Mate
                    if (isDiscovered)
                    {
                        summary.DoubleDiscoverdCheckmate++;
                    }
                    else
                    {
                        summary.DirectDiscoverdCheckmate++;
                    }
                }
                else
                {
                    // Check
                    if (isDiscovered)
                    {
                        summary.DoubleDiscoveredCheck++;
                    }
                    else
                    {
                        summary.DirectDiscoveredCheck++;
                    }
                }

                return;
            }

            summary.Nodes++;
            return;
        }

        var ptr = (Perft.HashTable + (Hash & Perft.HashTableMask));
        var hashEntry = Unsafe.Read<Summary>(ptr);
        if (hashEntry.FullHash == (Hash ^  (White | Black)) && depth == hashEntry.Depth)
        {
            summary.Accumulate(ref hashEntry);
            return;
        }

        hashEntry = default;
        hashEntry.FullHash = Hash ^  (White | Black);
        hashEntry.Depth = (byte)depth;
        AccumulateBlackKingMoves( ref hashEntry, depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            summary.Accumulate(ref hashEntry);
            *ptr = hashEntry;
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
            AccumulateBlackPawnMoves( ref hashEntry, depth, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackPawnMoves( ref hashEntry, depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = Black & Knight & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackKnightMoves( ref hashEntry, depth, index);
        }

        positions = Black & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackBishopMoves( ref hashEntry, depth, index, AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackBishopMoves( ref hashEntry, depth, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Rook& pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackRookMoves( ref hashEntry, depth, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackRookMoves( ref hashEntry, depth, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackQueenMoves( ref hashEntry, depth, index,  AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackQueenMoves( ref hashEntry, depth, index,  0xFFFFFFFFFFFFFFFF);
        }
        
        summary.Accumulate(ref hashEntry);
        *ptr = hashEntry;
    }
    public unsafe void AccumulateBlackPawnMoves( ref Summary summary, int depth, int index, ulong pushPinMask, ulong capturePinMask)
    {
        Board newBoard;
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSecondRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;

            while (validMoves != 0)
            {
                if (depth == 1) summary.AddPromotionCapture();
                toSquare = validMoves.PopLSB();

                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_KnightPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_BishopPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_RookPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_QueenPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                if (depth == 1) summary.AddPromotion();

                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_KnightPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_BishopPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_RookPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_QueenPromotion(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
            }
        }
        else
        {
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;
            while (validMoves != 0)
            {
                if (depth == 1) summary.AddCapture();

                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture(index, toSquare);
                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
            }

            if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.BlackEnpassantOffset + EnPassantFile;

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhiteSliders(newBoard.BlackKingPos))
                {
                    if (depth == 1) summary.AddEnpassant();
                    newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
                }
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);


                if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 4)
                {
                    // Double push: Check intermediate square
                    var intermediateSquare = (index + toSquare) / 2; // Midpoint between start and destination
                    if (((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue; // Intermediate square is blocked, skip this move
                    }
                    newBoard.BlackPawn_DoublePush(index, toSquare);
                }
                else
                {
                    // single push
                    newBoard.BlackPawn_Move(index, toSquare);
                }

                newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
            }
        }

    }

    public unsafe void AccumulateBlackKnightMoves( ref Summary summary, int depth, int index)
    {
        Board newBoard;
        int toSquare;

        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackKnight_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKnight_Move(index, toSquare);
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateBlackBishopMoves( ref Summary summary, int depth, int index, ulong pinMask)
    {
        Board newBoard;

        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;

        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackBishop_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackBishop_Move(index, toSquare);
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateBlackRookMoves( ref Summary summary, int depth, int index, ulong pinMask)
    {
        Board newBoard;

        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackRook_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackRook_Move(index, toSquare);
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateBlackQueenMoves( ref Summary summary, int depth, int index, ulong pinMask)
    {
        Board newBoard;

        var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                             AttackTables.PextRookAttacks(White | Black, index)) & MoveMask & pinMask;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackQueen_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackQueen_Move(index, toSquare);
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateBlackKingMoves( ref Summary summary, int depth, bool inCheck)
    {
        var attackedSquares = BlackKingDangerSquares();
        Board newBoard;

        var potentialMoves = *(AttackTables.KingAttackTable + BlackKingPos) & ~attackedSquares;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();
            newBoard.BlackKing_Capture(BlackKingPos, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKing_Move(BlackKingPos, toSquare);
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, toSquare);
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
            newBoard = Unsafe.As<Board, Board>(ref this);

            newBoard.BlackKing_KingSideCastle();
            if (depth == 1) summary.AddCastle();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, 61);
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.BlackQueenSide) != 0 &&
            (Black & Rook & Constants.BlackQueenSideCastleRookPosition) > 0 &&
            ((White | Black) & Constants.BlackQueenSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 58)) == 0 &&
            (attackedSquares & (1ul << 59)) == 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            newBoard.BlackKing_QueenSideCastle();
            if (depth == 1) summary.AddCastle();
            newBoard.AccumulateWhiteMoves( ref summary, depth - 1, 59);
        }
    }
}