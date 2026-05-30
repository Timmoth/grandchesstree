using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;


// Shared lock-free unique-perft TT entry. Same XOR-guard pattern as the bulk TT
// (see BulkPerft/PerftBulk.cs). Unique-perft TT only carries (key, depth) — no
// node count, because hits just short-circuit subtree recursion.
public struct PerftUniqueHashEntry
{
    public ulong HashXorData;
    public ulong Data;        // depth in low 4 bits; high 60 bits unused (kept random for XOR)
}
public static unsafe class PerftUnique
{
    #region HashTable
    public static uint HashTableMask;
    public static int HashTableSize;

    // Shared across all threads. XOR-guard makes concurrent access torn-read safe.
    public static PerftUniqueHashEntry* HashTable;

    static PerftUnique() { }

    private static uint CalculateHashTableEntries(int sizeInMb)
    {
        var transpositionCount = (ulong)sizeInMb * 1024ul * 1024ul / (ulong)sizeof(PerftUniqueHashEntry);
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

    private static readonly object _allocLock = new();

    public static void AllocateHashTable(int sizeInMb = 512)
    {
        // mbHash <= 0: drop the TT entirely.
        if (sizeInMb <= 0)
        {
            lock (_allocLock)
            {
                if (HashTable != null) { NativeMemory.AlignedFree(HashTable); HashTable = null; }
                HashTableSize = 0;
                HashTableMask = 0;
            }
            return;
        }

        var newHashTableSize = (int)CalculateHashTableEntries(sizeInMb);

        // Idempotent across threads: callers in worker loops are safe. We do NOT
        // clear an existing table of the same size — perft TT entries are valid
        // across consecutive runs of the same engine, since (position, depth) →
        // node count is a function of the position alone. This lets sequential
        // shard runs benefit from a warm TT.
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
            var bytes = ((nuint)sizeof(PerftUniqueHashEntry) * (nuint)HashTableSize);
            var block = NativeMemory.AlignedAlloc(bytes, alignment);
            NativeMemory.Clear(block, bytes);
            HashTable = (PerftUniqueHashEntry*)block;
        }
    }

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
            Unsafe.InitBlock(HashTable, 0, (uint)(sizeof(PerftUniqueHashEntry) * (HashTableMask + 1)));
        }
    }

    #endregion

    public static LockFreeHashSet UniquePositions = new LockFreeHashSet(1 << 30);
    public static LockFreeHashSet128? UniquePositions128;

    public static void ReallocateMemTable(int log2Capacity)
    {
        UniquePositions.Dispose();
        UniquePositions = new LockFreeHashSet(1L << log2Capacity);
    }

    public static void ReallocateMemTable128(int log2Capacity)
    {
        UniquePositions128?.Dispose();
        UniquePositions128 = new LockFreeHashSet128(1L << log2Capacity);
    }

    public static void FreeMemTable128()
    {
        UniquePositions128?.Dispose();
        UniquePositions128 = null;
    }

    // When non-null, depth=0 leaf records are written to disk-backed bucket files
    // instead of (or in addition to) the in-RAM UniquePositions set.
    public static BucketSpillSink? SpillSink;

    // BFS wave-expand mode. When non-null, depth=0 leaf writes the canonical
    // 26-byte position (not just the hash) to disk. Used for external-memory
    // BFS where each wave is a full set of positions on disk, and the next
    // wave is produced by 1-ply expansion + external sort+dedup.
    public static BucketPositionSpillSink? PositionSpillSink;

    // Shard filter: process only positions whose top bits of h1 match ShardId.
    // ShardCount == 1 disables filtering.
    public static int ShardCount = 1;
    public static int ShardId = 0;
    public static int ShardShift = 0;

    public static void SetShard(int shardCount, int shardId)
    {
        if (shardCount < 1 || (shardCount & (shardCount - 1)) != 0)
            throw new ArgumentException("shardCount must be a power of two");
        if (shardId < 0 || shardId >= shardCount)
            throw new ArgumentException("shardId out of range");
        ShardCount = shardCount;
        ShardId = shardId;
        ShardShift = shardCount == 1 ? 0 : (64 - System.Numerics.BitOperations.Log2((uint)shardCount));
    }

    // Output bucket-range filter for multi-pass wave_expand. The leaf emit drops
    // records whose (hash >> BucketShift) falls outside [BucketLo, BucketHi).
    // Defaults [0, int.MaxValue) cover all buckets — single-pass behaviour.
    // Each pass of a multi-pass run sets a slice [K*p/N, K*(p+1)/N) and writes
    // only its share of output buckets, leaving prior passes' files untouched.
    public static int BucketShift = 0;
    public static int BucketLo = 0;
    public static int BucketHi = int.MaxValue;

    public static void SetBucketRange(int numBuckets, int bucketLo, int bucketHi)
    {
        if (numBuckets < 1 || (numBuckets & (numBuckets - 1)) != 0)
            throw new ArgumentException("numBuckets must be a power of two");
        if (bucketLo < 0 || bucketHi > numBuckets || bucketLo >= bucketHi)
            throw new ArgumentException($"bucket range [{bucketLo}, {bucketHi}) out of [0, {numBuckets})");
        BucketShift = 64 - System.Numerics.BitOperations.Log2((uint)numBuckets);
        BucketLo = bucketLo;
        BucketHi = bucketHi;
    }

    public static void ClearBucketRange()
    {
        BucketShift = 0;
        BucketLo = 0;
        BucketHi = int.MaxValue;
    }

    // DFS emission counter for the sortKey trailing every spill record.
    // Per-thread (ThreadStatic): each worker maintains its own monotonic
    // counter. Counters across threads aren't globally ordered — Phase 3's
    // global merge tiebreaks dedups on min sortKey, so cross-thread
    // interleaving is well-defined per run.
    //
    // ThreadStatic default-inits to 0 per thread on first access; the first
    // returned value is 1 (post-increment in NextDfsCounter). Values are
    // monotonic per thread for the lifetime of that thread, across multiple
    // wave_expand invocations within one process.
    [ThreadStatic] private static ulong _dfsCounter;

    public static ulong NextDfsCounter() => ++_dfsCounter;

    public static void PerftRootUnique(ref Board board, int depth, bool whiteToMove)
    {
        if (depth == 0)
        {
            // perft(0) = 1
            return;
        }

        if (whiteToMove)
        {
            var checkers = board.BlackCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateWhiteKingMovesUnique(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return;
            }

            board.MoveMask = numCheckers == 0 ? 0xFFFFFFFFFFFFFFFF: checkers | *(AttackTables.LineBitBoardsInclusive + board.WhiteKingPos * 64 + BitOperations.TrailingZeroCount(checkers));
            var pinMask = board.WhiteKingPinnedRay();

            var positions = board.White & board.Pawn & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhitePawnMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions =board. White &board. Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhitePawnMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Knight & ~pinMask;
            while (positions != 0)
            {
                board.AccumulateWhiteKnightMovesUnique(depth, positions.PopLSB());
            }

            positions = board.White & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index));
            }
        
            positions = board.White & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteBishopMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookMovesUnique(depth, index,  AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }
            positions = board.White & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteRookMovesUnique(depth, index,  0xFFFFFFFFFFFFFFFF);
            }

            positions = board.White & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(board.WhiteKingPos, index) | AttackTables.GetRayToEdgeStraight(board.WhiteKingPos, index));
            }     
        
            positions = board.White & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateWhiteQueenMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }
        }
        else
        {
            var checkers = board.WhiteCheckers();
            var numCheckers = (byte)ulong.PopCount(checkers);

            board.AccumulateBlackKingMovesUnique(depth, numCheckers > 0);

            if (numCheckers > 1)
            {
                // Only a king move can evade double check
                return;
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
                board.AccumulateBlackPawnMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index), AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Pawn & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackPawnMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Knight & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackKnightMovesUnique(depth, index);
            }

            positions = board.Black & board.Bishop & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopMovesUnique(depth, index, AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Bishop & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackBishopMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Rook & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookMovesUnique(depth, index, AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Rook & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackRookMovesUnique(depth, index, 0xFFFFFFFFFFFFFFFF);
            }

            positions = board.Black & board.Queen & pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenMovesUnique(depth, index,  AttackTables.GetRayToEdgeDiagonal(board.BlackKingPos, index) | AttackTables.GetRayToEdgeStraight(board.BlackKingPos, index));
            }
        
            positions = board.Black & board.Queen & ~pinMask;
            while (positions != 0)
            {
                var index = positions.PopLSB();
                board.AccumulateBlackQueenMovesUnique(depth, index,  0xFFFFFFFFFFFFFFFF);
            }
        }

        return;
    }

  
}