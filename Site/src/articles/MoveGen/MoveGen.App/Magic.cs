using System.Numerics;

namespace MoveGen.App;

public static class Magic
{
    static readonly ulong[]   RookMasks    = new ulong[64];
    static readonly ulong[]   BishopMasks  = new ulong[64];
    static readonly ulong[]   RookMagics   = new ulong[64];
    static readonly ulong[]   BishopMagics = new ulong[64];
    static readonly int[]     RookShifts   = new int[64];
    static readonly int[]     BishopShifts = new int[64];
    static readonly ulong[][] RookTable    = new ulong[64][];
    static readonly ulong[][] BishopTable  = new ulong[64][];

    static bool _initialised;

    static Magic() => Init();

    public static void Init()
    {
        if (_initialised) return;
        InitMasks();

        for (int sq = 0; sq < 64; sq++)
        {
            int rbits = BitOperations.PopCount(RookMasks[sq]);
            int bbits = BitOperations.PopCount(BishopMasks[sq]);

            RookMagics[sq]   = FindMagic(sq, RookMasks[sq],   rbits, isBishop: false);
            BishopMagics[sq] = FindMagic(sq, BishopMasks[sq], bbits, isBishop: true);

            RookShifts[sq]   = 64 - rbits;
            BishopShifts[sq] = 64 - bbits;

            RookTable[sq]   = BuildTable(sq, RookMasks[sq],   RookMagics[sq],   rbits, isBishop: false);
            BishopTable[sq] = BuildTable(sq, BishopMasks[sq], BishopMagics[sq], bbits, isBishop: true);
        }

        _initialised = true;
    }

    static void InitMasks()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            RookMasks[sq]   = BuildRookMask(sq);
            BishopMasks[sq] = BuildBishopMask(sq);
        }
    }

    static ulong BuildRookMask(int sq)
    {
        ulong m = 0;
        int r = sq >> 3, f = sq & 7;
        for (int rr = r + 1; rr <= 6; rr++) m |= 1UL << (rr * 8 + f);
        for (int rr = r - 1; rr >= 1; rr--) m |= 1UL << (rr * 8 + f);
        for (int ff = f + 1; ff <= 6; ff++) m |= 1UL << (r  * 8 + ff);
        for (int ff = f - 1; ff >= 1; ff--) m |= 1UL << (r  * 8 + ff);
        return m;
    }

    static ulong BuildBishopMask(int sq)
    {
        ulong m = 0;
        int r = sq >> 3, f = sq & 7;
        for (int rr = r+1, ff = f+1; rr <= 6 && ff <= 6; rr++, ff++) m |= 1UL << (rr * 8 + ff);
        for (int rr = r+1, ff = f-1; rr <= 6 && ff >= 1; rr++, ff--) m |= 1UL << (rr * 8 + ff);
        for (int rr = r-1, ff = f+1; rr >= 1 && ff <= 6; rr--, ff++) m |= 1UL << (rr * 8 + ff);
        for (int rr = r-1, ff = f-1; rr >= 1 && ff >= 1; rr--, ff--) m |= 1UL << (rr * 8 + ff);
        return m;
    }

    static ulong OccupancyAtIndex(int index, ulong mask)
    {
        ulong result = 0;
        int n = BitOperations.PopCount(mask);
        for (int i = 0; i < n; i++)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            if (((index >> i) & 1) != 0)
                result |= 1UL << bit;
        }
        return result;
    }

    static ulong SparseRandom(Random rng)
    {
        ulong R() => ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
        return R() & R() & R();
    }

    static ulong FindMagic(int sq, ulong mask, int bits, bool isBishop)
    {
        int n = 1 << bits;
        ulong[] occupancies = new ulong[n];
        ulong[] reference   = new ulong[n];
        ulong[] used        = new ulong[n];

        for (int i = 0; i < n; i++)
        {
            occupancies[i] = OccupancyAtIndex(i, mask);
            reference[i]   = isBishop
                ? Attacks.BishopAttacks(sq, occupancies[i])
                : Attacks.RookAttacks  (sq, occupancies[i]);
        }

        var rng = new Random(sq + (isBishop ? 1000 : 0));
        for (int attempt = 0; attempt < 100_000_000; attempt++)
        {
            ulong magic = SparseRandom(rng);

            if (BitOperations.PopCount((mask * magic) & 0xFF00000000000000UL) < 6)
                continue;

            Array.Clear(used, 0, n);
            bool ok = true;
            for (int i = 0; i < n && ok; i++)
            {
                int idx = (int)((occupancies[i] * magic) >> (64 - bits));
                if (used[idx] == 0)        used[idx] = reference[i];
                else if (used[idx] != reference[i]) ok = false;
            }
            if (ok) return magic;
        }
        throw new Exception($"No magic found for sq={sq} bishop={isBishop}");
    }

    static ulong[] BuildTable(int sq, ulong mask, ulong magic, int bits, bool isBishop)
    {
        ulong[] table = new ulong[1 << bits];
        int n = 1 << bits;
        for (int i = 0; i < n; i++)
        {
            ulong occ = OccupancyAtIndex(i, mask);
            int idx   = (int)((occ * magic) >> (64 - bits));
            ulong attack = isBishop
                ? Attacks.BishopAttacks(sq, occ)
                : Attacks.RookAttacks  (sq, occ);
            table[idx] = attack;
        }
        return table;
    }

    public static ulong RookAttacks(int sq, ulong occ)
    {
        ulong idx = ((occ & RookMasks[sq]) * RookMagics[sq]) >> RookShifts[sq];
        return RookTable[sq][idx];
    }

    public static ulong BishopAttacks(int sq, ulong occ)
    {
        ulong idx = ((occ & BishopMasks[sq]) * BishopMagics[sq]) >> BishopShifts[sq];
        return BishopTable[sq][idx];
    }

    public static ulong QueenAttacks(int sq, ulong occ)
        => RookAttacks(sq, occ) | BishopAttacks(sq, occ);
}
