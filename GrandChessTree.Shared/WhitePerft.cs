using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    private unsafe void AccumulateWhiteMoves( ref Summary summary, int depth, int prevDestination)
    {
        var checkers = BlackCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        if (depth == 0)
        {
            // Leaf node
            if (numCheckers == 1)
            {
                summary.Nodes++;
                MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
                var isDiscovered = (checkers & (1UL << prevDestination)) == 0;
                if (!WhiteCanEvadeCheck())
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
                if (!CanWhiteKingMove())
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
        if (hashEntry.FullHash == (Hash ^ (White | Black)) && depth == hashEntry.Depth)
        {
            summary.Accumulate(ref hashEntry);
            return;
        }

        hashEntry = default;
        hashEntry.FullHash = Hash ^ (White | Black);
        hashEntry.Depth = (byte)depth;
        
        AccumulateWhiteKingMoves( ref hashEntry, depth, numCheckers > 0);

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
            MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
        }
        var pinMask = WhiteKingPinnedRay();

        var positions = White & Pawn & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhitePawnMoves( ref hashEntry, depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }
        
        positions = White & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhitePawnMoves( ref hashEntry, depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Knight & ~pinMask;
        while (positions != 0)
        {
            AccumulateWhiteKnightMoves( ref hashEntry, depth, positions.PopLSB());
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhiteBishopMoves( ref hashEntry, depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }
        
        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhiteBishopMoves( ref hashEntry, depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhiteRookMoves( ref hashEntry, depth, index,  AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhiteRookMoves( ref hashEntry, depth, index,  0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhiteQueenMoves( ref hashEntry, depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }     
        
        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateWhiteQueenMoves( ref hashEntry, depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        summary.Accumulate(ref hashEntry);
        *ptr = hashEntry;
    }
    public unsafe void AccumulateWhitePawnMoves( ref Summary summary, int depth, int index, ulong pushPinMask, ulong capturePinMask)
    {
        Board newBoard ;
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSeventhRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.WhitePawnAttackTable +index) & MoveMask & Black & capturePinMask;

            while (validMoves != 0)
            {
                if (depth == 1) summary.AddPromotionCapture();
                toSquare = validMoves.PopLSB();

                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_KnightPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_BishopPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_RookPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_QueenPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
            }

            validMoves = *(AttackTables.WhitePawnPushTable + index) & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                if (depth == 1) summary.AddPromotion();

                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_KnightPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_BishopPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_RookPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_QueenPromotion(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
            }
        }
        else
        {
            var validMoves = *(AttackTables.WhitePawnAttackTable + index) & MoveMask & Black & capturePinMask;

            while (validMoves != 0)
            {
                if (depth == 1) summary.AddCapture();

                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture(index, toSquare);
                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
            }

            if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

                newBoard.WhitePawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByBlackSliders(newBoard.WhiteKingPos))
                {
                    if (depth == 1) summary.AddEnpassant();
                    newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
                }
            }

            validMoves = AttackTables.WhitePawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);


                if (rankIndex.IsSecondRank() && toSquare.GetRankIndex() == 3)
                {
                    // Double push: Check intermediate square
                    var intermediateSquare = (index + toSquare) / 2; // Midpoint between start and destination
                    if (((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue; // Intermediate square is blocked, skip this move
                    }
                    newBoard.WhitePawn_DoublePush(index, toSquare);
                }
                else
                {
                    // single push
                    newBoard.WhitePawn_Move(index, toSquare);
                }

                newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
            }
        }
     
    }

    public unsafe void AccumulateWhiteKnightMoves( ref Summary summary, int depth, int index)
    {
        int toSquare;
        Board newBoard;
        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {    
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();
            newBoard.WhiteKnight_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKnight_Move(index, toSquare);
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateWhiteBishopMoves( ref Summary summary, int depth, int index, ulong pinMask)
    {
        Board newBoard;
        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;

        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = captureMoves.PopLSB();
            newBoard.WhiteBishop_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteBishop_Move(index, toSquare);
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateWhiteRookMoves( ref Summary summary, int depth, int index, ulong pinMask)
    {
        Board newBoard;
        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = captureMoves.PopLSB();
            newBoard.WhiteRook_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteRook_Move(index, toSquare);
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateWhiteQueenMoves( ref Summary summary, int depth, int index, ulong pinMask)
    {
        Board newBoard;

        var potentialMoves = (AttackTables.PextBishopAttacks(White | Black, index) |
                             AttackTables.PextRookAttacks(White | Black, index)) & MoveMask & pinMask;
        
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = captureMoves.PopLSB();
            newBoard.WhiteQueen_Capture(index, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteQueen_Move(index, toSquare);
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }
    }

    public unsafe void AccumulateWhiteKingMoves( ref Summary summary, int depth, bool inCheck)
    {
        Board newBoard;
        var attackedSquares = WhiteKingDangerSquares();

        var potentialMoves = *(AttackTables.KingAttackTable + WhiteKingPos) & ~attackedSquares;
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = captureMoves.PopLSB();
            newBoard.WhiteKing_Capture(WhiteKingPos, toSquare);
            if (depth == 1) summary.AddCapture();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKing_Move(WhiteKingPos, toSquare);
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, toSquare);
        }

        if (WhiteKingPos != 4 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return;

        if ((CastleRights & CastleRights.WhiteKingSide) != 0 &&
            (White & Rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 6)) == 0 &&
            (attackedSquares & (1ul << 5)) == 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            newBoard.WhiteKing_KingSideCastle();
            if (depth == 1) summary.AddCastle();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, 5);
        }

        // Queen Side Castle
        if ((CastleRights & CastleRights.WhiteQueenSide) != 0 &&
            (White & Rook & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
              (attackedSquares & (1ul << 2)) == 0 &&
            (attackedSquares & (1ul << 3)) == 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            newBoard.WhiteKing_QueenSideCastle();
            if (depth == 1) summary.AddCastle();
            newBoard.AccumulateBlackMoves( ref summary, depth - 1, 3);
        }
    }
}