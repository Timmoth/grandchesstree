using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{

    private unsafe void AccumulateWhiteCompressedUniqueLeafNodes(int depth)
    {
        if (depth == 0)
        {
            var hash = Hash;
            if (EnPassantFile < 8 && !CanWhitePawnEnpassant())
            {
                // Is ep move possible? If not remove possibility from hash
                hash ^= Zobrist.EnPassantFile[EnPassantFile];
                EnPassantFile = 8;
            }

            if (UniqueLeafNodeGeneratorCompressed.boards.TryGetValue(hash, out var entry))
            {
                entry.occurrences++;
            }
            else
            {
                UniqueLeafNodeGeneratorCompressed.boards[hash] = new UniqueLeafNodeGeneratorCompressedEntry()
                {
                    board = BoardStateSerialization.Serialize(ref this, true),
                    order = 1,
                    occurrences = 1
                };
            }
            return;
        }

        var checkers = BlackCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

         AccumulateWhiteKingCompressedUniqueLeafNodes(depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
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
             AccumulateWhitePawnCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Pawn & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhitePawnCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Knight & ~pinMask;
        while (positions != 0)
        {
             AccumulateWhiteKnightCompressedUniqueLeafNodes(depth, positions.PopLSB());
        }

        positions = White & Bishop & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteBishopCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index));
        }

        positions = White & Bishop & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteBishopCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Rook & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteRookCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }
        positions = White & Rook & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteRookCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
        }

        positions = White & Queen & pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteQueenCompressedUniqueLeafNodes(depth, index, AttackTables.GetRayToEdgeDiagonal(WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(WhiteKingPos, index));
        }

        positions = White & Queen & ~pinMask;
        while (positions != 0)
        {
            var index = positions.PopLSB();
             AccumulateWhiteQueenCompressedUniqueLeafNodes(depth, index, 0xFFFFFFFFFFFFFFFF);
        }
    }

    public unsafe void AccumulateWhitePawnCompressedUniqueLeafNodes(int depth, int index, ulong pushPinMask, ulong capturePinMask)
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
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_BishopPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_RookPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_Capture_QueenPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
            }

            validMoves = *(AttackTables.WhitePawnPushTable + index) & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_KnightPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_BishopPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_RookPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.WhitePawn_QueenPromotion(index, toSquare);
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
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
                 newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
            }

            if (EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.WhiteEnpassantOffset + EnPassantFile;

                newBoard.WhitePawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByBlackSliders(newBoard.WhiteKingPos))
                {
                     newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
                }
            }

// Compute all valid pawn push moves for this pawn (including double pushes)
            validMoves = AttackTables.WhitePawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;

// Loop over each destination square (each set bit in validMoves)
            while (validMoves != 0)
            {
                // Pop the least-significant set bit; that gives us a destination square.
                toSquare = validMoves.PopLSB();
    
                // Create a new board instance for this move.

                // For White double-pushes, the pawn must be on its initial rank (second rank, index == 1)
                // and the destination must be two squares forward (rank 3). In that case, the intermediate
                // square is one rank ahead (index + 8) and must be unoccupied.
                int intermediateSquare = index + 8;
    
                // Compute a flag for whether this move is a double push.
                // (Using a ternary operator to yield 1 if true, 0 if false.)
                // Note: For white, a double push happens when the pawn starts on rank 1 (second rank)
                // and lands on rank 3, and the intermediate square is empty.
                if((rankIndex.IsSecondRank() && toSquare.GetRankIndex() == 3))
                {
                    if(((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue;
                    }
                    newBoard = Unsafe.As<Board, Board>(ref this);
                    newBoard.WhitePawn_DoublePush(index, toSquare);
                }
                else
                {
                    newBoard = Unsafe.As<Board, Board>(ref this);
                    newBoard.WhitePawn_Move(index, toSquare);
                }
                
                newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
            }
          
        }
             return;

    }

    public unsafe void AccumulateWhiteKnightCompressedUniqueLeafNodes(int depth, int index)
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKnight_Move(index, toSquare);
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteBishopCompressedUniqueLeafNodes(int depth, int index, ulong pinMask)
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteBishop_Move(index, toSquare);
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteRookCompressedUniqueLeafNodes(int depth, int index, ulong pinMask)
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteRook_Move(index, toSquare);
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteQueenCompressedUniqueLeafNodes(int depth, int index, ulong pinMask)
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteQueen_Move(index, toSquare);
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }
        return;

    }

    public unsafe void AccumulateWhiteKingCompressedUniqueLeafNodes(int depth, bool inCheck)
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);
            toSquare = emptyMoves.PopLSB();
            newBoard.WhiteKing_Move(WhiteKingPos, toSquare);
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
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
             newBoard.AccumulateBlackCompressedUniqueLeafNodes(depth - 1);
        }

        return;
    }
}