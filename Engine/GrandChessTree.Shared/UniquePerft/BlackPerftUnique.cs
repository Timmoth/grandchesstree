using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;
public partial struct Board
{
    public bool CanBlackPawnEnpassant()
    {
        // Black captures en passant when a White pawn double-pushed from rank 1 to rank 3.
        // The White pawn passed over rank 2. So, the Black pawn lands on rank 2.
        ulong targetSquare = 1UL << (2 * 8 + EnPassantFile);  // Landing square (rank 2, given file)
        // The White pawn that moved is on rank 3 (one rank above target)
        ulong captureSquare = targetSquare << 8;       // White pawn’s square (rank 3)

        // Black pawn must be on rank 3 to capture en passant.
        ulong validBlackPawns = Black & Pawn & Constants.RankMasks[3];

        // Black moves downward. To "reverse" a downward diagonal:
        // - For a capture coming from the left (from Black’s perspective, that means the pawn comes from the right), origin = targetSquare << 9.
        // - For a capture coming from the right, origin = targetSquare << 7.
        ulong leftCandidate  = (EnPassantFile != 7) ? (targetSquare << 9) : 0UL; // only if target not in file H
        ulong rightCandidate = (EnPassantFile != 0) ? (targetSquare << 7) : 0UL; // only if target not in file A

        ulong enPassantCandidates = (validBlackPawns & leftCandidate) | (validBlackPawns & rightCandidate);

        while (enPassantCandidates != 0)
        {
            int fromSquare = enPassantCandidates.PopLSB();
            var black = Black ^ (1UL << fromSquare) | targetSquare;
            var white = White & ~captureSquare;

            var occupancy = white | black;
            var slidingAttackers = (AttackTables.PextBishopAttacks(occupancy, BlackKingPos) & (white & (Bishop | Queen))) |
                                   (AttackTables.PextRookAttacks(occupancy, BlackKingPos) & (white & (Rook | Queen)));
            if (slidingAttackers == 0)
            {
                return true; // Found a legal en passant capture.
            }
        }
        return false;
    }


    // Leaf emit for a position where Black is to move. Called from White
    // piece accumulators at depth=1, and from AccumulateBlackMovesUnique at
    // depth=0. AggressiveInlining lets the JIT fold this into the call site
    // when the call site can see we're at the leaf — saving the depth==0
    // branch and method-call dispatch on the hot path.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void EmitLeafBlack()
    {
        var hash = Hash;
        bool epAvailable = false;
        if (EnPassantFile < 8)
        {
            if (CanBlackPawnEnpassant()) epAvailable = true;
            else hash ^= Zobrist.EnPassantFile[EnPassantFile];
        }

        int _outBucket = (int)(hash >> PerftUnique.BucketShift);
        if (_outBucket < PerftUnique.BucketLo || _outBucket >= PerftUnique.BucketHi)
            return;

        var posSink = PerftUnique.PositionSpillSink;
        if (posSink == null)
        {
            EmitLeafBlackCold(hash, epAvailable);
            return;
        }

        var mtSuppress = PerftUnique.UniquePositions128;
        if (mtSuppress != null)
        {
            var h2s = SecondaryHash.Compute(ref this, whiteToMove: false, epAvailable);
            if (mtSuppress.TryAdd(hash, h2s) == LockFreeHashSet128.AddResult.AlreadyPresent)
                return;
        }
        Board snap = this;
        if (!epAvailable && snap.EnPassantFile < 8) snap.EnPassantFile = 8;
        Span<byte> buf = stackalloc byte[BucketPositionSpillSink.RecordSize];
        BoardStateSerialization.WriteToSpan(ref snap, buf, whiteToMove: false);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(26), PerftUnique.NextDfsCounter());
        posSink.Record(buf, hash);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void EmitLeafBlackCold(ulong hash, bool epAvailable)
    {
        // Sharding is only set by the legacy `unique_spill_mt128` command, which
        // also uses SpillSink (not PositionSpillSink), so the check belongs here.
        if (PerftUnique.ShardCount > 1 && (hash >> PerftUnique.ShardShift) != (ulong)PerftUnique.ShardId)
            return;

        var sink = PerftUnique.SpillSink;
        if (sink != null)
        {
            var h2 = SecondaryHash.Compute(ref this, whiteToMove: false, epAvailable);
            var mt128 = PerftUnique.UniquePositions128;
            if (mt128 != null)
            {
                if (mt128.TryAdd(hash, h2) != LockFreeHashSet128.AddResult.AlreadyPresent)
                    sink.Record(hash, h2);
            }
            else if (PerftUnique.UniquePositions.Add(hash))
            {
                sink.Record(hash, h2);
            }
        }
        else
        {
            PerftUnique.UniquePositions.Add(hash);
        }
    }

    public unsafe void AccumulateBlackMovesUnique(int depth)
    {

        if (depth == 0)
        {
            EmitLeafBlack();
            return;
        }


        // Shared TT lookup with XOR-guard. Skipped when HashTable == null.
        ulong key = Hash ^ (White | Black);
        PerftUniqueHashEntry* ptr = null;
        if (PerftUnique.HashTable != null)
        {
            ptr = PerftUnique.HashTable + (Hash & PerftUnique.HashTableMask);
            ulong cachedData = ptr->Data;
            ulong cachedGuard = ptr->HashXorData;
            if ((cachedGuard ^ cachedData) == key && (uint)(cachedData & 0xF) == (uint)depth)
            {
                return;
            }
        }

        var checkers = WhiteCheckers();
        var numCheckers = (byte)ulong.PopCount(checkers);

        AccumulateBlackKingMovesUnique( depth, numCheckers > 0);

        if (numCheckers > 1)
        {
            // Only a king move can evade double check
            if (ptr != null)
            {
                ulong newDataEarly = (uint)depth;
                ptr->Data = newDataEarly;
                ptr->HashXorData = key ^ newDataEarly;
            }
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

        if (ptr != null)
        {
            ulong newData = (uint)depth;
            ptr->Data = newData;
            ptr->HashXorData = key ^ newData;
        }
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
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_BishopPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_RookPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_Capture_QueenPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
            }

            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_KnightPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_BishopPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_RookPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                newBoard = Unsafe.As<Board, Board>(ref this);

                newBoard.BlackPawn_QueenPromotion(index, toSquare);
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
            }

            if (EnPassantFile != 8 && rankIndex.IsBlackEnPassantRankIndex() &&
                Math.Abs(index.GetFileIndex() - EnPassantFile) == 1)
            {
                newBoard = Unsafe.As<Board, Board>(ref this);

                toSquare = Constants.BlackEnpassantOffset + EnPassantFile;

                newBoard.BlackPawn_Enpassant(index, toSquare);
                if (!newBoard.IsAttackedByWhiteSliders(newBoard.BlackKingPos))
                {
                    if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
                }
            }

            // Generate valid push moves for a Black pawn from "index"
            validMoves = AttackTables.BlackPawnPushTable[index] & MoveMask & ~(White | Black) & pushPinMask;
            while (validMoves != 0)
            {
                toSquare = validMoves.PopLSB();
                
                // For Black, a double push is available if the pawn is on its starting rank (7th rank, i.e. rank index 6)
                // and the destination is two squares ahead (rank index 4) *and* the intermediate square is empty.
                // The intermediate square is computed as the average of source and destination.
                int intermediateSquare = (index + toSquare) / 2;
                if (rankIndex.IsSeventhRank() && toSquare.GetRankIndex() == 4)
                {
                    if (((White | Black) & (1UL << intermediateSquare)) != 0)
                    {
                        continue;
                    }
                    newBoard = Unsafe.As<Board, Board>(ref this);
                    newBoard.BlackPawn_DoublePush(index, toSquare);
                }
                else
                {
                    newBoard = Unsafe.As<Board, Board>(ref this);
                    newBoard.BlackPawn_Move(index, toSquare);
                }
                
                if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKnight_Move(index, toSquare);
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackBishop_Move(index, toSquare);
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackRook_Move(index, toSquare);
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackQueen_Move(index, toSquare);
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
        }

        var emptyMoves = potentialMoves & ~(White | Black);
        while (emptyMoves != 0)
        {
            newBoard = Unsafe.As<Board, Board>(ref this);

            toSquare = emptyMoves.PopLSB();

            newBoard.BlackKing_Move(BlackKingPos, toSquare);
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
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
            if (depth == 1) newBoard.EmitLeafWhite(); else newBoard.AccumulateWhiteMovesUnique(depth - 1);
        }
        return;

    }
}