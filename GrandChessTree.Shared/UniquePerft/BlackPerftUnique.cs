using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public bool CanBlackPawnEnpassant(int file)
    {
        Board newBoard;
        var positions = Black & Pawn;

        while (positions != 0)
        {
            var index = positions.PopLSB();
            var rankIndex = index.GetRankIndex();
            int toSquare;

            if (rankIndex.IsBlackEnPassantRankIndex() &&
                 Math.Abs(index.GetFileIndex() - file) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.BlackEnpassantOffset + file;

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                {
                    return true;
                }
            }
        }

        return false;
    }
    public unsafe void AccumulateBlackMovesUnique(int depth)
    {
        var ptr = (PerftUnique.HashTable + (Hash & PerftUnique.HashTableMask));
        var hashEntry = Unsafe.Read<PerftUniqueHashEntry>(ptr);
        if (hashEntry.FullHash == (Hash ^  (White | Black)) && depth == hashEntry.Depth)
        {
           // return;
        }

        hashEntry = default;
        hashEntry.FullHash = Hash ^ (White | Black);
        hashEntry.Depth = (byte)depth;

        if (depth == 0)
        {

            var hash = Hash;
            if (EnPassantFile < 8 && !CanBlackPawnEnpassant(EnPassantFile))
            {
                // Is move possible? If not remove possibility from hash
                hash ^= *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

            }

            PerftUnique.UniquePositions.Add(hash);

            //*ptr = hashEntry;
            // bulk count
            return;
        }

        var checkers = WhiteCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        AccumulateBlackKingMovesUnique( depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            * ptr = hashEntry;
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
            AccumulateBlackPawnMovesUnique( depth, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackPawnMovesUnique( depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = Black & Knight & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackKnightMovesUnique( depth, index);
        }

        positions = Black & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackBishopMovesUnique( depth, index, AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index));
        }
        
        positions = Black & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackBishopMovesUnique( depth, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Rook& pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackRookMovesUnique( depth, index, AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackRookMovesUnique( depth, index, 0xFFFFFFFFFFFFFFFF);
        }
        
        positions = Black & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackQueenMovesUnique( depth, index,  AttackTables.GetRayToEdgeDiagonal(BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(BlackKingPos, index));
        }
        
        positions = Black & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            AccumulateBlackQueenMovesUnique( depth, index,  0xFFFFFFFFFFFFFFFF);
        }

        *ptr = hashEntry;

        return;
    }
    public unsafe void AccumulateBlackPawnMovesUnique(int depth, int index, ulong pushPinMask, ulong capturePinMask)
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
                toSquare = validMoves.PopLSB();

                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_KnightPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_BishopPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_RookPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_QueenPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_KnightPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_BishopPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_RookPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_QueenPromotion(index, toSquare);
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
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
                newBoard.AccumulateWhiteMovesUnique( depth - 1);
            }

            if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.BlackEnpassantOffset + EnPassantFile;

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                {
                    newBoard.AccumulateWhiteMovesUnique( depth - 1);
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

                newBoard.AccumulateWhiteMovesUnique( depth - 1);
            }
        }
        return;

    }

    public unsafe void AccumulateBlackKnightMovesUnique(int depth, int index)
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKnight_Move(index, toSquare);
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateBlackBishopMovesUnique(int depth, int index, ulong pinMask)
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackBishop_Move(index, toSquare);
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateBlackRookMovesUnique(int depth, int index, ulong pinMask)
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackRook_Move(index, toSquare);
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateBlackQueenMovesUnique(int depth, int index, ulong pinMask)
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackQueen_Move(index, toSquare);
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateBlackKingMovesUnique(int depth, bool inCheck)
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKing_Move(BlackKingPos, toSquare);
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
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
            newBoard.AccumulateWhiteMovesUnique( depth - 1);
        }
        return;

    }
}