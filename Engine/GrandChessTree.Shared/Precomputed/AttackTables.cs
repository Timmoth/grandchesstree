using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Shared.Precomputed;
public static unsafe class AttackTables
{
    public static readonly ulong* RookAttackRays;
    public static readonly ulong[] RookAttackMasksAll = new ulong[64];
    public static readonly MagicBitBoard[] RookMagics = new MagicBitBoard[64];
    public static readonly ulong* BishopAttackRays;
    public static readonly ulong[] BishopAttackMasksAll = new ulong[64];
    public static readonly MagicBitBoard[] BishopMagics = new MagicBitBoard[64];

    private static readonly int[] WhitePawnOffsets = { 7, 9 };
    private static readonly int[] BlackPawnOffsets = { -7, -9 };

    private static readonly int[] KnightOffsets = { 17, 15, 10, 6, -6, -10, -15, -17 };
    private static readonly int[] KingOffsets = { 1, -1, 8, -8, 9, -9, 7, -7 };
    public static readonly ulong* KnightAttackTable;
    public static readonly ulong* KingAttackTable;
    public static readonly ulong* WhitePawnAttackTable;
    public static readonly ulong* BlackPawnAttackTable;

    public static readonly ulong* WhitePawnCaptureTable;
    public static readonly ulong* BlackPawnCaptureTable;
    public static readonly ulong* WhitePawnPushTable;
    public static readonly ulong* BlackPawnPushTable;

    private static readonly ulong* PextAttacks;
    private static readonly ulong* BishopPextOffset;
    private static readonly ulong* RookPextOffset;

    public static readonly ulong* LineBitBoards;
    public static readonly ulong* LineBitBoardsStraight;
    public static readonly ulong* LineBitBoardsDiagonal;
    public static readonly ulong* LineBitBoardsStraightToEdge;
    public static readonly ulong* LineBitBoardsDiagonalToEdge;
    public static readonly ulong* LineBitBoardsInclusive;

    static AttackTables()
    {
        PextAttacks = Allocate<ulong>(5248 + 102400);
        BishopPextOffset = Allocate<ulong>(64);
        RookPextOffset = Allocate<ulong>(64);
        KnightAttackTable = Allocate<ulong>(64);
        KingAttackTable = Allocate<ulong>(64);

        WhitePawnAttackTable = Allocate<ulong>(64);
        BlackPawnAttackTable = Allocate<ulong>(64);
        WhitePawnCaptureTable = Allocate<ulong>(64*64);
        BlackPawnCaptureTable = Allocate<ulong>(64*64);
        WhitePawnPushTable = Allocate<ulong>(64);
        BlackPawnPushTable = Allocate<ulong>(64);

        RookAttackRays = Allocate<ulong>(64);
        BishopAttackRays = Allocate<ulong>(64);
        LineBitBoards = Allocate<ulong>(64 * 64);
        LineBitBoardsStraight = Allocate<ulong>(64 * 64);
        LineBitBoardsDiagonal = Allocate<ulong>(64 * 64);
        LineBitBoardsStraightToEdge = Allocate<ulong>(64 * 64);
        LineBitBoardsDiagonalToEdge = Allocate<ulong>(64 * 64);
        LineBitBoardsInclusive = Allocate<ulong>(64 * 64);
        var rand = Random.Shared;
        for (var i = 0; i < 64; i++)
        {
            RookAttackRays[i] = RookAttackMask(i);
            RookAttackMasksAll[i] = RookAttackMaskAll(i);
            BishopAttackRays[i] = BishopAttackMask(i);
            BishopAttackMasksAll[i] = BishopAttackMaskAll(i);
        }

        for (var i = 0; i < 64; i++)
        {
            RookMagics[i] = GetRookMagicNumbers(i, rand);
            BishopMagics[i] = GetBishopMagicNumbers(i, rand);
        }

        // Initialize the knight and king attack tables
        for (var square = 0; square < 64; square++)
        {
            var knightAttacks = 0UL;
            var kingAttacks = 0UL;
            var whitePawnAttacks = 0UL;
            var blackPawnAttacks = 0UL;
            var rank = square.GetRankIndex();
            var file = square.GetFileIndex();

            foreach (var offset in WhitePawnOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 2 &&
                    Math.Abs(file - targetFile) <= 2)
                    whitePawnAttacks |= 1UL << targetSquare;
            }

            foreach (var offset in BlackPawnOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 2 &&
                    Math.Abs(file - targetFile) <= 2)
                    blackPawnAttacks |= 1UL << targetSquare;
            }

            foreach (var offset in KnightOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 2 &&
                    Math.Abs(file - targetFile) <= 2)
                    knightAttacks |= 1UL << targetSquare;
            }

            foreach (var offset in KingOffsets)
            {
                var targetSquare = square + offset;
                var targetRank = targetSquare.GetRankIndex();
                var targetFile = targetSquare.GetFileIndex();

                if (targetSquare is >= 0 and < 64 && Math.Abs(rank - targetRank) <= 1 &&
                    Math.Abs(file - targetFile) <= 1)
                    kingAttacks |= 1UL << targetSquare;
            }

            // Pawn Enpassant attacks
            for(var enPassantFile = 0; enPassantFile <= 8; enPassantFile++)
            {
                var rankIndex = square.GetRankIndex();

                if (rankIndex.IsWhiteEnPassantRankIndex() &&
                    Math.Abs(square.GetFileIndex() - enPassantFile) == 1)
                {
                    var toSquare = Constants.WhiteEnpassantOffset + enPassantFile;
                    WhitePawnCaptureTable[square * 64 + enPassantFile] = whitePawnAttacks | (1ul << toSquare);
                }
                else
                {
                    // No enpassant
                    WhitePawnCaptureTable[square * 64 + enPassantFile] = whitePawnAttacks;
                }

                if (rankIndex.IsBlackEnPassantRankIndex() &&
                      Math.Abs(square.GetFileIndex() - enPassantFile) == 1)
                {
                    var toSquare = Constants.BlackEnpassantOffset + enPassantFile;
                    BlackPawnCaptureTable[square * 64 + enPassantFile] = whitePawnAttacks | (1ul << toSquare);
                }
                else
                {
                    // No enpassant
                    BlackPawnCaptureTable[square * 64 + enPassantFile] = blackPawnAttacks;
                }
            }

            WhitePawnPushTable[square] = (rank < 7)
              ? (1ul << square.ShiftUp()) |
                (rank.IsSecondRank() ? (1ul << square.ShiftUp().ShiftUp()) : 0ul)
              : 0ul;

            BlackPawnPushTable[square] = (rank > 0)
                ? (1ul << square.ShiftDown()) |
                  (rank.IsSeventhRank() ? (1ul << square.ShiftDown().ShiftDown()) : 0ul)
                : 0ul;

            WhitePawnAttackTable[square] = whitePawnAttacks;
            BlackPawnAttackTable[square] = blackPawnAttacks;
            KnightAttackTable[square] = knightAttacks;
            KingAttackTable[square] = kingAttacks;
        }

        if (Bmi2.X64.IsSupported)
        {
            ulong pextAttackIndex = 0;
            for (var square = 0; square < 64; square++)
            {
                BishopPextOffset[square] = pextAttackIndex;
                var bishopAttackMask = BishopAttackRays[square];
                var patterns = 1UL << (byte)ulong.PopCount(bishopAttackMask);
                for (ulong i = 0; i < patterns; i++)
                {
                    var blockers = Bmi2.X64.ParallelBitDeposit(i, bishopAttackMask);
                    PextAttacks[pextAttackIndex++] = BishopMagics[square].GetMoves(blockers);
                }
            }

            for (var square = 0; square < 64; square++)
            {
                RookPextOffset[square] = pextAttackIndex;
                var rookAttackMask = RookAttackRays[square];
                var patterns = 1UL << (byte)ulong.PopCount(rookAttackMask);
                for (ulong i = 0; i < patterns; i++)
                {
                    var blockers = Bmi2.X64.ParallelBitDeposit(i, rookAttackMask);
                    PextAttacks[pextAttackIndex++] = RookMagics[square].GetMoves(blockers);
                }
            }
        }

        for (var i = 0; i < 64; i++)
        for (var j = 0; j < 64; j++)
        {
            CalculateLineBitBoard(i, j);
            CalculateLineBitBoardInclusive(i, j);
                CalculateLineBitBoardStraight(i, j);
                CalculateLineBitBoardDiagonal(i, j); 
                CalculateLineToEdgeBitBoardStraight(i, j);
                CalculateLineToEdgeBitBoardDiagonal(i, j);
        }
    }

    private static T* Allocate<T>(int count) where T : unmanaged
    {
        const nuint alignment = 64;
        var size = sizeof(T) * count;
        var block = NativeMemory.AlignedAlloc((nuint)size, alignment);
        NativeMemory.Clear(block, (nuint)size);

        return (T*)block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetFileIndex(this int square)
    {
        // File is the last 3 bits of the square index
        return square & 7; // Equivalent to square % 8
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetRankIndex(this int square)
    {
        // Rank is obtained by shifting right by 3 bits
        return square >> 3; // Equivalent to square / 8
    }

    private static void CalculateLineBitBoardInclusive(int i, int j)
    {
        // Convert squares i and j to (rank, file) coordinates
        int rank1 = i / 8, file1 = i % 8;
        int rank2 = j / 8, file2 = j % 8;

        // If i and j are the same, return a bitboard with just that square
        if (i == j)
        {
            LineBitBoardsInclusive[i * 64 + j] = 1UL << i;
            return;
        }

        var bitboard = 0UL;

        // Same rank (horizontal line)
        if (rank1 == rank2)
        {
            var minFile = Math.Min(file1, file2);
            var maxFile = Math.Max(file1, file2);
            for (var file = minFile; file <= maxFile; file++) bitboard |= 1UL << (rank1 * 8 + file);
        }
        // Same file (vertical line)
        else if (file1 == file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++) bitboard |= 1UL << (rank * 8 + file1);
        }
        // Same diagonal (positive slope)
        else if (rank1 - file1 == rank2 - file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++)
            {
                var file = rank - (rank1 - file1); // file along the same diagonal
                bitboard |= 1UL << (rank * 8 + file);
            }
        }
        // Same anti-diagonal (negative slope)
        else if (rank1 + file1 == rank2 + file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++)
            {
                var file = rank1 + file1 - rank; // file along the same anti-diagonal
                bitboard |= 1UL << (rank * 8 + file);
            }
        }

        // No longer clearing the i and j squares, keeping them as part of the bitboard
        LineBitBoardsInclusive[i * 64 + j] = bitboard;
    }


    private static void CalculateLineBitBoard(int i, int j)
    {
        // Convert squares i and j to (rank, file) coordinates
        int rank1 = i / 8, file1 = i % 8;
        int rank2 = j / 8, file2 = j % 8;

        // If i and j are the same, return a bitboard with just that square
        if (i == j)
        {
            LineBitBoards[i * 64 + j] = 1UL << i;
            return;
        }

        var bitboard = 0UL;

        // Same rank (horizontal line)
        if (rank1 == rank2)
        {
            var minFile = Math.Min(file1, file2);
            var maxFile = Math.Max(file1, file2);
            for (var file = minFile; file <= maxFile; file++) bitboard |= 1UL << (rank1 * 8 + file);
        }
        // Same file (vertical line)
        else if (file1 == file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++) bitboard |= 1UL << (rank * 8 + file1);
        }
        // Same diagonal (positive slope)
        else if (rank1 - file1 == rank2 - file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++)
            {
                var file = rank - (rank1 - file1); // file along the same diagonal
                bitboard |= 1UL << (rank * 8 + file);
            }
        }
        // Same anti-diagonal (negative slope)
        else if (rank1 + file1 == rank2 + file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++)
            {
                var file = rank1 + file1 - rank; // file along the same anti-diagonal
                bitboard |= 1UL << (rank * 8 + file);
            }
        }

        bitboard &= ~(1UL << i);
        bitboard &= ~(1UL << j);

        LineBitBoards[i * 64 + j] = bitboard;
    }

    private static void CalculateLineBitBoardStraight(int i, int j)
    {
        // Convert squares i and j to (rank, file) coordinates
        int rank1 = i / 8, file1 = i % 8;
        int rank2 = j / 8, file2 = j % 8;

        var bitboard = 0UL;

        // Same rank (horizontal line)
        if (rank1 == rank2)
        {
            var minFile = Math.Min(file1, file2);
            var maxFile = Math.Max(file1, file2);
            for (var file = minFile; file <= maxFile; file++) bitboard |= 1UL << (rank1 * 8 + file);
        }
        // Same file (vertical line)
        else if (file1 == file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++) bitboard |= 1UL << (rank * 8 + file1);
        }

        bitboard &= ~(1UL << i);
        bitboard &= ~(1UL << j);

        LineBitBoardsStraight[i * 64 + j] = bitboard;
    }

    private static void CalculateLineBitBoardDiagonal(int i, int j)
    {
        // Convert squares i and j to (rank, file) coordinates
        int rank1 = i / 8, file1 = i % 8;
        int rank2 = j / 8, file2 = j % 8;

        var bitboard = 0UL;


        // Same diagonal (positive slope)
        if (rank1 - file1 == rank2 - file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++)
            {
                var file = rank - (rank1 - file1); // file along the same diagonal
                bitboard |= 1UL << (rank * 8 + file);
            }
        }
        // Same anti-diagonal (negative slope)
        else if (rank1 + file1 == rank2 + file2)
        {
            var minRank = Math.Min(rank1, rank2);
            var maxRank = Math.Max(rank1, rank2);
            for (var rank = minRank; rank <= maxRank; rank++)
            {
                var file = rank1 + file1 - rank; // file along the same anti-diagonal
                bitboard |= 1UL << (rank * 8 + file);
            }
        }

        bitboard &= ~(1UL << i);
        bitboard &= ~(1UL << j);

        LineBitBoardsDiagonal[i * 64 + j] = bitboard;
    }

    private static void CalculateLineToEdgeBitBoardStraight(int i, int j)
    {
        // Convert squares i and j to (rank, file) coordinates
        int rank1 = i / 8, file1 = i % 8;
        int rank2 = j / 8, file2 = j % 8;

        var bitboard = 0UL;

        // Horizontal ray (same rank)
        if (rank1 == rank2)
        {
            // Determine direction: left or right
            if (file1 < file2)  // Rightward
            {
                for (var file = file1; file < 8; file++)
                    bitboard |= 1UL << (rank1 * 8 + file);
            }
            else // Leftward
            {
                for (var file = file1; file >= 0; file--)
                    bitboard |= 1UL << (rank1 * 8 + file);
            }
        }
        // Vertical ray (same file)
        else if (file1 == file2)
        {
            // Determine direction: up or down
            if (rank1 < rank2)  // Downward
            {
                for (var rank = rank1; rank < 8; rank++)
                    bitboard |= 1UL << (rank * 8 + file1);
            }
            else  // Upward
            {
                for (var rank = rank1; rank >= 0; rank--)
                    bitboard |= 1UL << (rank * 8 + file1);
            }
        }

        // Remove the origin and target squares
        bitboard &= ~(1UL << i);
        bitboard &= ~(1UL << j);

        LineBitBoardsStraightToEdge[i * 64 + j] = bitboard;
    }


    private static void CalculateLineToEdgeBitBoardDiagonal(int i, int j)
    {
        // Convert squares i and j to (rank, file) coordinates
        int rank1 = i / 8, file1 = i % 8;
        int rank2 = j / 8, file2 = j % 8;

        var bitboard = 0UL;

        // Positive slope diagonal (↘ / top-left to bottom-right)
        if (rank1 - file1 == rank2 - file2)
        {
            int startRank = Math.Max(rank1 - Math.Min(rank1, file1), 0);
            int startFile = Math.Max(file1 - Math.Min(rank1, file1), 0);

            while (startRank < 8 && startFile < 8)
            {
                bitboard |= 1UL << (startRank * 8 + startFile);
                startRank++;
                startFile++;
            }
        }
        // Negative slope diagonal (↙ / top-right to bottom-left)
        else if (rank1 + file1 == rank2 + file2)
        {
            int startRank = Math.Min(rank1 + file1, 7);
            int startFile = Math.Max(rank1 + file1 - 7, 0);

            while (startRank >= 0 && startFile < 8)
            {
                bitboard |= 1UL << (startRank * 8 + startFile);
                startRank--;
                startFile++;
            }
        }

        // Remove the origin and target squares
        bitboard &= ~(1UL << i);
        bitboard &= ~(1UL << j);

        LineBitBoardsDiagonalToEdge[i * 64 + j] = bitboard;
    }

    public static ulong RookAttackMask(int square)
    {
        var attackMask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        // Horizontal (rank) moves
        for (var f = file + 1; f < 7; f++) // Start from the next square and end before the edge
            attackMask |= 1UL << (rank * 8 + f);

        for (var f = file - 1; f > 0; f--) // Start from the previous square and end before the edge
            attackMask |= 1UL << (rank * 8 + f);

        // Vertical (file) moves
        for (var r = rank + 1; r < 7; r++) // Start from the next square and end before the edge
            attackMask |= 1UL << (r * 8 + file);

        for (var r = rank - 1; r > 0; r--) // Start from the previous square and end before the edge
            attackMask |= 1UL << (r * 8 + file);

        return attackMask;
    }


    public static ulong BishopAttackMask(int square)
    {
        var attackMask = 0UL;
        var rank = square / 8;
        var file = square % 8;

        // Northeast direction (increasing rank and file)
        for (int r = rank + 1, f = file + 1; r < 7 && f < 7; r++, f++) attackMask |= 1UL << (r * 8 + f);

        // Northwest direction (increasing rank, decreasing file)
        for (int r = rank + 1, f = file - 1; r < 7 && f > 0; r++, f--) attackMask |= 1UL << (r * 8 + f);

        // Southeast direction (decreasing rank, increasing file)
        for (int r = rank - 1, f = file + 1; r > 0 && f < 7; r--, f++) attackMask |= 1UL << (r * 8 + f);

        // Southwest direction (decreasing rank and file)
        for (int r = rank - 1, f = file - 1; r > 0 && f > 0; r--, f--) attackMask |= 1UL << (r * 8 + f);

        return attackMask;
    }

    public static ulong RookAttackMaskAll(int square)
    {
        var rank = square / 8;
        var file = square % 8;
        var mask = 0UL;

        // Horizontal (rank) attack mask
        for (var f = 0; f < 8; f++)
            if (f != file) // Skip the square itself
                mask |= 1UL << (rank * 8 + f);

        // Vertical (file) attack mask
        for (var r = 0; r < 8; r++)
            if (r != rank) // Skip the square itself
                mask |= 1UL << (r * 8 + file);

        return mask;
    }

    public static ulong BishopAttackMaskAll(int square)
    {
        var rank = square / 8;
        var file = square % 8;
        var mask = 0UL;

        // Diagonal (positive slope, rank - file = constant)
        for (var r = 0; r < 8; r++)
        {
            var f = file + (r - rank);
            if (f >= 0 && f < 8 && r != rank) // Valid file and skip the square itself
                mask |= 1UL << (r * 8 + f);
        }

        // Anti-diagonal (negative slope, rank + file = constant)
        for (var r = 0; r < 8; r++)
        {
            var f = file - (r - rank);
            if (f >= 0 && f < 8 && r != rank) // Valid file and skip the square itself
                mask |= 1UL << (r * 8 + f);
        }

        return mask;
    }

    private static ulong[] CreateAllBlockerBitBoards(ulong movementMask)
    {
        var indicesCount = (byte)ulong.PopCount(movementMask);
        var numPatterns = 1 << indicesCount;
        Span<int> indices = stackalloc int[indicesCount];

        var index = 0;
        for (var i = 0; i < 64; i++)
            if (((movementMask >> i) & 1) == 1)
                indices[index++] = i;

        var blockerBitBoards = new ulong[numPatterns];

        for (var patternIndex = 0; patternIndex < numPatterns; patternIndex++)
        for (var bitIndex = 0; bitIndex < indicesCount; bitIndex++)
        {
            var bit = (patternIndex >> bitIndex) & 1;
            blockerBitBoards[patternIndex] |= (ulong)bit << indices[bitIndex];
        }

        return blockerBitBoards;
    }

    private static ulong CalculateRookLegalMoveBitBoard(ulong position, ulong blockers)
    {
        var legalMoves = 0UL;

        // Calculate moves in each direction and stop at the first blocker
        legalMoves |= CalculateUpRayMoves(position, blockers);
        legalMoves |= CalculateDownRayMoves(position, blockers);
        legalMoves |= CalculateLeftRayMoves(position, blockers);
        legalMoves |= CalculateRightRayMoves(position, blockers);

        return legalMoves;
    }

    private static ulong CalculateUpRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0xFF00000000000000UL) == 0) // Ensure not beyond the top row
        {
            currentPos <<= 8;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateDownRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0x00000000000000FFUL) == 0) // Ensure not beyond the bottom row
        {
            currentPos >>= 8;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateLeftRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0x0101010101010101UL) == 0) // Ensure not beyond the left column
        {
            currentPos >>= 1;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateRightRayMoves(ulong rookPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = rookPosition;
        while ((currentPos & 0x8080808080808080UL) == 0) // Ensure not beyond the right column
        {
            currentPos <<= 1;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateBishopLegalMoveBitBoard(ulong position, ulong blockers)
    {
        var legalMoves = 0UL;

        // Calculate moves in each diagonal direction and stop at the first blocker
        legalMoves |= CalculateNortheastRayMoves(position, blockers);
        legalMoves |= CalculateNorthwestRayMoves(position, blockers);
        legalMoves |= CalculateSoutheastRayMoves(position, blockers);
        legalMoves |= CalculateSouthwestRayMoves(position, blockers);

        return legalMoves;
    }

    private static ulong CalculateNortheastRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x8080808080808080UL) == 0 &&
               (currentPos & 0xFF00000000000000UL) == 0) // Ensure not beyond the top row and right column
        {
            currentPos <<= 9;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateNorthwestRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x0101010101010101UL) == 0 &&
               (currentPos & 0xFF00000000000000UL) == 0) // Ensure not beyond the top row and left column
        {
            currentPos <<= 7;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateSoutheastRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x8080808080808080UL) == 0 &&
               (currentPos & 0x00000000000000FFUL) == 0) // Ensure not beyond the bottom row and right column
        {
            currentPos >>= 7;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static ulong CalculateSouthwestRayMoves(ulong bishopPosition, ulong blockers)
    {
        var rayMoves = 0UL;
        var currentPos = bishopPosition;
        while ((currentPos & 0x0101010101010101UL) == 0 &&
               (currentPos & 0x00000000000000FFUL) == 0) // Ensure not beyond the bottom row and left column
        {
            currentPos >>= 9;
            rayMoves |= currentPos;
            if ((currentPos & blockers) != 0) break;
        }

        return rayMoves;
    }

    private static MagicBitBoard GetBishopMagicNumbers(int square, Random rand)
    {
        var movementMask = BishopAttackRays[square];

        // Calculate all possible blocker configurations for movement mask
        var blockers = CreateAllBlockerBitBoards(movementMask);

        // Calculate all possible legal moves for each blocker arrangement
        Span<ulong> legalMoves = stackalloc ulong[blockers.Length];
        var position = 1UL << square;
        for (var j = 0; j < blockers.Length; j++)
            legalMoves[j] = CalculateBishopLegalMoveBitBoard(position, blockers[j]);

        var relevantBits = (byte)ulong.PopCount(movementMask);
        Span<ulong> usedAttacks = stackalloc ulong[1 << relevantBits];
        var indexBits = 64 - relevantBits;
        ulong magic = 0;

        while (true)
        {
            var u1 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u2 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u3 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            magic = u1 & u2 & u3;
            var isMagic = true;
            usedAttacks.Clear();
            for (var i = 0; i < legalMoves.Length; i++)
            {
                var occupancy = blockers[i];
                var index = (int)((occupancy * magic) >> indexBits);
                if (usedAttacks[index] == 0)
                {
                    usedAttacks[index] = legalMoves[i];
                }
                else if (usedAttacks[index] != legalMoves[i])
                {
                    isMagic = false;
                    break;
                }
            }

            if (isMagic) break;
        }

        return new MagicBitBoard(magic, movementMask, indexBits, usedAttacks.ToArray());
    }

    private static MagicBitBoard GetRookMagicNumbers(int square, Random rand)
    {
        var movementMask = RookAttackRays[square];

        // Calculate all possible blocker configurations for movement mask
        var blockers = CreateAllBlockerBitBoards(movementMask);

        // Calculate all possible legal moves for each blocker arrangement
        Span<ulong> legalMoves = stackalloc ulong[blockers.Length];
        var position = 1UL << square;
        for (var j = 0; j < blockers.Length; j++) legalMoves[j] = CalculateRookLegalMoveBitBoard(position, blockers[j]);

        var relevantBits = (byte)ulong.PopCount(movementMask);
        Span<ulong> usedAttacks = stackalloc ulong[1 << relevantBits];
        var indexBits = 64 - relevantBits;
        ulong magic = 0;

        while (true)
        {
            var u1 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u2 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            var u3 = ((ulong)rand.Next() & 0xFFFF) | (((ulong)rand.Next() & 0xFFFF) << 16) |
                     (((ulong)rand.Next() & 0xFFFF) << 32) | (((ulong)rand.Next() & 0xFFFF) << 48);
            magic = u1 & u2 & u3;
            var isMagic = true;
            usedAttacks.Clear();
            for (var i = 0; i < legalMoves.Length; i++)
            {
                var occupancy = blockers[i];
                var index = (int)((occupancy * magic) >> indexBits);
                if (usedAttacks[index] == 0)
                {
                    usedAttacks[index] = legalMoves[i];
                }
                else if (usedAttacks[index] != legalMoves[i])
                {
                    isMagic = false;
                    break;
                }
            }

            if (isMagic) break;
        }

        return new MagicBitBoard(magic, movementMask, indexBits, usedAttacks.ToArray());
    }

#if ARM
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PextRookAttacks(ulong occupation, int square)
    {
        return RookMagics[square].GetMoves(occupation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PextBishopAttacks(ulong occupation, int square)
    {
        return BishopMagics[square].GetMoves(occupation);
    }

#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PextRookAttacks(ulong occupation, int square)
    {
        return *(PextAttacks +
                 *(RookPextOffset + square) + Bmi2.X64.ParallelBitExtract(occupation, *(RookAttackRays + square)));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PextBishopAttacks(ulong occupation, int square)
    {
        return *(PextAttacks +
                 *(BishopPextOffset + square) + Bmi2.X64.ParallelBitExtract(occupation, *(BishopAttackRays + square)));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DetectPinsStraight(int kingPos, ulong enemySlidingPieces, ulong occupancy)
    {
        var pinned = 0ul;

        while (enemySlidingPieces != 0)
        {
            var attackerPos = enemySlidingPieces.PopLSB(); // Get one attacker at a time

            // Calculate ray between king and attacker
            var pinRay = *(LineBitBoardsStraight + kingPos * 64 + attackerPos) & occupancy;

            // Count the number of pieces in the pinRay
            if (ulong.PopCount(pinRay) == 1)
            {
                pinned |= pinRay; // The single blocking piece is pinned
            }
        }
        return pinned;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DetectPinsDiagonal(int kingPos, ulong enemySlidingPieces, ulong occupancy)
    {
        var pinned = 0ul;

        while (enemySlidingPieces != 0)
        {
            var attackerPos = enemySlidingPieces.PopLSB(); // Get one attacker at a time

            // Calculate ray between king and attacker
            var pinRay = *(LineBitBoardsDiagonal + kingPos * 64 + attackerPos) & occupancy;

            // Count the number of pieces in the pinRay
            if (ulong.PopCount(pinRay) == 1)
            {
                pinned |= pinRay; // The single blocking piece is pinned
            }
        }
        return pinned;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetRayToEdgeStraight(int from, int to)
    {
        return *(AttackTables.LineBitBoardsStraightToEdge + from * 64 + to);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetRayToEdgeDiagonal(int from, int to)
    {
        return *(AttackTables.LineBitBoardsDiagonalToEdge + from * 64 + to);
    }
}

public sealed record MagicBitBoard(ulong MagicNumber, ulong MovementMask, int Position, ulong[] Moves)
{
    public ulong GetMoves(ulong blockers)
    {
        return Moves[((MovementMask & blockers) * MagicNumber) >> Position];
    }
}
