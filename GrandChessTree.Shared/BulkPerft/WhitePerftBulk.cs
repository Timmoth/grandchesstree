using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    private unsafe ulong AccumulateWhiteMovesBulk(int depth)
    {
        if (depth <= 1)
        {
            // bulk count
            return AccumulateWhiteMovesBulkCount();
        }
        var ptr = (PerftBulk.HashTable + (Hash & PerftBulk.HashTableMask));
        var hashEntry = Unsafe.Read<PerftBulkHashEntry>(ptr);
        if (hashEntry.FullHash == (Hash ^ (White | Black)) && depth == hashEntry.Depth)
        {
            return hashEntry.Nodes;
        }

        hashEntry = default;
        hashEntry.FullHash = Hash ^ (White | Black);
        hashEntry.Depth = (byte)depth;

        ulong nodes = 0;


        var checkers = BlackCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        nodes += AccumulateWhiteKingMovesBulk(depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            hashEntry.Nodes = nodes;
            *ptr = hashEntry;
            return nodes;
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
            nodes += AccumulateWhitePawnMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhitePawnMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Knight & ~pinMask;
        while (positions != 0)
        {
            nodes += AccumulateWhiteKnightMovesBulk(depth, positions.PopLSB());
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteBishopMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteBishopMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteRookMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteRookMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteQueenMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }

        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateWhiteQueenMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        hashEntry.Nodes = nodes;
        *ptr = hashEntry;

        return nodes;
    }

    public unsafe ulong AccumulateWhitePawnMovesBulk(int depth, int index, ulong pushPinMask, ulong capturePinMask)
    {
        ulong nodes = 0;

        Board newBoard ;
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSeventhRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.WhitePawnAttackTable +index) & MoveMask & Black & capturePinMask;

            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();

                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_KnightPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_BishopPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_RookPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_QueenPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
            }

            validMoves = *(AttackTables.WhitePawnPushTable + index) & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_KnightPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_BishopPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_RookPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_QueenPromotion(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
            }
        }
        else
        {
            var validMoves = *(AttackTables.WhitePawnAttackTable + index) & MoveMask & Black & capturePinMask;

            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture(index, toSquare);
                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
            }

            if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

                newBoard.WhitePawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByBlackSliders(newBoard.WhiteKingPos))
                {
                    nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
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

                nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
            }
        }
             return nodes;

    }

    public unsafe ulong AccumulateWhiteKnightMovesBulk(int depth, int index)
    {
        ulong nodes = 0;

        int toSquare;
        Board newBoard;
        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {    
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();
            newBoard.WhiteKnight_Capture(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKnight_Move(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateWhiteBishopMovesBulk(int depth, int index, ulong pinMask)
    {
        ulong nodes = 0;

        Board newBoard;
        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;

        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = captureMoves.PopLSB();
            newBoard.WhiteBishop_Capture(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteBishop_Move(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateWhiteRookMovesBulk(int depth, int index, ulong pinMask)
    {
        ulong nodes = 0;

        Board newBoard;
        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        int toSquare;
        var captureMoves = potentialMoves & Black;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = captureMoves.PopLSB();
            newBoard.WhiteRook_Capture(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteRook_Move(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateWhiteQueenMovesBulk(int depth, int index, ulong pinMask)
    {
        ulong nodes = 0;

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
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteQueen_Move(index, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateWhiteKingMovesBulk(int depth, bool inCheck)
    {
        ulong nodes = 0;

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
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKing_Move(WhiteKingPos, toSquare);
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        if (WhiteKingPos != 4 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return nodes;


        if ((CastleRights & CastleRights.WhiteKingSide) != 0 &&
            (White & Rook & Constants.WhiteKingSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 6)) == 0 &&
            (attackedSquares & (1ul << 5)) == 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            newBoard.WhiteKing_KingSideCastle();
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
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
            nodes += newBoard.AccumulateBlackMovesBulk( depth - 1);
        }

        return nodes;
    }
}