using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

// Shared lock-free perft transposition table entry.
// Stockfish-style XOR trick: HashXorData = (FullHash ^ Data) is the validity guard.
// Data packs depth (low 4 bits) and node count (high 60 bits). Self-consistent
// reads under concurrent writes because torn (Data, HashXorData) pairs almost
// never XOR back to a queried key — false-miss rate ~2^-64, never a wrong hit.
public struct PerftBulkHashEntry
{
    public ulong HashXorData;
    public ulong Data;        // (nodes << 4) | depth
}
public static unsafe class PerftBulk
{
    #region HashTable
    public static uint HashTableMask;
    public static int HashTableSize;
    public static ulong AllocatedMb = 0;

    // Shared across all threads (not [ThreadStatic]). The XOR-guard protocol on
    // each entry makes concurrent access safe without locking.
    public static PerftBulkHashEntry* HashTable;

    static PerftBulk() { }

    private static uint CalculateHashTableEntries(int sizeInMb)
    {
        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(PerftBulkHashEntry);
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

    public static void AllocateHashTable(int sizeInMb = 512)
    {
        // mbHash == 0 (or negative) means "no TT" — pure move-gen mode. We
        // release any existing table and leave HashTable == null. The hot
        // path in AccumulateWhite/BlackMovesBulk null-checks before access.
        if (sizeInMb <= 0)
        {
            lock (_allocLock)
            {
                if (HashTable != null)
                {
                    NativeMemory.AlignedFree(HashTable);
                    HashTable = null;
                }
                HashTableSize = 0;
                HashTableMask = 0;
                AllocatedMb = 0;
            }
            return;
        }

        var newHashTableSize = (int)CalculateHashTableEntries(sizeInMb);
        AllocatedMb = (ulong)newHashTableSize * (ulong)sizeof(PerftBulkHashEntry) / 1024ul / 1024ul;

        // Allocation is idempotent across threads: first caller wins; same-size
        // re-allocations are no-ops (entries are reusable since perft TT keys
        // are position-deterministic). Threaded callers can fire this freely.
        lock (_allocLock)
        {
            if (HashTable != null && HashTableSize == newHashTableSize)
            {
                return;
            }
            if (HashTable != null)
            {
                FreeHashTable();
            }
            HashTableSize = newHashTableSize;
            HashTableMask = (uint)HashTableSize - 1;

            const nuint alignment = 64;
            var bytes = ((nuint)sizeof(PerftBulkHashEntry) * (nuint)HashTableSize);
            var block = NativeMemory.AlignedAlloc(bytes, alignment);
            NativeMemory.Clear(block, bytes);
            HashTable = (PerftBulkHashEntry*)block;
        }
    }
    private static readonly object _allocLock = new();

    public static void FreeHashTable()
    {
        lock (_allocLock)
        {
            if (HashTable != null)
            {
                NativeMemory.AlignedFree(HashTable);
                HashTable = null;
            }
        }
    }

    public static void ClearTable()
    {
        if (HashTable != null)
        {
            Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(PerftBulkHashEntry) * (HashTableMask + 1)));
        }
    }

    /// <summary>
    /// Issue a non-blocking prefetch of the TT slot for `hash`. Designed to be
    /// called right after a move is applied and before the recursive descent —
    /// the function-call + epilogue overhead absorbs the DRAM-to-L1 latency
    /// (~100ns / ~300 cycles on Zen 4) so the TT read at the bottom hits L1.
    /// No-op on platforms without SSE (e.g. ARM) and when the TT is disabled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PrefetchSlot(ulong hash)
    {
        if (Sse.IsSupported && HashTable != null)
        {
            Sse.Prefetch0((byte*)(HashTable + (hash & HashTableMask)));
        }
    }

    #endregion
    
    public static ulong PerftRootBulk(ref Board board, int depth, bool whiteToMove)
    {
        if (depth == 0)
        {
            // perft(0) = 1
            return 1;
        }
        if (depth == 1)
        {
            if (whiteToMove)
            {
                return board.AccumulateWhiteMovesBulkCount();
            }
            else
            {
                return board.AccumulateBlackMovesBulkCount();
            }
        }

        ulong nodes = 0;

        if (whiteToMove)
        {


            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            nodes += board.AccumulateWhiteKingMovesBulk(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return nodes;
            }

            board.MoveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF: checkers | *(AttackTables.LineBitBoardsInclusive + board.WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            var pinMask = board.WhiteKingPinnedRay();

            var positions = board.White & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhitePawnMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhitePawnMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                nodes += board.AccumulateWhiteKnightMovesBulk(depth, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteBishopMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteBishopMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteRookMovesBulk(depth, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteRookMovesBulk(depth, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteQueenMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateWhiteQueenMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            nodes += board.AccumulateBlackKingMovesBulk(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return nodes;
            }
            
            board.MoveMask = 0xFFFFFFFFFFFFFFFF;
            if (numCheckers == 1)
            {
                board.MoveMask = checkers | *(AttackTables.LineBitBoardsInclusive + board.BlackKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            }
            var pinMask = board.BlackKingPinnedRay();

            var positions = board.Black & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackPawnMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackPawnMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackKnightMovesBulk(depth, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackBishopMovesBulk(depth, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackBishopMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackRookMovesBulk(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackRookMovesBulk(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackQueenMovesBulk(depth, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                nodes += board.AccumulateBlackQueenMovesBulk(depth, index,  0xFFFFFFFFFFFFFFFF);
            }
        }

        return nodes;
    }

  
}