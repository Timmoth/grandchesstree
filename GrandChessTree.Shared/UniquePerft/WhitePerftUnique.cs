using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
public bool CanWhitePawnEnpassant()
{
    // White captures en passant when a Black pawn double-pushed from rank 6 to rank 4.
    // The Black pawn passed over rank 5. So, the white pawn lands on rank 5.
    ulong targetSquare = 1UL << (5 * 8 + EnPassantFile);  // Landing square (rank 5, given file)
    // The Black pawn that moved is on rank 4 (one rank below target)
    ulong captureSquare = targetSquare >> 8;       // Black pawn’s square (rank 4)

    // White pawn must be on rank 4 to capture (i.e. adjacent to the black pawn)
    ulong validWhitePawns = White & Pawn & Constants.RankMasks[4];

    // A white pawn capturing en passant will move diagonally upward.
    // To determine its origin, we “reverse” the move:
    // - For a capture coming from the left (white pawn is to the right of target), origin = targetSquare >> 9.
    // - For a capture coming from the right, origin = targetSquare >> 7.
    ulong leftCandidate  = (EnPassantFile != 0) ? (targetSquare >> 9) : 0UL; // only if target not in file A
    ulong rightCandidate = (EnPassantFile != 7) ? (targetSquare >> 7) : 0UL; // only if target not in file H

    ulong enPassantCandidates = (validWhitePawns & leftCandidate) | (validWhitePawns & rightCandidate);

    while (enPassantCandidates != 0)
    {
        int fromSquare = enPassantCandidates.PopLSB();
        // Simulate the move:
        // Remove white pawn from its original square and add it on target.
        var white = (White ^ (1UL << fromSquare)) | targetSquare;
        var black = Black & ~captureSquare;
        var occupancy = white | black;
        
        var slidingAttackers = (AttackTables.PextBishopAttacks(occupancy, WhiteKingPos) & (black & (Bishop | Queen))) |
                               (AttackTables.PextRookAttacks(occupancy, WhiteKingPos) & (black & (Rook | Queen)));
        if (slidingAttackers == 0)
        {
            return true; // Found a legal en passant capture.
        }
    }
    return false; // No legal en passant capture found.
}


    private unsafe void AccumulateWhiteMovesUnique(int depth)
    {
        var ptr = (PerftUnique.HashTable + (Hash & PerftUnique.HashTableMask));
        var hashEntry = Unsafe.Read<PerftUniqueHashEntry>(ptr);
        if (hashEntry.FullHash == (Hash ^ (White | Black)) && depth == hashEntry.Depth)
        {
           return;
        }
        hashEntry = default;
        hashEntry.FullHash = Hash ^ (White | Black);
        hashEntry.Depth = (byte)depth;

        if (depth == 0)
        {
            var hash = Hash;
            if (EnPassantFile < 8 && !CanWhitePawnEnpassant())
            {
                // Is move possible? If not remove possibility from hash
                hash ^= Zobrist.EnPassantFile[EnPassantFile];
            }

            PerftUnique.UniquePositions.Add(hash);
            return;
        }

        var checkers = BlackCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

         AccumulateWhiteKingMovesUnique(depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
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
             AccumulateWhitePawnMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhitePawnMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Knight & ~pinMask;
        while (positions != 0)
        {
             AccumulateWhiteKnightMovesUnique(depth, positions.PopLSB());
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteBishopMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteBishopMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteRookMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteRookMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteQueenMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }

        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteQueenMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        *ptr = hashEntry;
    }

    public unsafe void AccumulateWhitePawnMovesUnique(int depth, int index, ulong pushPinMask, ulong capturePinMask)
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
                toSquare = validMoves.PopLSB();

                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_KnightPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_BishopPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_RookPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_QueenPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
            }

            validMoves = *(AttackTables.WhitePawnPushTable + index) & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_KnightPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_BishopPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_RookPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_QueenPromotion(index, toSquare);
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
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
                 newBoard.AccumulateBlackMovesUnique( depth - 1);
            }

            if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

                newBoard.WhitePawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                {
                     newBoard.AccumulateBlackMovesUnique(depth - 1);
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

                newBoard.AccumulateBlackMovesUnique( depth - 1);
            }
        }
             return;

    }

    public unsafe void AccumulateWhiteKnightMovesUnique(int depth, int index)
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKnight_Move(index, toSquare);
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteBishopMovesUnique(int depth, int index, ulong pinMask)
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteBishop_Move(index, toSquare);
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteRookMovesUnique(int depth, int index, ulong pinMask)
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteRook_Move(index, toSquare);
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteQueenMovesUnique(int depth, int index, ulong pinMask)
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteQueen_Move(index, toSquare);
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteKingMovesUnique(int depth, bool inCheck)
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKing_Move(WhiteKingPos, toSquare);
             newBoard.AccumulateBlackMovesUnique( depth - 1);
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
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
             newBoard.AccumulateBlackMovesUnique( depth - 1);
        }

        return;
    }
}