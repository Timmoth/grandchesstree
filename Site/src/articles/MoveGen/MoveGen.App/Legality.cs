using System.Numerics;

namespace MoveGen.App;

public static class Legality
{
    public static readonly ulong[,] SquaresBetween = BuildSquaresBetween();

    static ulong[,] BuildSquaresBetween()
    {
        var tbl = new ulong[64, 64];
        for (int a = 0; a < 64; a++)
            for (int b = 0; b < 64; b++)
            {
                if (a == b) continue;
                int ra = a >> 3, fa = a & 7;
                int rb = b >> 3, fb = b & 7;
                int dr = Math.Sign(rb - ra);
                int df = Math.Sign(fb - fa);
                bool aligned = ra == rb || fa == fb
                            || Math.Abs(ra - rb) == Math.Abs(fa - fb);
                if (!aligned) continue;

                ulong bb = 0;
                int r = ra + dr, f = fa + df;
                while (r != rb || f != fb)
                {
                    bb |= 1UL << (r * 8 + f);
                    r += dr; f += df;
                }
                tbl[a, b] = bb;
            }
        return tbl;
    }

    public static ulong ComputeKingDanger(Position pos, int kingSq, int enemyColor)
    {
        ulong occWithoutKing = pos.AllOccupied ^ (1UL << kingSq);
        ulong danger = 0;
        bool enemyWhite = enemyColor == 0;

        // Pawns — use the enemy colour's pawn-attack table from each enemy pawn.
        ulong epawns = pos.Pieces[(int)(enemyWhite ? Piece.WhitePawn : Piece.BlackPawn)];
        while (epawns != 0)
        {
            int sq = BitOperations.TrailingZeroCount(epawns);
            danger |= Attacks.Pawn[enemyColor, sq];
            epawns &= epawns - 1;
        }

        // Knights
        ulong knights = pos.Pieces[(int)(enemyWhite ? Piece.WhiteKnight : Piece.BlackKnight)];
        while (knights != 0)
        {
            int sq = BitOperations.TrailingZeroCount(knights);
            danger |= Attacks.Knight[sq];
            knights &= knights - 1;
        }

        // Bishops + queens — diagonal
        ulong bq = pos.Pieces[(int)(enemyWhite ? Piece.WhiteBishop : Piece.BlackBishop)]
                 | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen  : Piece.BlackQueen)];
        while (bq != 0)
        {
            int sq = BitOperations.TrailingZeroCount(bq);
            danger |= Magic.BishopAttacks(sq, occWithoutKing);
            bq &= bq - 1;
        }

        // Rooks + queens — orthogonal
        ulong rq = pos.Pieces[(int)(enemyWhite ? Piece.WhiteRook  : Piece.BlackRook)]
                 | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen : Piece.BlackQueen)];
        while (rq != 0)
        {
            int sq = BitOperations.TrailingZeroCount(rq);
            danger |= Magic.RookAttacks(sq, occWithoutKing);
            rq &= rq - 1;
        }

        // Enemy king
        int eksq = BitOperations.TrailingZeroCount(
            pos.Pieces[(int)(enemyWhite ? Piece.WhiteKing : Piece.BlackKing)]);
        danger |= Attacks.King[eksq];

        return danger;
    }

    public static ulong ComputeCheckers(Position pos, int kingSq, int enemyColor)
    {
        ulong occ = pos.AllOccupied;
        bool enemyWhite = enemyColor == 0;

        ulong attackerPawns   = pos.Pieces[(int)(enemyWhite ? Piece.WhitePawn   : Piece.BlackPawn)];
        ulong attackerKnights = pos.Pieces[(int)(enemyWhite ? Piece.WhiteKnight : Piece.BlackKnight)];
        ulong attackerBQ      = pos.Pieces[(int)(enemyWhite ? Piece.WhiteBishop : Piece.BlackBishop)]
                              | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen  : Piece.BlackQueen)];
        ulong attackerRQ      = pos.Pieces[(int)(enemyWhite ? Piece.WhiteRook   : Piece.BlackRook)]
                              | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen  : Piece.BlackQueen)];

        return  (Attacks.Pawn[1 - enemyColor, kingSq] & attackerPawns)
              | (Attacks.Knight[kingSq]               & attackerKnights)
              | (Magic.BishopAttacks(kingSq, occ)     & attackerBQ)
              | (Magic.RookAttacks  (kingSq, occ)     & attackerRQ);
    }

    public static void ComputePins(Position pos, int kingSq, int enemyColor,
                                   out ulong pinned, Span<ulong> pinLines)
    {
        for (int i = 0; i < 64; i++) pinLines[i] = ulong.MaxValue;
        pinned = 0;

        ulong us = pos.WhiteToMove ? pos.WhiteOccupied : pos.BlackOccupied;
        ulong occ = pos.AllOccupied;
        bool enemyWhite = enemyColor == 0;

        ulong enemyRQ = pos.Pieces[(int)(enemyWhite ? Piece.WhiteRook  : Piece.BlackRook)]
                      | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen : Piece.BlackQueen)];
        ulong enemyBQ = pos.Pieces[(int)(enemyWhite ? Piece.WhiteBishop : Piece.BlackBishop)]
                      | pos.Pieces[(int)(enemyWhite ? Piece.WhiteQueen  : Piece.BlackQueen)];

        pinned |= ScanPinners(kingSq, us, occ, enemyRQ, isBishop: false, pinLines);
        pinned |= ScanPinners(kingSq, us, occ, enemyBQ, isBishop: true,  pinLines);
    }

    static ulong ScanPinners(int kingSq, ulong us, ulong occ, ulong enemySliders,
                             bool isBishop, Span<ulong> pinLines)
    {
        ulong directAttacks = isBishop
            ? Magic.BishopAttacks(kingSq, occ)
            : Magic.RookAttacks  (kingSq, occ);

        // Blockers on the slider's lines from king (could be our pieces)
        ulong blockers = directAttacks & us;
        ulong throughOccupancy = occ ^ blockers;

        ulong xrayAttacks = isBishop
            ? Magic.BishopAttacks(kingSq, throughOccupancy)
            : Magic.RookAttacks  (kingSq, throughOccupancy);

        ulong xrayPinners = xrayAttacks & enemySliders & ~directAttacks;

        ulong pinned = 0;
        while (xrayPinners != 0)
        {
            int pinnerSq = BitOperations.TrailingZeroCount(xrayPinners);
            ulong between = SquaresBetween[kingSq, pinnerSq];
            ulong ourBlocker = between & us;
            if (ourBlocker != 0 && BitOperations.PopCount(ourBlocker) == 1)
            {
                int pinnedSq = BitOperations.TrailingZeroCount(ourBlocker);
                pinned |= ourBlocker;
                pinLines[pinnedSq] = between | (1UL << pinnerSq);
            }
            xrayPinners &= xrayPinners - 1;
        }
        return pinned;
    }
}
