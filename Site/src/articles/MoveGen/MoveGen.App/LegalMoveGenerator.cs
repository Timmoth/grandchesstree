using System.Numerics;

namespace MoveGen.App;

public static class LegalMoveGenerator
{
    public static int Generate(Position pos, Span<Move> dest)
    {
        int us       = pos.WhiteToMove ? 0 : 1;
        int them     = 1 - us;
        int kingSq   = BitOperations.TrailingZeroCount(
            pos.Pieces[(int)(us == 0 ? Piece.WhiteKing : Piece.BlackKing)]);

        ulong kingDanger = Legality.ComputeKingDanger(pos, kingSq, them);
        ulong checkers   = Legality.ComputeCheckers  (pos, kingSq, them);

        Span<ulong> pinLines = stackalloc ulong[64];
        Legality.ComputePins(pos, kingSq, them, out ulong pinned, pinLines);

        int n = 0;

        n = GenKingMoves(pos, dest, n, kingSq, kingDanger);

        int numCheckers = BitOperations.PopCount(checkers);
        if (numCheckers >= 2)
            return n;

        ulong checkMask = ulong.MaxValue;
        if (numCheckers == 1)
        {
            int checkerSq = BitOperations.TrailingZeroCount(checkers);
            checkMask = checkers | Legality.SquaresBetween[kingSq, checkerSq];
        }

        n = GenPawnsLegal  (pos, dest, n, us, kingSq, checkers, checkMask, pinned, pinLines);
        n = GenKnightsLegal(pos, dest, n, us, checkMask, pinned);
        n = GenSlidersLegal(pos, dest, n, us, checkMask, pinned, pinLines);

        if (numCheckers == 0)
            n = GenCastlingLegal(pos, dest, n, kingSq, kingDanger);

        return n;
    }

    static int GenKingMoves(Position pos, Span<Move> dest, int n, int kingSq, ulong kingDanger)
    {
        bool white = pos.WhiteToMove;
        ulong ours  = white ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong enemy = white ? pos.BlackOccupied : pos.WhiteOccupied;
        ulong targets = Attacks.King[kingSq] & ~ours & ~kingDanger;
        return EmitTargets(dest, n, kingSq, targets, enemy);
    }

    static int GenKnightsLegal(Position pos, Span<Move> dest, int n,
                               int us, ulong checkMask, ulong pinned)
    {
        ulong ourPieces = us == 0 ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong enemy     = us == 0 ? pos.BlackOccupied : pos.WhiteOccupied;

        ulong knights = pos.Pieces[(int)(us == 0 ? Piece.WhiteKnight : Piece.BlackKnight)] & ~pinned;
        while (knights != 0)
        {
            int from = BitOperations.TrailingZeroCount(knights);
            ulong attacks = Attacks.Knight[from] & ~ourPieces & checkMask;
            n = EmitTargets(dest, n, from, attacks, enemy);
            knights &= knights - 1;
        }
        return n;
    }

    static int GenSlidersLegal(Position pos, Span<Move> dest, int n,
                               int us, ulong checkMask, ulong pinned, Span<ulong> pinLines)
    {
        ulong ourPieces = us == 0 ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong enemy     = us == 0 ? pos.BlackOccupied : pos.WhiteOccupied;
        ulong occ = pos.AllOccupied;

        Piece bishop = us == 0 ? Piece.WhiteBishop : Piece.BlackBishop;
        Piece rook   = us == 0 ? Piece.WhiteRook   : Piece.BlackRook;
        Piece queen  = us == 0 ? Piece.WhiteQueen  : Piece.BlackQueen;

        n = EmitSlider(dest, n, pos.Pieces[(int)bishop], isBishop: true,  isRook: false,
                       ourPieces, enemy, occ, checkMask, pinned, pinLines);
        n = EmitSlider(dest, n, pos.Pieces[(int)rook],   isBishop: false, isRook: true,
                       ourPieces, enemy, occ, checkMask, pinned, pinLines);
        n = EmitSlider(dest, n, pos.Pieces[(int)queen],  isBishop: true,  isRook: true,
                       ourPieces, enemy, occ, checkMask, pinned, pinLines);
        return n;
    }

    static int EmitSlider(Span<Move> dest, int n, ulong pieces, bool isBishop, bool isRook,
                          ulong ourPieces, ulong enemy, ulong occ,
                          ulong checkMask, ulong pinned, Span<ulong> pinLines)
    {
        while (pieces != 0)
        {
            int from = BitOperations.TrailingZeroCount(pieces);
            ulong attacks = 0;
            if (isBishop) attacks |= Magic.BishopAttacks(from, occ);
            if (isRook)   attacks |= Magic.RookAttacks  (from, occ);
            attacks &= ~ourPieces & checkMask;
            if (((1UL << from) & pinned) != 0)
                attacks &= pinLines[from];
            n = EmitTargets(dest, n, from, attacks, enemy);
            pieces &= pieces - 1;
        }
        return n;
    }

    static int EmitTargets(Span<Move> dest, int n, int from, ulong targets, ulong enemy)
    {
        while (targets != 0)
        {
            int to = BitOperations.TrailingZeroCount(targets);
            MoveFlag flag = ((1UL << to) & enemy) != 0 ? MoveFlag.Capture : MoveFlag.Quiet;
            dest[n++] = new Move(from, to, flag);
            targets &= targets - 1;
        }
        return n;
    }

    static int GenPawnsLegal(Position pos, Span<Move> dest, int n,
                             int us, int kingSq, ulong checkers, ulong checkMask,
                             ulong pinned, Span<ulong> pinLines)
    {
        bool white = us == 0;
        ulong pawns = pos.Pieces[(int)(white ? Piece.WhitePawn : Piece.BlackPawn)];
        ulong empty = ~pos.AllOccupied;
        ulong enemy = white ? pos.BlackOccupied : pos.WhiteOccupied;

        int dPush = white ? -8 : +8;
        int dDouble = 2 * dPush;
        int dCapE = white ? -9 : +7;
        int dCapW = white ? -7 : +9;
        ulong promoRank = white ? Bitboards.Rank8 : Bitboards.Rank1;
        ulong startRank3 = white ? Bitboards.Rank3 : Bitboards.Rank6;

        ulong singles = (white ? Bitboards.ShiftN(pawns) : Bitboards.ShiftS(pawns)) & empty;
        ulong doubles = (white ? Bitboards.ShiftN(singles & startRank3) : Bitboards.ShiftS(singles & startRank3)) & empty;
        ulong capE    = (white ? Bitboards.ShiftNE(pawns) : Bitboards.ShiftSE(pawns)) & enemy;
        ulong capW    = (white ? Bitboards.ShiftNW(pawns) : Bitboards.ShiftSW(pawns)) & enemy;

        singles &= checkMask;
        doubles &= checkMask;
        capE    &= checkMask;
        capW    &= checkMask;

        n = EmitPawnLegal(dest, n, singles & ~promoRank, dPush,   MoveFlag.Quiet,          pinned, pinLines);
        n = EmitPromos   (dest, n, singles &  promoRank, dPush,   capture: false,          pinned, pinLines);
        n = EmitPawnLegal(dest, n, doubles,              dDouble, MoveFlag.DoublePawnPush, pinned, pinLines);
        n = EmitPawnLegal(dest, n, capE & ~promoRank,    dCapE,   MoveFlag.Capture,        pinned, pinLines);
        n = EmitPawnLegal(dest, n, capW & ~promoRank,    dCapW,   MoveFlag.Capture,        pinned, pinLines);
        n = EmitPromos   (dest, n, capE &  promoRank,    dCapE,   capture: true,           pinned, pinLines);
        n = EmitPromos   (dest, n, capW &  promoRank,    dCapW,   capture: true,           pinned, pinLines);

        if (pos.EpSquare >= 0)
            n = GenEnPassant(pos, dest, n, us, kingSq, checkers);

        return n;
    }

    static int EmitPawnLegal(Span<Move> dest, int n, ulong targets, int deltaFrom,
                             MoveFlag flag, ulong pinned, Span<ulong> pinLines)
    {
        while (targets != 0)
        {
            int to = BitOperations.TrailingZeroCount(targets);
            int from = to + deltaFrom;
            if (((1UL << from) & pinned) == 0 || ((1UL << to) & pinLines[from]) != 0)
                dest[n++] = new Move(from, to, flag);
            targets &= targets - 1;
        }
        return n;
    }

    static int EmitPromos(Span<Move> dest, int n, ulong targets, int deltaFrom,
                          bool capture, ulong pinned, Span<ulong> pinLines)
    {
        int baseFlag = capture ? (int)MoveFlag.PromoCaptureKnight : (int)MoveFlag.PromoteKnight;
        while (targets != 0)
        {
            int to = BitOperations.TrailingZeroCount(targets);
            int from = to + deltaFrom;
            if (((1UL << from) & pinned) == 0 || ((1UL << to) & pinLines[from]) != 0)
                for (int i = 0; i < 4; i++)
                    dest[n++] = new Move(from, to, (MoveFlag)(baseFlag + i));
            targets &= targets - 1;
        }
        return n;
    }

    static int GenEnPassant(Position pos, Span<Move> dest, int n, int us, int kingSq, ulong checkers)
    {
        bool white = us == 0;
        int ep = pos.EpSquare;
        ulong epBB = 1UL << ep;

        ulong pawns = pos.Pieces[(int)(white ? Piece.WhitePawn : Piece.BlackPawn)];
        ulong capE = (white ? Bitboards.ShiftNE(pawns) : Bitboards.ShiftSE(pawns)) & epBB;
        ulong capW = (white ? Bitboards.ShiftNW(pawns) : Bitboards.ShiftSW(pawns)) & epBB;

        int dCapE = white ? -9 : +7;
        int dCapW = white ? -7 : +9;

        if (capE != 0) n = TryEpMove(pos, dest, n, ep + dCapE, ep, us, kingSq, checkers);
        if (capW != 0) n = TryEpMove(pos, dest, n, ep + dCapW, ep, us, kingSq, checkers);
        return n;
    }

    static int TryEpMove(Position pos, Span<Move> dest, int n, int from, int to,
                         int us, int kingSq, ulong checkers)
    {
        bool white = us == 0;
        int capturedPawnSq = white ? to - 8 : to + 8;

        // If we're in single check, the ep capture is only legal if it removes
        // the lone checker (i.e. the captured pawn is the checker).
        // Double check was already filtered out before this function ever runs.
        int numCheckers = BitOperations.PopCount(checkers);
        if (numCheckers == 1 && (checkers & (1UL << capturedPawnSq)) == 0)
            return n;

        // Simulate the capture and check for any newly-uncovered attack on the king.
        // Only sliders can newly attack — non-sliders' attack squares don't depend
        // on occupancy, and the only piece whose attack we lose is the captured pawn.
        ulong simOcc = pos.AllOccupied
                     ^ (1UL << from)
                     ^ (1UL << to)
                     ^ (1UL << capturedPawnSq);

        int them = 1 - us;
        bool enemyWhite = them == 0;
        ulong enemyRQ = pos.Pieces[(int)(enemyWhite ? Piece.WhiteRook  : Piece.BlackRook)]
                      | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen : Piece.BlackQueen)];
        ulong enemyBQ = pos.Pieces[(int)(enemyWhite ? Piece.WhiteBishop : Piece.BlackBishop)]
                      | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen  : Piece.BlackQueen)];

        bool kingAttacked =
               (Magic.RookAttacks  (kingSq, simOcc) & enemyRQ) != 0
            || (Magic.BishopAttacks(kingSq, simOcc) & enemyBQ) != 0;

        if (!kingAttacked)
            dest[n++] = new Move(from, to, MoveFlag.EnPassant);
        return n;
    }

    static int GenCastlingLegal(Position pos, Span<Move> dest, int n,
                                int kingSq, ulong kingDanger)
    {
        bool white = pos.WhiteToMove;
        ulong occ = pos.AllOccupied;

        if (white)
        {
            if ((pos.Castling & CastlingRights.WhiteKingside) != 0
                && (occ & 0x60UL) == 0
                && (kingDanger & 0x70UL) == 0)
                dest[n++] = new Move(4, 6, MoveFlag.KingsideCastle);
            if ((pos.Castling & CastlingRights.WhiteQueenside) != 0
                && (occ & 0x0EUL) == 0
                && (kingDanger & 0x1CUL) == 0)
                dest[n++] = new Move(4, 2, MoveFlag.QueensideCastle);
        }
        else
        {
            if ((pos.Castling & CastlingRights.BlackKingside) != 0
                && (occ & 0x6000000000000000UL) == 0
                && (kingDanger & 0x7000000000000000UL) == 0)
                dest[n++] = new Move(60, 62, MoveFlag.KingsideCastle);
            if ((pos.Castling & CastlingRights.BlackQueenside) != 0
                && (occ & 0x0E00000000000000UL) == 0
                && (kingDanger & 0x1C00000000000000UL) == 0)
                dest[n++] = new Move(60, 58, MoveFlag.QueensideCastle);
        }
        return n;
    }
}
