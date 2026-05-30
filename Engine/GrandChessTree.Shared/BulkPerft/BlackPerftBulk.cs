using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public unsafe ulong AccumulateBlackMovesBulk(int depth)
    {
        if (depth <= 1)
        {
            return AccumulateBlackMovesBulkCount();
        }

        // Shared TT lookup with XOR-guard (see PerftBulk.cs notes). Skipped when
        // HashTable == null (pure move-gen mode).
        ulong key = Hash ^ (White | Black);
        PerftBulkHashEntry* ptr = null;
        if (PerftBulk.HashTable != null)
        {
            ptr = PerftBulk.HashTable + (Hash & PerftBulk.HashTableMask);
            ulong cachedData = ptr->Data;
            ulong cachedGuard = ptr->HashXorData;
            if ((cachedGuard ^ cachedData) == key && (uint)(cachedData & 0xF) == (uint)depth)
            {
                return cachedData >> 4;
            }
        }

        ulong nodes = 0;

        var checkers = WhiteCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        nodes += AccumulateBlackKingMovesBulk( depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            if (ptr != null)
            {
                // Replace-by-depth on collision.
                ulong existingData = ptr->Data;
                uint existingDepth = (uint)(existingData & 0xF);
                if (existingData == 0 || existingDepth <= (uint)depth)
                {
                    ulong newDataEarly = (nodes << 4) | (uint)depth;
                    ptr->Data = newDataEarly;
                    ptr->HashXorData = key ^ newDataEarly;
                }
            }
            return nodes;
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
            nodes += AccumulateBlackPawnMovesBulk( depth, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackPawnMovesBulk( depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = Black & Knight & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackKnightMovesBulk( depth, index);
        }

        positions = Black & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackBishopMovesBulk( depth, index, AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackBishopMovesBulk( depth, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Rook& pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackRookMovesBulk( depth, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackRookMovesBulk( depth, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackQueenMovesBulk( depth, index,  AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            nodes += AccumulateBlackQueenMovesBulk( depth, index,  0xFFFFFFFFFFFFFFFF);
        }

        // Replace-by-depth: preserve deeper entries on collision.
        if (ptr != null)
        {
            ulong existingData = ptr->Data;
            uint existingDepth = (uint)(existingData & 0xF);
            if (existingData == 0 || existingDepth <= (uint)depth)
            {
                ulong newData = (nodes << 4) | (uint)depth;
                ptr->Data = newData;
                ptr->HashXorData = key ^ newData;
            }
        }
        return nodes;
    }
    public unsafe ulong AccumulateBlackPawnMovesBulk(int depth, int index, ulong pushPinMask, ulong capturePinMask)
    {
        ulong nodes = 0;
        Board newBoard;
        var rankIndex = index.GetRankIndex();
        int toSquare;
        if (rankIndex.IsSecondRank())
        {
            // Promoting moves
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;

            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();

                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_KnightPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_BishopPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_RookPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_QueenPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_KnightPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_BishopPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_RookPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_QueenPromotion(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
            }
        }
        else
        {
            var validMoves = *(AttackTables.BlackPawnAttackTable + index) & MoveMask & White & capturePinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture(index, toSquare);
                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
            }

            if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                // Inline legality check; only copy the board if the capture is legal.
                ulong fromBit = 1UL << index;
                ulong toBit = 1UL << (Constants.BlackEnpassantOffset + EnPassantFile);
                ulong captureBit = 1UL << (3 * 8 + EnPassantFile);
                ulong occAfter = (White & ~captureBit) | ((Black ^ fromBit) | toBit);
                ulong whiteAfter = White & ~captureBit;
                if ((AttackTables.PextBishopAttacks(occAfter, BlackKingPos) & (whiteAfter & (Bishop | Queen))) == 0 &&
                    (AttackTables.PextRookAttacks(occAfter, BlackKingPos) & (whiteAfter & (Rook | Queen))) == 0)
                {
                    newBoard = Unsafe.As<Board, Board>(ref this);
                    newBoard.BlackPawn_Enpassant(index, Constants.BlackEnpassantOffset + EnPassantFile);
                    nodes += newBoard.AccumulateWhiteMovesBulk(depth - 1);
                }
            }

            // Filter out double-push to rank-5 if intermediate (rank-6) is occupied.
            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            if (rankIndex.IsSeventhRank() && ((White | Black) & (1UL << (index - 8))) != 0)
            {
                validMoves &= ~(1UL << (index - 16));
            }
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 4)
                {
                    newBoard.BlackPawn_DoublePush(index, toSquare);
                }
                else
                {
                    newBoard.BlackPawn_Move(index, toSquare);
                }

                nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
            }
        }
        return nodes;

    }

    public unsafe ulong AccumulateBlackKnightMovesBulk(int depth, int index)
    {
        ulong nodes = 0;

        Board newBoard;
        int toSquare;

        var potentialMoves = *(AttackTables.KnightAttackTable + index) & MoveMask;
        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackKnight_Capture(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKnight_Move(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateBlackBishopMovesBulk(int depth, int index, ulong pinMask)
    {
        ulong nodes = 0;

        Board newBoard;

        var potentialMoves = AttackTables.PextBishopAttacks(White | Black, index) & MoveMask & pinMask;

        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackBishop_Capture(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackBishop_Move(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateBlackRookMovesBulk(int depth, int index, ulong pinMask)
    {
        ulong nodes = 0;

        Board newBoard;

        var potentialMoves = AttackTables.PextRookAttacks(White | Black, index) & MoveMask & pinMask;
        int toSquare;

        var captureMoves = potentialMoves & White;
        while (captureMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = captureMoves.PopLSB();

            newBoard.BlackRook_Capture(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackRook_Move(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateBlackQueenMovesBulk(int depth, int index, ulong pinMask)
    {
        ulong nodes = 0;

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
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackQueen_Move(index, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }
        return nodes;

    }

    public unsafe ulong AccumulateBlackKingMovesBulk(int depth, bool inCheck)
    {
        ulong nodes = 0;

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
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKing_Move(BlackKingPos, toSquare);
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }

        if (BlackKingPos != 60 || inCheck)
            // Can't castle if king is attacked or not on the starting position
            return nodes;

        // King Side Castle
        if ((CastleRights & CastleRights.BlackKingSide) != 0 &&
            (Black & Rook & Constants.BlackKingSideCastleRookPosition) > 0 &&
            ((White | Black)& Constants.BlackKingSideCastleEmptyPositions) == 0 &&
            (attackedSquares & (1ul << 61)) == 0 &&
            (attackedSquares & (1ul << 62)) == 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            newBoard.BlackKing_KingSideCastle();
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
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
            nodes += newBoard.AccumulateWhiteMovesBulk( depth - 1);
        }
        return nodes;

    }
}