# Move Generation, Part 7 — Legal Move Generation

Our generator from Part 4 emits pseudo-legal moves, and the perft loop in Part 5 filters out the ones that leave our king in check by making each one, asking "is our king attacked?", and rolling it back. That gets us correct perft numbers, but it does a lot of pointless work — every search node tries moves that we could have known were illegal up front.

This article fixes that. We'll compute three bitboards once per node and use them to emit **only legal moves**. By the end we should be able to drop the post-hoc filter from the perft loop, get the same numbers back, and run faster.

## The three bitboards

At every position we compute:

1. **`checkers`** — enemy pieces currently attacking our king.
2. **`pinned`** — our pieces that can't move freely because doing so would expose the king to a slider. Per-pinned-piece we also need its **pin line** — the squares it's still allowed to move to (which include capturing the pinner).
3. **`kingDanger`** — squares the enemy attacks *if we pretend our king isn't on the board*. The "pretend" matters because a slider attacking the king extends past it — the square immediately behind the king is also unsafe, even though normal attack-generation wouldn't show it.

With those three in hand the rules write themselves:

- **Two or more checkers:** only king moves to non-`kingDanger` squares are legal. By geometry there's never more than two simultaneous checkers (one direct, one discovered).
- **One checker:** king moves as above, *plus* moves that capture the checker or interpose on the check line — but only by **unpinned** friendly pieces.
- **No checkers:** every pseudo-legal move is fine *except* a pinned piece moving off its pin line. King moves still avoid `kingDanger`.

Plus a couple of stragglers — en-passant has its weird horizontal-pin case, and castling has its transit-square test.

## Computing the trinity

### King-danger squares

Recompute the enemy's attack bitboard with our king's bit removed from occupancy. Anything they hit is somewhere the king cannot land:

```csharp
using System.Numerics;

public static class Legality
{
    public static ulong ComputeKingDanger(Position pos, int kingSq, int enemyColor)
    {
        ulong occWithoutKing = pos.AllOccupied ^ (1UL << kingSq);

        ulong danger = 0;

        // Pawns
        ulong epawns = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhitePawn : Piece.BlackPawn)];
        while (epawns != 0)
        {
            int sq = BitOperations.TrailingZeroCount(epawns);
            danger |= Attacks.Pawn[enemyColor, sq];
            epawns &= epawns - 1;
        }

        // Knights
        ulong knights = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteKnight : Piece.BlackKnight)];
        while (knights != 0)
        {
            int sq = BitOperations.TrailingZeroCount(knights);
            danger |= Attacks.Knight[sq];
            knights &= knights - 1;
        }

        // Bishops + queens (diagonal)
        ulong bq = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteBishop : Piece.BlackBishop)]
                 | pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteQueen  : Piece.BlackQueen)];
        while (bq != 0)
        {
            int sq = BitOperations.TrailingZeroCount(bq);
            danger |= Magic.BishopAttacks(sq, occWithoutKing);
            bq &= bq - 1;
        }

        // Rooks + queens (orthogonal)
        ulong rq = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteRook  : Piece.BlackRook)]
                 | pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteQueen : Piece.BlackQueen)];
        while (rq != 0)
        {
            int sq = BitOperations.TrailingZeroCount(rq);
            danger |= Magic.RookAttacks(sq, occWithoutKing);
            rq &= rq - 1;
        }

        // Enemy king
        int eksq = BitOperations.TrailingZeroCount(
            pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteKing : Piece.BlackKing)]);
        danger |= Attacks.King[eksq];

        return danger;
    }
}
```

### Checkers

The same `IsAttackedBy` idea from Part 4, but this time we want the set of *attackers* not just a boolean. We compose it from each piece type's attack pattern shot back from the king square:

```csharp
public static ulong ComputeCheckers(Position pos, int kingSq, int enemyColor)
{
    ulong occ = pos.AllOccupied;

    ulong attackerPawns   = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhitePawn   : Piece.BlackPawn)];
    ulong attackerKnights = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteKnight : Piece.BlackKnight)];
    ulong attackerBQ      = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteBishop : Piece.BlackBishop)]
                          | pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteQueen  : Piece.BlackQueen)];
    ulong attackerRQ      = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteRook   : Piece.BlackRook)]
                          | pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteQueen  : Piece.BlackQueen)];

    return  (Attacks.Pawn[1 - enemyColor, kingSq] & attackerPawns)
          | (Attacks.Knight[kingSq]               & attackerKnights)
          | (Magic.BishopAttacks(kingSq, occ)     & attackerBQ)
          | (Magic.RookAttacks  (kingSq, occ)     & attackerRQ);
}
```

We use `Attacks.Pawn[1 - enemyColor, ...]` because a square is attacked by an enemy pawn at sq X if *our*-colour pawn at the king's square would attack X.

### Pinned pieces and pin lines

For each enemy slider that would attack the king *if we removed exactly one of our pieces in between*, that piece is pinned. The pin line is the ray from the king through (and including) the pinner.

We use an x-ray attack — shoot from the king square through one of our own pieces and see if an enemy slider sits on the far side. The pin-line array is sized 64 (one entry per square); we use a `Span<ulong>` and the caller stack-allocates the buffer, so the computation is allocation-free:

```csharp
public static void ComputePins(Position pos, int kingSq, int enemyColor,
                               out ulong pinned, Span<ulong> pinLines)
{
    for (int i = 0; i < 64; i++) pinLines[i] = ulong.MaxValue;
    pinned = 0;

    ulong us = pos.WhiteToMove ? pos.WhiteOccupied : pos.BlackOccupied;
    ulong occ = pos.AllOccupied;

    ulong enemyRQ = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteRook  : Piece.BlackRook)]
                  | pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteQueen : Piece.BlackQueen)];
    ulong enemyBQ = pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteBishop : Piece.BlackBishop)]
                  | pos.Pieces[(int)(enemyColor == 0 ? Piece.WhiteQueen  : Piece.BlackQueen)];

    pinned |= ScanPinners(kingSq, us, occ, enemyRQ, isBishop: false, pinLines);
    pinned |= ScanPinners(kingSq, us, occ, enemyBQ, isBishop: true,  pinLines);
}

static ulong ScanPinners(int kingSq, ulong us, ulong occ, ulong enemySliders,
                         bool isBishop, Span<ulong> pinLines)
{
    ulong directAttacks = isBishop
        ? Magic.BishopAttacks(kingSq, occ)
        : Magic.RookAttacks  (kingSq, occ);

    ulong blockers = directAttacks & us;         // our blockers on the slider's rays
    ulong throughOccupancy = occ ^ blockers;     // remove them

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
        // X-rays were constructed so exactly one of our pieces sits between king & pinner.
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
```

The `pinLines` span is full-board (`ulong.MaxValue`) for unpinned pieces, so masking attacks by `pinLines[from]` is a no-op for non-pinned pieces and an effective restriction for pinned ones.

The caller (the legal generator's `Generate` method) supplies the buffer with `stackalloc`:

```csharp
Span<ulong> pinLines = stackalloc ulong[64];
Legality.ComputePins(pos, kingSq, them, out ulong pinned, pinLines);
```

`SquaresBetween[a, b]` is the bitboard of squares strictly between `a` and `b` when they're on the same rank, file, or diagonal — and zero otherwise. We pre-compute it at startup:

```csharp
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
                int dr = System.Math.Sign(rb - ra);
                int df = System.Math.Sign(fb - fa);
                bool aligned = ra == rb || fa == fb
                            || System.Math.Abs(ra - rb) == System.Math.Abs(fa - fb);
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
}
```

## The legal generator

```csharp
public static class LegalMoveGenerator
{
    public static int Generate(Position pos, System.Span<Move> dest)
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

        // King moves: always allowed, just avoid danger and own pieces.
        n = GenKingMoves(pos, dest, n, kingSq, kingDanger);

        int numCheckers = BitOperations.PopCount(checkers);
        if (numCheckers >= 2)
            return n;   // double check — king moves only

        // checkMask: squares a non-king piece is allowed to *land* on.
        //   not in check       → everywhere
        //   single check       → checker's square (for capture) ∪ squares between king and checker (for block)
        ulong checkMask = ulong.MaxValue;
        if (numCheckers == 1)
        {
            int checkerSq = BitOperations.TrailingZeroCount(checkers);
            checkMask = checkers | Legality.SquaresBetween[kingSq, checkerSq];
        }

        n = GenPawnsLegal  (pos, dest, n, us, checkMask, pinned, pinLines);
        n = GenKnightsLegal(pos, dest, n, us, checkMask, pinned);
        n = GenSlidersLegal(pos, dest, n, us, checkMask, pinned, pinLines);

        if (numCheckers == 0)
            n = GenCastlingLegal(pos, dest, n, kingSq, kingDanger);

        return n;
    }
}
```

The mainline is short — most of the work is in those three pre-computations. The piece generators are minor twists on the Part-4 versions.

### Knights

A pinned knight can never move legally — no straight line covers a knight jump. Otherwise: pseudo-legal attacks restricted to the `checkMask`:

```csharp
static int GenKnightsLegal(Position pos, System.Span<Move> dest, int n,
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
```

`EmitTargets` is the same helper from Part 4 — emits captures and quiets based on whether the target is in `enemy`.

### Sliders

A pinned slider may still move along its pin line — that's what the per-piece pin-line mask is for:

```csharp
static int GenSlidersLegal(Position pos, System.Span<Move> dest, int n,
                           int us, ulong checkMask, ulong pinned, ulong[] pinLines)
{
    ulong ourPieces = us == 0 ? pos.WhiteOccupied : pos.BlackOccupied;
    ulong enemy     = us == 0 ? pos.BlackOccupied : pos.WhiteOccupied;
    ulong occ = pos.AllOccupied;

    Piece bishop = us == 0 ? Piece.WhiteBishop : Piece.BlackBishop;
    Piece rook   = us == 0 ? Piece.WhiteRook   : Piece.BlackRook;
    Piece queen  = us == 0 ? Piece.WhiteQueen  : Piece.BlackQueen;

    n = EmitSlider(dest, n, pos.Pieces[(int)bishop], Magic.BishopAttacks,
                   ourPieces, enemy, occ, checkMask, pinned, pinLines);
    n = EmitSlider(dest, n, pos.Pieces[(int)rook],   Magic.RookAttacks,
                   ourPieces, enemy, occ, checkMask, pinned, pinLines);
    n = EmitSlider(dest, n, pos.Pieces[(int)queen],  Magic.QueenAttacks,
                   ourPieces, enemy, occ, checkMask, pinned, pinLines);
    return n;
}

delegate ulong AttackFn(int sq, ulong occ);

static int EmitSlider(System.Span<Move> dest, int n, ulong pieces, AttackFn attackFn,
                      ulong ourPieces, ulong enemy, ulong occ,
                      ulong checkMask, ulong pinned, Span<ulong> pinLines)
{
    while (pieces != 0)
    {
        int from = BitOperations.TrailingZeroCount(pieces);
        ulong attacks = attackFn(from, occ) & ~ourPieces & checkMask;
        if (((1UL << from) & pinned) != 0)
            attacks &= pinLines[from];      // pinned: restrict to pin line
        n = EmitTargets(dest, n, from, attacks, enemy);
        pieces &= pieces - 1;
    }
    return n;
}
```

### Pawns

Pawns are the busy ones. A pinned pawn can push only if the pin is along its file, capture only if the pin is along its capture diagonal. The `pinLines[from]` mask handles all of that uniformly — restrict the targets to the pin line.

The one piece that doesn't quite fit the mask-everything pattern is **en-passant** with its horizontal pin. We can't express the "and your king isn't suddenly attacked along the rank because both pawns vacated" check as a target-mask. The cheap solution: when we generate an ep candidate, simulate the capture (just XOR the two pawns out of the occupancy), and re-check whether the king is attacked along its rank.

```csharp
static int GenPawnsLegal(Position pos, System.Span<Move> dest, int n,
                         int us, ulong checkMask, ulong pinned, ulong[] pinLines)
{
    bool white = us == 0;
    ulong pawns = pos.Pieces[(int)(white ? Piece.WhitePawn : Piece.BlackPawn)];
    ulong empty = ~pos.AllOccupied;
    ulong enemy = white ? pos.BlackOccupied : pos.WhiteOccupied;

    int dPush = white ? -8 : +8;
    int dCapE = white ? -9 : +7;
    int dCapW = white ? -7 : +9;
    ulong promoRank = white ? Bitboards.Rank8 : Bitboards.Rank1;
    ulong startRank3 = white ? Bitboards.Rank3 : Bitboards.Rank6;

    ulong singles = (white ? Bitboards.ShiftN(pawns) : Bitboards.ShiftS(pawns)) & empty;
    ulong doubles = (white ? Bitboards.ShiftN(singles & startRank3) : Bitboards.ShiftS(singles & startRank3)) & empty;
    ulong capE    = (white ? Bitboards.ShiftNE(pawns) : Bitboards.ShiftSE(pawns)) & enemy;
    ulong capW    = (white ? Bitboards.ShiftNW(pawns) : Bitboards.ShiftSW(pawns)) & enemy;

    // Apply check mask: if in check, pushes must land on a blocking square and
    // captures must take the checker (which the same mask expresses).
    singles &= checkMask;
    doubles &= checkMask;
    capE    &= checkMask;
    capW    &= checkMask;

    n = EmitPawnLegal(dest, n, singles & ~promoRank, dPush, MoveFlag.Quiet,           pinned, pinLines);
    n = EmitPromos   (dest, n, singles &  promoRank, dPush, capture: false,           pinned, pinLines);
    n = EmitPawnLegal(dest, n, doubles,              2 * dPush, MoveFlag.DoublePawnPush, pinned, pinLines);
    n = EmitPawnLegal(dest, n, capE & ~promoRank,    dCapE, MoveFlag.Capture,         pinned, pinLines);
    n = EmitPawnLegal(dest, n, capW & ~promoRank,    dCapW, MoveFlag.Capture,         pinned, pinLines);
    n = EmitPromos   (dest, n, capE &  promoRank,    dCapE, capture: true,            pinned, pinLines);
    n = EmitPromos   (dest, n, capW &  promoRank,    dCapW, capture: true,            pinned, pinLines);

    // En passant — special legality check
    if (pos.EpSquare >= 0)
    {
        n = GenEnPassant(pos, dest, n, us);
    }

    return n;
}

static int EmitPawnLegal(System.Span<Move> dest, int n, ulong targets, int deltaFrom,
                         MoveFlag flag, ulong pinned, ulong[] pinLines)
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

static int EmitPromos(System.Span<Move> dest, int n, ulong targets, int deltaFrom,
                      bool capture, ulong pinned, ulong[] pinLines)
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
```

### En-passant: the horizontal-pin check (and in-check filter)

We brute-force this — simulate the capture, see if our king is suddenly attacked along the rank. We also handle the rare case of an en-passant capture that resolves a check: it's only legal in single check if the captured pawn IS the lone checker. If we're in check from anything else (a knight, a slider that's not the just-pushed pawn), removing the pawn doesn't help and we must suppress the ep move.

```csharp
static int GenEnPassant(Position pos, System.Span<Move> dest, int n,
                        int us, int kingSq, ulong checkers)
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

static int TryEpMove(Position pos, System.Span<Move> dest, int n,
                     int from, int to, int us, int kingSq, ulong checkers)
{
    bool white = us == 0;
    int capturedPawnSq = white ? to - 8 : to + 8;

    // If we're in single check, the ep capture is only legal when it removes
    // the lone checker.  Double check was filtered out before we got here.
    int numCheckers = BitOperations.PopCount(checkers);
    if (numCheckers == 1 && (checkers & (1UL << capturedPawnSq)) == 0)
        return n;

    ulong simOcc = pos.AllOccupied
                 ^ (1UL << from)
                 ^ (1UL << to)
                 ^ (1UL << capturedPawnSq);

    int them = 1 - us;
    ulong enemyRQ = pos.Pieces[(int)(them == 0 ? Piece.WhiteRook  : Piece.BlackRook)]
                  | pos.Pieces[(int)(them == 0 ? Piece.WhiteQueen : Piece.BlackQueen)];
    ulong enemyBQ = pos.Pieces[(int)(them == 0 ? Piece.WhiteBishop : Piece.BlackBishop)]
                  | pos.Pieces[(int)(them == 0 ? Piece.WhiteQueen  : Piece.BlackQueen)];

    bool kingAttacked =
           (Magic.RookAttacks  (kingSq, simOcc) & enemyRQ) != 0
        || (Magic.BishopAttacks(kingSq, simOcc) & enemyBQ) != 0;

    if (!kingAttacked)
        dest[n++] = new Move(from, to, MoveFlag.EnPassant);
    return n;
}
```

### King moves and castling

King moves just avoid king-danger; castling needs the full transit-square check:

```csharp
static int GenKingMoves(Position pos, System.Span<Move> dest, int n,
                        int kingSq, ulong kingDanger)
{
    bool white = pos.WhiteToMove;
    ulong ourPieces = white ? pos.WhiteOccupied : pos.BlackOccupied;
    ulong enemy     = white ? pos.BlackOccupied : pos.WhiteOccupied;
    ulong targets = Attacks.King[kingSq] & ~ourPieces & ~kingDanger;
    return EmitTargets(dest, n, kingSq, targets, enemy);
}

static int GenCastlingLegal(Position pos, System.Span<Move> dest, int n,
                            int kingSq, ulong kingDanger)
{
    bool white = pos.WhiteToMove;
    ulong occ = pos.AllOccupied;

    if (white)
    {
        if ((pos.Castling & CastlingRights.WhiteKingside) != 0
            && (occ & 0x60UL) == 0
            && (kingDanger & 0x70UL) == 0)       // e1, f1, g1 unattacked
            dest[n++] = new Move(4, 6, MoveFlag.KingsideCastle);
        if ((pos.Castling & CastlingRights.WhiteQueenside) != 0
            && (occ & 0x0EUL) == 0
            && (kingDanger & 0x1CUL) == 0)       // e1, d1, c1 unattacked
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
```

For occupancy we test the squares *between* king and rook (excluding king square), but for the attack test we test the king's start, transit, and destination — three squares. The 0x70 / 0x1C / etc. constants pack those tests neatly.

## Updating perft

The whole make-then-test filter goes. Perft becomes the simple loop again:

```csharp
public static class Perft
{
    public static ulong Run(Position pos, int depth)
    {
        if (depth == 0) return 1UL;
        Span<Move> buf = stackalloc Move[256];
        int n = LegalMoveGenerator.Generate(pos, buf);
        ulong nodes = 0;
        for (int i = 0; i < n; i++)
        {
            pos.MakeMove(buf[i]);
            nodes += Run(pos, depth - 1);
            pos.UnmakeMove(buf[i]);
        }
        return nodes;
    }
}
```

Bulk-counting trick — at `depth == 1` we can return `n` directly without ever calling MakeMove, which roughly doubles speed:

```csharp
public static ulong Run(Position pos, int depth)
{
    if (depth == 0) return 1UL;
    Span<Move> buf = stackalloc Move[256];
    int n = LegalMoveGenerator.Generate(pos, buf);
    if (depth == 1) return (ulong)n;
    ulong nodes = 0;
    for (int i = 0; i < n; i++)
    {
        pos.MakeMove(buf[i]);
        nodes += Run(pos, depth - 1);
        pos.UnmakeMove(buf[i]);
    }
    return nodes;
}
```

## Running it

```csharp
internal class Program
{
    static void Main()
    {
        Magic.Init();

        var pos = Fen.Parse(Fen.Initial);
        for (int d = 1; d <= 6; d++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ulong nodes = Perft.Run(pos, d);
            sw.Stop();
            System.Console.WriteLine($"perft({d}) = {nodes,12}   {sw.Elapsed.TotalSeconds,7:F2}s");
        }

        System.Console.WriteLine();

        var kp = Fen.Parse(Fen.Kiwipete);
        for (int d = 1; d <= 5; d++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ulong nodes = Perft.Run(kp, d);
            sw.Stop();
            System.Console.WriteLine($"kiwipete({d}) = {nodes,12}   {sw.Elapsed.TotalSeconds,7:F2}s");
        }
    }
}
```

Expected:

```
perft(1) =           20
perft(2) =          400
perft(3) =         8902
perft(4) =       197281
perft(5) =      4865609
perft(6) =    119060324

kiwipete(1) =          48
kiwipete(2) =        2039
kiwipete(3) =       97862
kiwipete(4) =     4085603
kiwipete(5) =   193690690
```

All matching the reference values from Part 1.

## Series recap

What we built across seven articles:

- **Part 1** — a perft skeleton and an ASCII FEN renderer.
- **Part 2** — a `Position` with 12 piece bitboards plus an 8×8 mailbox, full FEN parse and round-trip.
- **Part 3** — a 16-bit `Move` struct with UCI conversion.
- **Part 4** — a pseudo-legal generator with classical ray-scan sliders.
- **Part 5** — `MakeMove` / `UnmakeMove` with a stack of irreversible state, plugged into perft via a make-then-check filter.
- **Part 6** — magic bitboards replacing the slider attack functions.
- **Part 7** — proper legal generation via the checkers / pinned / king-danger trinity.

The result is a cross-platform .NET move generator that passes perft on the standard test positions. From here you could turn it into a real engine by adding alpha-beta search, quiescence, an evaluation function, and a UCI interface. None of that needs the move generator to change. The hardest part of any chess engine — getting move generation right — is done.
