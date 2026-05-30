# Move Generation, Part 6 — Magic Bitboards

The generator from Part 4 is correct, and the make/unmake from Part 5 is correct, and together they give us matching perft numbers. They're also slow. Most of the time is spent in `RookAttacks` and `BishopAttacks` — the classical ray-scan does up to four bitscans and four conditional XORs per slider, every time it's called, on every search node.

This is the article where we make those two functions fast. We'll replace the ray-scan with **magic bitboards**: a single multiply, a single shift, and a single table lookup, in exchange for ~840 KiB of precomputed attack tables. No new perft numbers — the move generator still produces the same moves — but the runtime drops by several times.

## The hashing problem

For a rook on `sq`, the squares it attacks depend on `sq` and on which squares along its rays are blocked. Different occupancy bitboards can produce the same attack set — only the *first* blocker along each ray matters; everything past it is hidden. So if we could feed an occupancy bitboard through a function that mapped it to a small dense index, we could just look up the answer.

That function is the magic-bitboard hash:

```
index = ((occupancy & mask[sq]) * magic[sq]) >> (64 - bits[sq])
attack = attackTable[sq][index]
```

Four steps:

1. **Mask** — keep only the occupancy bits that *could* be blockers along this square's rays. Edge squares don't matter; if there's no square beyond them, "blocked at the edge" looks the same as "no blocker". A rook on d4 has 10 relevant occupancy bits (the inner squares along its rank and file); a bishop has at most 9.
2. **Multiply** — multiply by a precomputed 64-bit *magic* constant. The result is essentially nonsense, *except* that its top `n` bits form a unique fingerprint of the masked occupancy.
3. **Shift** — right-shift by `64 - n` to extract those top n bits as a small index.
4. **Lookup** — index a per-square attack table.

Why does it work? Because the number of *distinct attack sets* for a slider on a square is far smaller than the number of possible occupancy bitmasks. A rook on a corner has only 49 unique attack sets across all 4096 possible occupancies. A bishop on d4 has 108 across its 512. So we don't need a perfect-hash function — we need any function that maps each occupancy to *some* index such that two occupancies with the *same* attack set may collide but two with *different* attack sets never do. That's exactly what magic numbers give us.

## How to find a magic

We try random 64-bit candidates and accept the first one whose `(occ * magic) >> (64 - n)` doesn't conflate two occupancies with different attack sets. The trick is a **sparse PRNG** — most working magics have very few set bits, so we AND together three random values to bias toward sparse candidates:

```csharp
static ulong SparseRandom(System.Random rng)
{
    ulong R() => ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
    return R() & R() & R();
}
```

The whole search loop is a few dozen lines. We'll build it after we have the supporting pieces.

## Masks: what counts as "relevant"

For each square we want the bits along that piece's rays, excluding both the square itself and the outer edges:

```csharp
using System.Numerics;

public static class Magic
{
    static readonly ulong[] RookMasks   = new ulong[64];
    static readonly ulong[] BishopMasks = new ulong[64];

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
}
```

Notice the masks stop one short of the edge (`rr <= 6`, `ff >= 1`) — edge squares can never *be* blockers in a meaningful way (the ray ends there regardless).

The popcount of a square's mask tells us how many index bits we need. Corner rooks see 12; central rooks see 10. Corner bishops see 6; central bishops see 9. (Bishops are easier because their masks are smaller.)

## Enumerating occupancies

To validate a candidate magic we need every possible occupancy along the mask's bits. There are `2^popcount(mask)` of them. We enumerate by treating an index `0..2^n - 1` as a bitmask over the set bits of the mask:

```csharp
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
```

For each occupancy we also need to know the *correct* attack set — the answer the magic table will eventually return. We've already got that: `Attacks.RookAttacks` and `Attacks.BishopAttacks` from Part 4. The classical ray-scan is slow but right, and that's exactly what we want for setting things up.

## The finder

We try sparse-random magics, hash all 2^n occupancies through each, and accept the first magic whose results never collide on different attack sets:

```csharp
public static class Magic
{
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

        var rng = new System.Random(sq + (isBishop ? 1000 : 0));
        for (int attempt = 0; attempt < 100_000_000; attempt++)
        {
            ulong magic = SparseRandom(rng);

            // Quick reject — magic must spread the top bits enough.
            if (BitOperations.PopCount((mask * magic) & 0xFF00000000000000UL) < 6)
                continue;

            System.Array.Clear(used, 0, n);
            bool ok = true;
            for (int i = 0; i < n && ok; i++)
            {
                int idx = (int)((occupancies[i] * magic) >> (64 - bits));
                if (used[idx] == 0)        used[idx] = reference[i];
                else if (used[idx] != reference[i]) ok = false;
            }
            if (ok) return magic;
        }
        throw new System.Exception($"No magic found for sq={sq} bishop={isBishop}");
    }
}
```

The "spread the top bits" pre-filter is a heuristic — the high byte of `mask * magic` needs at least 6 set bits, or the index distribution will be terrible. It's optional but skipping it makes the search much slower.

## Wiring it all together

We need, per square, the mask, the magic, the shift (`64 - bits`), and the populated attack table:

```csharp
public static class Magic
{
    static readonly ulong[] RookMasks    = new ulong[64];
    static readonly ulong[] BishopMasks  = new ulong[64];
    static readonly ulong[] RookMagics   = new ulong[64];
    static readonly ulong[] BishopMagics = new ulong[64];
    static readonly int[]   RookShifts   = new int[64];
    static readonly int[]   BishopShifts = new int[64];
    static readonly ulong[][] RookTable   = new ulong[64][];
    static readonly ulong[][] BishopTable = new ulong[64][];

    static bool _initialised;

    // Static constructor runs on first access — keeps things working without
    // requiring callers to remember an explicit Init().
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
```

Total memory: bishops use up to 512 entries per square (9 bits in the middle, smaller at the edges), rooks up to 4096. Adding up the actual sizes per square gives ~840 KiB total. Comfortably within L2 on any modern CPU.

`Magic.Init()` does roughly 128 magic searches and builds the tables. On a recent machine it takes a fraction of a second. The static constructor calls it automatically on first access, so callers don't need to remember; if you want to control *when* the cost is paid (e.g. eagerly at program start before timing-sensitive code), call `Magic.Init()` explicitly — it's idempotent.

## Dropping it in

Two lines change in the rest of the engine. In `MoveGenerator.GenSliders` from Part 4:

```csharp
ulong attacks = isBishop
    ? Magic.BishopAttacks(from, occ)   // was Attacks.BishopAttacks
    : Magic.RookAttacks  (from, occ);  // was Attacks.RookAttacks
```

And in `Attacks.IsAttackedBy`:

```csharp
if ((Magic.BishopAttacks(sq, occ) & (bishops | queens)) != 0) return true;
if ((Magic.RookAttacks  (sq, occ) & (rooks   | queens)) != 0) return true;
```

Run perft. Numbers should be identical to Part 5. Time should be a *lot* shorter — typically 3–5× speedup, more on hardware with fast 64-bit multiply.

```csharp
internal class Program
{
    static void Main()
    {
        Magic.Init();   // call once at startup

        var pos = Fen.Parse(Fen.Initial);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ulong nodes = Perft.Run(pos, 5);
        sw.Stop();
        System.Console.WriteLine(
            $"perft(5) = {nodes} in {sw.Elapsed.TotalSeconds:F2}s "
          + $"({nodes / sw.Elapsed.TotalSeconds / 1_000_000:F2} Mnps)");
    }
}
```

Worth keeping the classical ray-scan around in `Attacks` for two reasons: the magic-finder uses it to compute reference attack sets, and it's a useful fallback if we ever want to double-check a magic implementation.

## A note on saving magics

The finder runs every time the program starts. That's fine — it's fast. But if we wanted to skip the startup cost we could just print the found magics once and paste them in as a constant array. There's nothing dynamic about them; any magic that works will keep working forever. Doing it that way:

```csharp
// Optional — precomputed; replaces the search step.
static readonly ulong[] PrecomputedRookMagics = new ulong[]
{
    0xa8002c000108020UL, /* a1 */ 0x6c00049b0002001UL, /* b1 */ ...
};
```

I find that easier to debug while developing (re-finding is automatic if you change the layout) and easier to ship when stable (no startup cost). Take whichever you prefer.

## What's next

Part 7 — the final article. We replace the make-then-check-king-attacked filter with a proper **legal move generator**. We'll compute three bitboards per node — the *checkers* attacking our king, the *pinned* friendly pieces, and the *king-danger* squares — and use them to emit only moves that are genuinely legal. The perft numbers stay the same (Part 5's filter was already correct), but the generator gets cleaner *and* faster because we no longer make-and-undo illegal candidates. Plus we'll handle the en-passant pin and the full castling legality test inside the generator itself.
