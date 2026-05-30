namespace MoveGen.App;

public sealed partial class Position
{
    public void MakeMove(Move move)
    {
        int from = move.From;
        int to   = move.To;
        Piece moving = Squares[from];

        // Capture the side-to-move up front; all the piece-bitboard work happens
        // with this value, and we only flip WhiteToMove at the very end.
        bool whiteMoving = WhiteToMove;

        Piece captured = move.IsEnPassant
            ? (whiteMoving ? Piece.BlackPawn : Piece.WhitePawn)
            : Squares[to];

        _undoStack[_undoTop++] = new UndoInfo
        {
            Castling      = Castling,
            EpSquare      = EpSquare,
            HalfmoveClock = HalfmoveClock,
            Captured      = captured,
        };

        Castling &= CastlingRightsMask[from] & CastlingRightsMask[to];

        if (moving == Piece.WhitePawn || moving == Piece.BlackPawn || move.IsCapture)
            HalfmoveClock = 0;
        else
            HalfmoveClock++;

        // 1. Move the moving piece.
        ulong fromTo = (1UL << from) | (1UL << to);
        Pieces[(int)moving] ^= fromTo;
        Squares[from] = Piece.None;
        Squares[to]   = moving;

        // 2. Capture (default case — special-cased below for ep / promo-capture).
        if (move.IsCapture && !move.IsEnPassant)
        {
            Pieces[(int)captured] ^= 1UL << to;
        }

        // 3. Special moves — done *before* the side flip so `whiteMoving` is the
        //    obvious "side that just moved" throughout.
        switch (move.Flag)
        {
            case MoveFlag.EnPassant:
            {
                int capSq = whiteMoving ? to - 8 : to + 8;
                Pieces[(int)captured] ^= 1UL << capSq;
                Squares[capSq] = Piece.None;
                break;
            }
            case MoveFlag.KingsideCastle:
                if (whiteMoving) MoveRookForCastle(7, 5);    // h1 → f1
                else             MoveRookForCastle(63, 61);  // h8 → f8
                break;
            case MoveFlag.QueensideCastle:
                if (whiteMoving) MoveRookForCastle(0, 3);    // a1 → d1
                else             MoveRookForCastle(56, 59);  // a8 → d8
                break;
            default:
                if (move.IsPromotion)
                {
                    Piece pawn  = whiteMoving ? Piece.WhitePawn : Piece.BlackPawn;
                    int colorOffset = whiteMoving ? 0 : 6;
                    Piece promo = (Piece)(colorOffset + 1 + move.PromotionPieceIndex);
                    Pieces[(int)pawn]  ^= 1UL << to;
                    Pieces[(int)promo] ^= 1UL << to;
                    Squares[to] = promo;
                }
                break;
        }

        // 4. En-passant square for the next position.
        EpSquare = move.Flag == MoveFlag.DoublePawnPush
            ? (whiteMoving ? to - 8 : to + 8)
            : -1;

        // 5. Side / fullmove. The flip is the last thing we touch.
        if (!whiteMoving) FullmoveNumber++;
        WhiteToMove = !whiteMoving;

        RebuildOccupancy();
    }

    public void UnmakeMove(Move move)
    {
        int from = move.From;
        int to   = move.To;

        // Flip side back first so `whiteMoving` is the side that originally made
        // this move. The rest mirrors MakeMove in reverse.
        WhiteToMove = !WhiteToMove;
        if (!WhiteToMove) FullmoveNumber--;
        bool whiteMoving = WhiteToMove;

        UndoInfo u = _undoStack[--_undoTop];
        Castling      = u.Castling;
        EpSquare      = u.EpSquare;
        HalfmoveClock = u.HalfmoveClock;

        // If we promoted, swap the promoted piece back to a pawn first so we can
        // move it back as a pawn.
        Piece nowOnTo = Squares[to];
        if (move.IsPromotion)
        {
            Piece pawn = whiteMoving ? Piece.WhitePawn : Piece.BlackPawn;
            Pieces[(int)nowOnTo] ^= 1UL << to;
            Pieces[(int)pawn]    ^= 1UL << to;
            nowOnTo = pawn;
        }

        // Move the piece back.
        ulong fromTo = (1UL << from) | (1UL << to);
        Pieces[(int)nowOnTo] ^= fromTo;
        Squares[to]   = Piece.None;
        Squares[from] = nowOnTo;

        // Restore the captured piece.
        if (u.Captured != Piece.None)
        {
            int capSq = move.IsEnPassant
                ? (whiteMoving ? to - 8 : to + 8)
                : to;
            Pieces[(int)u.Captured] ^= 1UL << capSq;
            Squares[capSq] = u.Captured;
        }

        // Undo castling rook move.
        if (move.Flag == MoveFlag.KingsideCastle)
        {
            if (whiteMoving) MoveRookForCastle(5, 7);
            else             MoveRookForCastle(61, 63);
        }
        else if (move.Flag == MoveFlag.QueensideCastle)
        {
            if (whiteMoving) MoveRookForCastle(3, 0);
            else             MoveRookForCastle(59, 56);
        }

        RebuildOccupancy();
    }

    void MoveRookForCastle(int rookFrom, int rookTo)
    {
        Piece rook = Squares[rookFrom];
        ulong fromTo = (1UL << rookFrom) | (1UL << rookTo);
        Pieces[(int)rook] ^= fromTo;
        Squares[rookFrom] = Piece.None;
        Squares[rookTo]   = rook;
    }

    void RebuildOccupancy()
    {
        ulong w = 0, b = 0;
        for (int p = (int)Piece.WhitePawn; p <= (int)Piece.WhiteKing; p++) w |= Pieces[p];
        for (int p = (int)Piece.BlackPawn; p <= (int)Piece.BlackKing; p++) b |= Pieces[p];
        WhiteOccupied = w;
        BlackOccupied = b;
        AllOccupied   = w | b;
    }

    /// <summary>
    /// Asserts internal consistency: piece bitboards, the mailbox, and aggregate
    /// occupancy must all agree. Cheap enough to call in test mode after every
    /// MakeMove / UnmakeMove; an absolute lifesaver when chasing perft mismatches.
    /// </summary>
    public void Validate()
    {
        ulong w = 0, b = 0;
        for (int p = 0; p < 12; p++)
        {
            ulong pieces = Pieces[p];
            // No piece occupies the same square twice — the bitboards are disjoint pairwise.
            for (int q = p + 1; q < 12; q++)
                if ((pieces & Pieces[q]) != 0)
                    throw new InvalidOperationException(
                        $"Piece bitboards {(Piece)p} and {(Piece)q} overlap");

            // Every set bit in a piece bitboard must show that piece in the mailbox.
            ulong bb = pieces;
            while (bb != 0)
            {
                int sq = System.Numerics.BitOperations.TrailingZeroCount(bb);
                if ((int)Squares[sq] != p)
                    throw new InvalidOperationException(
                        $"Bitboard says {(Piece)p} on {sq} but mailbox says {Squares[sq]}");
                bb &= bb - 1;
            }

            if (p < 6) w |= pieces; else b |= pieces;
        }

        // Mailbox vs bitboards: every non-empty square must be set in some piece bitboard.
        for (int sq = 0; sq < 64; sq++)
        {
            Piece m = Squares[sq];
            if (m == Piece.None)
            {
                if (((w | b) & (1UL << sq)) != 0)
                    throw new InvalidOperationException($"Mailbox says empty on {sq} but bitboards say occupied");
            }
            else
            {
                if ((Pieces[(int)m] & (1UL << sq)) == 0)
                    throw new InvalidOperationException($"Mailbox has {m} on {sq} but its bitboard doesn't");
            }
        }

        // Aggregate occupancy fields must match the recomputed values.
        if (WhiteOccupied != w) throw new InvalidOperationException("WhiteOccupied out of sync");
        if (BlackOccupied != b) throw new InvalidOperationException("BlackOccupied out of sync");
        if (AllOccupied != (w | b)) throw new InvalidOperationException("AllOccupied out of sync");
    }
}
