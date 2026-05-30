using System.Numerics;

namespace MoveGen.App;

public static class Attacks
{
    public static readonly ulong[]  Knight = new ulong[64];
    public static readonly ulong[]  King   = new ulong[64];
    public static readonly ulong[,] Pawn   = new ulong[2, 64];
    public static readonly ulong[,] Ray    = new ulong[8, 64];

    public const int N = 0, NE = 1, E = 2, SE = 3, S = 4, SW = 5, W = 6, NW = 7;

    static Attacks()
    {
        InitKnight();
        InitKing();
        InitPawn();
        InitRays();
    }

    static ulong KnightAttacksOf(ulong knights)
    {
        ulong l1 = (knights >> 1) & 0x7F7F7F7F7F7F7F7FUL;
        ulong l2 = (knights >> 2) & 0x3F3F3F3F3F3F3F3FUL;
        ulong r1 = (knights << 1) & 0xFEFEFEFEFEFEFEFEUL;
        ulong r2 = (knights << 2) & 0xFCFCFCFCFCFCFCFCUL;
        ulong h1 = l1 | r1;
        ulong h2 = l2 | r2;
        return (h1 << 16) | (h1 >> 16) | (h2 << 8) | (h2 >> 8);
    }

    static void InitKnight()
    {
        for (int sq = 0; sq < 64; sq++)
            Knight[sq] = KnightAttacksOf(1UL << sq);
    }

    static ulong KingAttacksOf(ulong k)
    {
        ulong horiz = Bitboards.ShiftE(k) | Bitboards.ShiftW(k);
        k |= horiz;
        return horiz | Bitboards.ShiftN(k) | Bitboards.ShiftS(k);
    }

    static void InitKing()
    {
        for (int sq = 0; sq < 64; sq++)
            King[sq] = KingAttacksOf(1UL << sq);
    }

    static void InitPawn()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            ulong b = 1UL << sq;
            Pawn[0, sq] = Bitboards.ShiftNE(b) | Bitboards.ShiftNW(b);
            Pawn[1, sq] = Bitboards.ShiftSE(b) | Bitboards.ShiftSW(b);
        }
    }

    static void InitRays()
    {
        int[] dr = {  1,  1,  0, -1, -1, -1,  0,  1 };
        int[] df = {  0,  1,  1,  1,  0, -1, -1, -1 };

        for (int dir = 0; dir < 8; dir++)
            for (int sq = 0; sq < 64; sq++)
            {
                int r = sq >> 3, f = sq & 7;
                ulong ray = 0;
                for (;;)
                {
                    r += dr[dir];
                    f += df[dir];
                    if ((uint)r > 7 || (uint)f > 7) break;
                    ray |= 1UL << (r * 8 + f);
                }
                Ray[dir, sq] = ray;
            }
    }

    static ulong PositiveRay(int sq, int dir, ulong occ)
    {
        ulong a = Ray[dir, sq];
        ulong blockers = a & occ;
        if (blockers != 0)
            a ^= Ray[dir, BitOperations.TrailingZeroCount(blockers)];
        return a;
    }

    static ulong NegativeRay(int sq, int dir, ulong occ)
    {
        ulong a = Ray[dir, sq];
        ulong blockers = a & occ;
        if (blockers != 0)
            a ^= Ray[dir, 63 - BitOperations.LeadingZeroCount(blockers)];
        return a;
    }

    public static ulong RookAttacks(int sq, ulong occ)
        =>  PositiveRay(sq, N,  occ)
          | NegativeRay(sq, S,  occ)
          | PositiveRay(sq, E,  occ)
          | NegativeRay(sq, W,  occ);

    public static ulong BishopAttacks(int sq, ulong occ)
        =>  PositiveRay(sq, NE, occ)
          | NegativeRay(sq, SE, occ)
          | PositiveRay(sq, NW, occ)
          | NegativeRay(sq, SW, occ);

    public static ulong QueenAttacks(int sq, ulong occ)
        => RookAttacks(sq, occ) | BishopAttacks(sq, occ);

    public static bool IsAttackedBy(Position pos, int sq, int byColor)
    {
        ulong occ = pos.AllOccupied;
        bool white = byColor == 0;

        ulong pawns   = pos.Pieces[(int)(white ? Piece.WhitePawn   : Piece.BlackPawn)];
        ulong knights = pos.Pieces[(int)(white ? Piece.WhiteKnight : Piece.BlackKnight)];
        ulong bishops = pos.Pieces[(int)(white ? Piece.WhiteBishop : Piece.BlackBishop)];
        ulong rooks   = pos.Pieces[(int)(white ? Piece.WhiteRook   : Piece.BlackRook)];
        ulong queens  = pos.Pieces[(int)(white ? Piece.WhiteQueen  : Piece.BlackQueen)];
        ulong king    = pos.Pieces[(int)(white ? Piece.WhiteKing   : Piece.BlackKing)];

        // Pawn lookup uses the *opposite* colour's table.
        if ((Pawn[1 - byColor, sq] & pawns)   != 0) return true;
        if ((Knight[sq]            & knights) != 0) return true;
        if ((King[sq]              & king)    != 0) return true;
        if ((Magic.BishopAttacks(sq, occ) & (bishops | queens)) != 0) return true;
        if ((Magic.RookAttacks  (sq, occ) & (rooks   | queens)) != 0) return true;

        return false;
    }
}
