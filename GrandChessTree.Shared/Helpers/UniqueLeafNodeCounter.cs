using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared.Helpers;

using System;
using System.ComponentModel;
using System.Numerics;
using System.Reflection;


public static unsafe class UniqueLeafNodeCounter
{

    public struct UniqueLeafNodeHashEntry
    {
        public ulong FullHash;
        public byte Depth;
    }
    #region HashTable
    public static uint HashTableMask;
    public static int HashTableSize;

    [ThreadStatic] public static UniqueLeafNodeHashEntry* HashTable;

    static UniqueLeafNodeCounter()
    {

    }
    private static uint CalculateHashTableEntries(int sizeInMb)
    {
        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(UniqueLeafNodeHashEntry);
        if (!BitOperations.IsPow2(transpositionCount))
        {
            transpositionCount = BitOperations.RoundUpToPowerOf2(transpositionCount) >> 1;
        }

        if (transpositionCount > int.MaxValue)
        {
            throw new ArgumentException("Hash table too large");
        }

        return (uint)transpositionCount;
    }

    public static UniqueLeafNodeHashEntry* AllocateHashTable(int sizeInMb = 512)
    {
        HashTableSize = (int)CalculateHashTableEntries(sizeInMb);
        HashTableMask = (uint)HashTableSize - 1;

        const nuint alignment = 64;

        var bytes = ((nuint)sizeof(UniqueLeafNodeHashEntry) * (nuint)HashTableSize);
        var block = NativeMemory.AlignedAlloc(bytes, alignment);
        NativeMemory.Clear(block, bytes);

        return (UniqueLeafNodeHashEntry*)block;
    }

    public static void FreeHashTable()
    {
        if (HashTable != null)
        {
            NativeMemory.AlignedFree(HashTable);
            HashTable = null;
        }
    }
    #endregion

    public static void CountUniqueLeafNodes(HashSet<ulong> hyperLogLog, ref Board board, int depth, bool whiteToMove)
    {
        if (whiteToMove)
        {
            var positions = board.White & board.Pawn;
            while (positions != 0) GenerateWhitePawnNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.White & board.Knight;
            while (positions != 0) GenerateWhiteKnightNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.White & board.Bishop;
            while (positions != 0) GenerateWhiteBishopNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.White & board.Rook;
            while (positions != 0) GenerateWhiteRookNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.White & board.Queen;
            while (positions != 0) GenerateWhiteQueenNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            GenerateWhiteKingNodes(ref board, hyperLogLog,  depth, board.WhiteKingPos);
        }
        else
        {
            var positions = board.Black & board.Pawn;
            while (positions != 0) GenerateBlackPawnNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.Black & board.Knight;
            while (positions != 0) GenerateBlackKnightNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.Black & board.Bishop;
            while (positions != 0) GenerateBlackBishopNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.Black & board.Rook;
            while (positions != 0) GenerateBlackRookNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            positions = board.Black & board.Queen;
            while (positions != 0) GenerateBlackQueenNodes(ref board, hyperLogLog,  depth, positions.PopLSB());

            GenerateBlackKingNodes(ref board, hyperLogLog,  depth, board.BlackKingPos);
        }
    }

    private static void GenerateWhiteNodes(ref Board board, HashSet<ulong> boards, int depth)
    {
        if (depth == 0)
        {
            var hash = board.Hash;
  
            if (board.EnPassantFile < 8 && !CanWhitePawnEnpassant(ref board))
            {
                // Is move possible? If not remove possibility from hash
                hash ^= Zobrist.EnPassantFile[board.EnPassantFile];
            }

            boards.Add(hash);
            //if (hash == 11926642270410332418ul)
            //{
            //    Console.WriteLine(board.ToFen(true, 0, 1));
            //    Console.WriteLine("wrooong");
            //}
            return;
        }

        var ptr = (UniqueLeafNodeCounter.HashTable + (board.Hash & UniqueLeafNodeCounter.HashTableMask));
        var hashEntry = Unsafe.Read<UniqueLeafNodeHashEntry>(ptr);
        if (hashEntry.FullHash == (board.Hash ^ (board.White | board.Black)) && depth <= hashEntry.Depth)
        {
          //  return;
        }

        hashEntry = default;
        hashEntry.FullHash = board.Hash ^ (board.White | board.Black);
        hashEntry.Depth = (byte)depth;

        var positions = board.White & board.Pawn;
        while (positions != 0) GenerateWhitePawnNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.White & board.Knight;
        while (positions != 0) GenerateWhiteKnightNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.White & board.Bishop;
        while (positions != 0) GenerateWhiteBishopNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.White & board.Rook;
        while (positions != 0) GenerateWhiteRookNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.White & board.Queen;
        while (positions != 0) GenerateWhiteQueenNodes(ref board, boards,  depth, positions.PopLSB());

        GenerateWhiteKingNodes(ref board, boards,  depth, board.WhiteKingPos);
      //  *ptr = hashEntry;

    }

    private static void GenerateBlackNodes(ref Board board, HashSet<ulong> boards,  int depth)
    {
        if (depth == 0)
        {
            var hash = board.Hash;
            if (board.EnPassantFile < 8 && !CanBlackPawnEnpassant(ref board))
            {
                // Is move possible? If not remove possibility from hash
                hash ^= Zobrist.EnPassantFile[board.EnPassantFile];
            }

            boards.Add(hash);
            if (hash == 11926642270410332418ul)
            {
                Console.WriteLine(board.ToFen(true, 0, 1));
                Console.WriteLine("wrooong");
            }

            return;
        }

        var ptr = (UniqueLeafNodeCounter.HashTable + (board.Hash & UniqueLeafNodeCounter.HashTableMask));
        var hashEntry = Unsafe.Read<UniqueLeafNodeHashEntry>(ptr);
        if (hashEntry.FullHash == (board.Hash ^ (board.White | board.Black)) && depth <= hashEntry.Depth)
        {
          //  return;
        }

        hashEntry = default;
        hashEntry.FullHash = board.Hash ^ (board.White | board.Black);
        hashEntry.Depth = (byte)depth;

        var positions = board.Black & board.Pawn;
        while (positions != 0) GenerateBlackPawnNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.Black & board.Knight;
        while (positions != 0) GenerateBlackKnightNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.Black & board.Bishop;
        while (positions != 0) GenerateBlackBishopNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.Black & board.Rook;
        while (positions != 0) GenerateBlackRookNodes(ref board, boards,  depth, positions.PopLSB());

        positions = board.Black & board.Queen;
        while (positions != 0) GenerateBlackQueenNodes(ref board, boards,  depth, positions.PopLSB());

       GenerateBlackKingNodes(ref board, boards,  depth, board.BlackKingPos);

        //*ptr = hashEntry;
    }

    private static bool CanWhitePawnEnpassant(ref Board board)
    {
        var toSquare = Constants.WhiteEnpassantOffset + board.EnPassantFile;
        Board newBoard = default;
        var pawns = board.White & board.Pawn;
        while (pawns != 0)
        {
            var fromSquare = pawns.PopLSB();

            if (fromSquare.GetRankIndex().IsWhiteEnPassantRankIndex() &&
                Math.Abs(fromSquare.GetFileIndex() - board.EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref board);

                newBoard.WhitePawn_Enpassant(fromSquare, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void GenerateWhitePawnNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var rankIndex = index.GetRankIndex();
        var posEncoded = 1UL << index;
        int toSquare;

        if (board.EnPassantFile != 8 && rankIndex.IsWhiteEnPassantRankIndex() &&
            Math.Abs(index.GetFileIndex() - board.EnPassantFile) == 1)
        {
            board.CloneTo(ref newBoard);
            toSquare = Constants.WhiteEnpassantOffset + board.EnPassantFile;

            newBoard.WhitePawn_Enpassant(index, toSquare);
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        var canPromote = rankIndex.IsSeventhRank();

        // Take left piece
        var target = posEncoded.ShiftUpLeft();
        if ((board.Black & target) != 0)
        {
            toSquare = index.ShiftUpLeft();
            if (canPromote)
            {
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_KnightPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_BishopPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_RookPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_QueenPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            }
            else
            {
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            }
        }

        // Take right piece
        target = posEncoded.ShiftUpRight();
        if ((board.Black & target) != 0)
        {
            toSquare = index.ShiftUpRight();
            if (canPromote)
            {
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_KnightPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_BishopPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_RookPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture_QueenPromotion(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            }
            else
            {
                board.CloneTo(ref newBoard);
                newBoard.WhitePawn_Capture(index, toSquare);
                if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                    GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            }
        }

        // Move up
        target = posEncoded.ShiftUp();
        if (((board.White | board.Black) & target) > 0)
            // Blocked from moving down
            return;

        toSquare = index.ShiftUp();
        if (canPromote)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhitePawn_KnightPromotion(index, toSquare);
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            board.CloneTo(ref newBoard);
            newBoard.WhitePawn_BishopPromotion(index, toSquare);
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            board.CloneTo(ref newBoard);
            newBoard.WhitePawn_RookPromotion(index, toSquare);
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            board.CloneTo(ref newBoard);
            newBoard.WhitePawn_QueenPromotion(index, toSquare);
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
            return;
        }

        board.CloneTo(ref newBoard);
        newBoard.WhitePawn_Move(index, toSquare);
        if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
            GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        target = target.ShiftUp();
        if (rankIndex.IsSecondRank() && ((board.White | board.Black) & target) == 0)
        {
            // Double push
            board.CloneTo(ref newBoard);
            newBoard.WhitePawn_DoublePush(index, toSquare.ShiftUp());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateWhiteKnightNodes(ref Board board, HashSet<ulong> boards,  int depth,
        int index)
    {
        Board newBoard = default;

        var potentialMoves = *(AttackTables.KnightAttackTable + index);
        var captureMoves = potentialMoves & board.Black;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteKnight_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteKnight_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateWhiteBishopNodes(ref Board board, HashSet<ulong> boards,  int depth,
        int index)
    {
        Board newBoard = default;

        var potentialMoves = AttackTables.PextBishopAttacks(board.White | board.Black, index);
        var captureMoves = potentialMoves & board.Black;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteBishop_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteBishop_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateWhiteRookNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var potentialMoves = AttackTables.PextRookAttacks(board.White | board.Black, index);
        var captureMoves = potentialMoves & board.Black;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteRook_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteRook_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateWhiteQueenNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var potentialMoves = AttackTables.PextBishopAttacks(board.White | board.Black, index) |
                             AttackTables.PextRookAttacks(board.White | board.Black, index);
        var captureMoves = potentialMoves & board.Black;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteQueen_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteQueen_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateWhiteKingNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var potentialMoves = *(AttackTables.KingAttackTable + index);

        var captureMoves = potentialMoves & board.Black;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteKing_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteKing_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        if (index != 4 || board.IsAttackedByBlack(board.WhiteKingPos))
            // Can't castle if king is attacked or not on the starting position
            return;

        if ((board.CastleRights & CastleRights.WhiteKingSide) != 0 &&
            ((board.White & board.Rook) & Constants.WhiteKingSideCastleRookPosition) > 0 &&
            ((board.White | board.Black) & Constants.WhiteKingSideCastleEmptyPositions) == 0 &&
            !board.IsAttackedByBlack(6) &&
            !board.IsAttackedByBlack(5))
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteKing_KingSideCastle();
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }

        // Queen Side Castle
        if ((board.CastleRights & CastleRights.WhiteQueenSide) != 0 &&
            ((board.White & board.Rook) & Constants.WhiteQueenSideCastleRookPosition) > 0 &&
            ((board.White | board.Black) & Constants.WhiteQueenSideCastleEmptyPositions) == 0 &&
            !board.IsAttackedByBlack(2) &&
            !board.IsAttackedByBlack(3))
        {
            board.CloneTo(ref newBoard);
            newBoard.WhiteKing_QueenSideCastle();
            if (!newBoard.IsAttackedByBlack(newBoard.WhiteKingPos))
                GenerateBlackNodes(ref newBoard, boards,  depth - 1);
        }
    }



    private static bool CanBlackPawnEnpassant(ref Board board)
    {
        var toSquare = Constants.BlackEnpassantOffset + board.EnPassantFile;


        Board newBoard = default;
        var positions = board.Black & board.Pawn;
        while (positions != 0)
        {
            var index = positions.PopLSB();
            var rankIndex = index.GetRankIndex();
            var posEncoded = 1UL << index;

            var captureSquare = 1UL << (index.GetRankIndex() * 8 + board.EnPassantFile);

            if ((board.Pawn & board.White & captureSquare) != 0 && 
                rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - board.EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref board);

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                {
                    return true;
                }
            }
        }


        return false;
    }

    private static void GenerateBlackPawnNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var rankIndex = index.GetRankIndex();
        var posEncoded = 1UL << index;
        int toSquare;

        if (board.EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
            Math.Abs(index.GetFileIndex() - board.EnPassantFile) == 1)
        {
            board.CloneTo(ref newBoard);
            toSquare = Constants.BlackEnpassantOffset + board.EnPassantFile;

            newBoard.BlackPawn_Enpassant(index, toSquare);
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        var canPromote = rankIndex.IsSecondRank();

        // Left capture
        var target = posEncoded.ShiftDownLeft();
        if ((board.White & target) != 0)
        {
            toSquare = index.ShiftDownLeft();
            if (canPromote)
            {
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_KnightPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_BishopPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_RookPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_QueenPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            }
            else
            {
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            }
        }

        // Right capture
        target = posEncoded.ShiftDownRight();
        if ((board.White & target) != 0)
        {
            toSquare = index.ShiftDownRight();

            if (canPromote)
            {
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_KnightPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_BishopPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_RookPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture_QueenPromotion(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            }
            else
            {
                board.CloneTo(ref newBoard);
                newBoard.BlackPawn_Capture(index, toSquare);
                if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                    GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            }
        }

        // Vertical moves
        target = posEncoded.ShiftDown();
        if (((board.White | board.Black) & target) > 0)
            // Blocked from moving down
            return;

        toSquare = index.ShiftDown();
        if (canPromote)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackPawn_KnightPromotion(index, toSquare);
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            board.CloneTo(ref newBoard);
            newBoard.BlackPawn_BishopPromotion(index, toSquare);
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            board.CloneTo(ref newBoard);
            newBoard.BlackPawn_RookPromotion(index, toSquare);
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            board.CloneTo(ref newBoard);
            newBoard.BlackPawn_QueenPromotion(index, toSquare);
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
            return;
        }

        // Move down
        board.CloneTo(ref newBoard);
        newBoard.BlackPawn_Move(index, toSquare);
        if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
            GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        target = target.ShiftDown();
        if (rankIndex.IsSeventhRank() && ((board.White | board.Black) & target) == 0)
        {
            // Double push
            board.CloneTo(ref newBoard);
            newBoard.BlackPawn_DoublePush(index, toSquare.ShiftDown());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateBlackKnightNodes(ref Board board, HashSet<ulong> boards,  int depth,
        int index)
    {
        Board newBoard = default;

        var potentialMoves = *(AttackTables.KnightAttackTable + index);
        var captureMoves = potentialMoves & board.White;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackKnight_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackKnight_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateBlackBishopNodes(ref Board board, HashSet<ulong> boards,  int depth,
        int index)
    {
        Board newBoard = default;

        var potentialMoves = AttackTables.PextBishopAttacks(board.White | board.Black, index);
        var captureMoves = potentialMoves & board.White;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackBishop_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackBishop_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateBlackRookNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var potentialMoves = AttackTables.PextRookAttacks(board.White | board.Black, index);
        var captureMoves = potentialMoves & board.White;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackRook_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackRook_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateBlackQueenNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var potentialMoves = AttackTables.PextBishopAttacks(board.White | board.Black, index) |
                             AttackTables.PextRookAttacks(board.White | board.Black, index);
        var captureMoves = potentialMoves & board.White;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackQueen_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackQueen_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }
    }

    private static void GenerateBlackKingNodes(ref Board board, HashSet<ulong> boards,  int depth, int index)
    {
        Board newBoard = default;

        var potentialMoves = *(AttackTables.KingAttackTable + index);
        var captureMoves = potentialMoves & board.White;
        while (captureMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackKing_Capture(index, captureMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        var emptyMoves = potentialMoves & ~(board.White | board.Black);
        while (emptyMoves != 0)
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackKing_Move(index, emptyMoves.PopLSB());
            if (!newBoard.IsAttackedByWhite(newBoard.BlackKingPos))
                GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        if (index != 60 || board.IsAttackedByWhite(board.BlackKingPos))
            // Can't castle if king is attacked or not on the starting position
            return;

        // King Side Castle
        if ((board.CastleRights & CastleRights.BlackKingSide) != 0 &&
            ((board.Black & board.Rook) & Constants.BlackKingSideCastleRookPosition) > 0 &&
            ((board.White | board.Black) & Constants.BlackKingSideCastleEmptyPositions) == 0 &&
            !board.IsAttackedByWhite(61) &&
            !board.IsAttackedByWhite(62))
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackKing_KingSideCastle();
            if (!newBoard.IsAttackedByWhite(62)) GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }

        // Queen Side Castle
        if ((board.CastleRights & CastleRights.BlackQueenSide) != 0 &&
            ((board.Black & board.Rook) & Constants.BlackQueenSideCastleRookPosition) > 0 &&
            ((board.White | board.Black) & Constants.BlackQueenSideCastleEmptyPositions) == 0 &&
            !board.IsAttackedByWhite(58) &&
            !board.IsAttackedByWhite(59))
        {
            board.CloneTo(ref newBoard);
            newBoard.BlackKing_QueenSideCastle();
            if (!newBoard.IsAttackedByWhite(58)) GenerateWhiteNodes(ref newBoard, boards,  depth - 1);
        }
    }
}