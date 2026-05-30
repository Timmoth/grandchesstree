using System.Numerics;

namespace MoveGen.App;

public static class MoveGenerator
{
    public static int GeneratePseudoLegal(Position pos, Span<Move> dest)
    {
        int n = 0;
        if (pos.WhiteToMove)
        {
            n = GenWhitePawns   (pos, dest, n);
            n = GenKnights      (pos, dest, n, Piece.WhiteKnight);
            n = GenSliders      (pos, dest, n, Piece.WhiteBishop, isBishop: true);
            n = GenSliders      (pos, dest, n, Piece.WhiteRook,   isBishop: false);
            n = GenSliders      (pos, dest, n, Piece.WhiteQueen,  isBishop: true);
            n = GenSliders      (pos, dest, n, Piece.WhiteQueen,  isBishop: false);
            n = GenKing         (pos, dest, n, Piece.WhiteKing);
            n = GenWhiteCastling(pos, dest, n);
        }
        else
        {
            n = GenBlackPawns   (pos, dest, n);
            n = GenKnights      (pos, dest, n, Piece.BlackKnight);
            n = GenSliders      (pos, dest, n, Piece.BlackBishop, isBishop: true);
            n = GenSliders      (pos, dest, n, Piece.BlackRook,   isBishop: false);
            n = GenSliders      (pos, dest, n, Piece.BlackQueen,  isBishop: true);
            n = GenSliders      (pos, dest, n, Piece.BlackQueen,  isBishop: false);
            n = GenKing         (pos, dest, n, Piece.BlackKing);
            n = GenBlackCastling(pos, dest, n);
        }
        return n;
    }

    // Part 1 legacy stub — referenced by the Part-1 Perft until Part 5 rewires it.
    public static Move[] GenerateMoves(Position pos) => Array.Empty<Move>();

    static int GenKnights(Position pos, Span<Move> dest, int n, Piece us)
    {
        ulong ours  = pos.WhiteToMove ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong enemy = pos.WhiteToMove ? pos.BlackOccupied : pos.WhiteOccupied;
        ulong knights = pos.Pieces[(int)us];

        while (knights != 0)
        {
            int from = BitOperations.TrailingZeroCount(knights);
            ulong attacks = Attacks.Knight[from] & ~ours;
            n = EmitTargets(dest, n, from, attacks, enemy);
            knights &= knights - 1;
        }
        return n;
    }

    static int GenKing(Position pos, Span<Move> dest, int n, Piece us)
    {
        ulong ours  = pos.WhiteToMove ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong enemy = pos.WhiteToMove ? pos.BlackOccupied : pos.WhiteOccupied;
        int from = BitOperations.TrailingZeroCount(pos.Pieces[(int)us]);
        ulong attacks = Attacks.King[from] & ~ours;
        return EmitTargets(dest, n, from, attacks, enemy);
    }

    static int GenSliders(Position pos, Span<Move> dest, int n, Piece us, bool isBishop)
    {
        ulong ours  = pos.WhiteToMove ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong enemy = pos.WhiteToMove ? pos.BlackOccupied : pos.WhiteOccupied;
        ulong pieces = pos.Pieces[(int)us];
        ulong occ = pos.AllOccupied;

        while (pieces != 0)
        {
            int from = BitOperations.TrailingZeroCount(pieces);
            ulong attacks = isBishop
                ? Magic.BishopAttacks(from, occ)
                : Magic.RookAttacks  (from, occ);
            attacks &= ~ours;
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

    static int GenWhitePawns(Position pos, Span<Move> dest, int n)
    {
        ulong pawns = pos.Pieces[(int)Piece.WhitePawn];
        ulong empty = ~pos.AllOccupied;
        ulong enemy = pos.BlackOccupied;

        ulong singles = Bitboards.ShiftN(pawns) & empty;
        ulong promoSingles = singles & Bitboards.Rank8;
        ulong quietSingles = singles & ~Bitboards.Rank8;

        n = EmitPawnTargets(dest, n, quietSingles, -8, MoveFlag.Quiet);
        n = EmitPromotions (dest, n, promoSingles, -8, capture: false);

        ulong doubles = Bitboards.ShiftN(singles & Bitboards.Rank3) & empty;
        n = EmitPawnTargets(dest, n, doubles, -16, MoveFlag.DoublePawnPush);

        ulong capE = Bitboards.ShiftNE(pawns) & enemy;
        ulong capW = Bitboards.ShiftNW(pawns) & enemy;

        n = EmitPawnTargets(dest, n, capE & ~Bitboards.Rank8, -9, MoveFlag.Capture);
        n = EmitPawnTargets(dest, n, capW & ~Bitboards.Rank8, -7, MoveFlag.Capture);
        n = EmitPromotions (dest, n, capE &  Bitboards.Rank8, -9, capture: true);
        n = EmitPromotions (dest, n, capW &  Bitboards.Rank8, -7, capture: true);

        if (pos.EpSquare >= 0)
        {
            ulong epBB = 1UL << pos.EpSquare;
            ulong epE = Bitboards.ShiftNE(pawns) & epBB;
            ulong epW = Bitboards.ShiftNW(pawns) & epBB;
            n = EmitPawnTargets(dest, n, epE, -9, MoveFlag.EnPassant);
            n = EmitPawnTargets(dest, n, epW, -7, MoveFlag.EnPassant);
        }

        return n;
    }

    static int GenBlackPawns(Position pos, Span<Move> dest, int n)
    {
        ulong pawns = pos.Pieces[(int)Piece.BlackPawn];
        ulong empty = ~pos.AllOccupied;
        ulong enemy = pos.WhiteOccupied;

        ulong singles = Bitboards.ShiftS(pawns) & empty;
        ulong promoSingles = singles & Bitboards.Rank1;
        ulong quietSingles = singles & ~Bitboards.Rank1;

        n = EmitPawnTargets(dest, n, quietSingles, +8, MoveFlag.Quiet);
        n = EmitPromotions (dest, n, promoSingles, +8, capture: false);

        ulong doubles = Bitboards.ShiftS(singles & Bitboards.Rank6) & empty;
        n = EmitPawnTargets(dest, n, doubles, +16, MoveFlag.DoublePawnPush);

        ulong capE = Bitboards.ShiftSE(pawns) & enemy;
        ulong capW = Bitboards.ShiftSW(pawns) & enemy;

        n = EmitPawnTargets(dest, n, capE & ~Bitboards.Rank1, +7, MoveFlag.Capture);
        n = EmitPawnTargets(dest, n, capW & ~Bitboards.Rank1, +9, MoveFlag.Capture);
        n = EmitPromotions (dest, n, capE &  Bitboards.Rank1, +7, capture: true);
        n = EmitPromotions (dest, n, capW &  Bitboards.Rank1, +9, capture: true);

        if (pos.EpSquare >= 0)
        {
            ulong epBB = 1UL << pos.EpSquare;
            ulong epE = Bitboards.ShiftSE(pawns) & epBB;
            ulong epW = Bitboards.ShiftSW(pawns) & epBB;
            n = EmitPawnTargets(dest, n, epE, +7, MoveFlag.EnPassant);
            n = EmitPawnTargets(dest, n, epW, +9, MoveFlag.EnPassant);
        }

        return n;
    }

    static int EmitPawnTargets(Span<Move> dest, int n, ulong targets, int deltaFrom, MoveFlag flag)
    {
        while (targets != 0)
        {
            int to = BitOperations.TrailingZeroCount(targets);
            dest[n++] = new Move(to + deltaFrom, to, flag);
            targets &= targets - 1;
        }
        return n;
    }

    static int EmitPromotions(Span<Move> dest, int n, ulong targets, int deltaFrom, bool capture)
    {
        int baseFlag = capture ? (int)MoveFlag.PromoCaptureKnight : (int)MoveFlag.PromoteKnight;
        while (targets != 0)
        {
            int to = BitOperations.TrailingZeroCount(targets);
            int from = to + deltaFrom;
            for (int i = 0; i < 4; i++)
                dest[n++] = new Move(from, to, (MoveFlag)(baseFlag + i));
            targets &= targets - 1;
        }
        return n;
    }

    static int GenWhiteCastling(Position pos, Span<Move> dest, int n)
    {
        ulong occ = pos.AllOccupied;

        if ((pos.Castling & CastlingRights.WhiteKingside)  != 0
            && (occ & 0x0000000000000060UL) == 0)
        {
            dest[n++] = new Move(4, 6, MoveFlag.KingsideCastle);
        }
        if ((pos.Castling & CastlingRights.WhiteQueenside) != 0
            && (occ & 0x000000000000000EUL) == 0)
        {
            dest[n++] = new Move(4, 2, MoveFlag.QueensideCastle);
        }
        return n;
    }

    static int GenBlackCastling(Position pos, Span<Move> dest, int n)
    {
        ulong occ = pos.AllOccupied;

        if ((pos.Castling & CastlingRights.BlackKingside) != 0
            && (occ & 0x6000000000000000UL) == 0)
        {
            dest[n++] = new Move(60, 62, MoveFlag.KingsideCastle);
        }
        if ((pos.Castling & CastlingRights.BlackQueenside) != 0
            && (occ & 0x0E00000000000000UL) == 0)
        {
            dest[n++] = new Move(60, 58, MoveFlag.QueensideCastle);
        }
        return n;
    }
}

public static class PositionExtensions
{
    // Part 1 stubs — replaced by instance methods in Part 5. Kept as no-ops to keep
    // the Part-1 Perft.cs compiling until then.
    public static void MakeMove  (this Position p, Move m) { }
    public static void UnmakeMove(this Position p, Move m) { }
}
