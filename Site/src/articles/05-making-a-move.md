# Move Generation, Part 5 — Making a Move

We've got a generator that produces every pseudo-legal move. To use it — for perft, for search, for anything — we need to **apply** each move and then **reverse** it so the next sibling move starts from the same position.

Two operations: `MakeMove` and `UnmakeMove`. Plus a small undo record, because some of the state we mutate can't be reconstructed from the move alone.

By the end of this article we'll wire the generator and make/unmake into the perft skeleton from Part 1, filter out moves that leave our king in check, and get **correct** perft counts matching the reference values from the initial position and from Kiwipete.

## What needs to be saved before make

The position has two kinds of state:

- **Reversible** — piece bitboards, the mailbox, aggregate occupancy. These are pure functions of "which pieces are on which squares", and XOR-ing the same bits twice in a row gets us back where we started. Make and unmake do mirror operations.
- **Irreversible** — castling rights, en-passant square, halfmove clock, captured piece, side to move. These can't be derived from the new position. If we don't save them before mutating, we can't restore them.

So make-move needs an **undo record** holding the irreversible state from *before* the move:

```csharp
public struct UndoInfo
{
    public CastlingRights Castling;
    public int            EpSquare;
    public int            HalfmoveClock;
    public Piece          Captured;     // Piece.None for non-captures
}
```

We'll keep a stack of these inside `Position` so the search can nest as deeply as it likes. Position is now big enough that splitting it across two files is tidier — make `Position.cs` a partial class and add a new `PositionMake.cs` file for the make/unmake methods:

```csharp
// Position.cs — add the partial keyword, the undo-stack fields, and the static
// castling-rights mask we'll use to update rights on every move.
public sealed partial class Position
{
    // ... (existing fields from Part 2) ...

    internal readonly UndoInfo[] _undoStack = new UndoInfo[1024];
    internal int _undoTop;
}
```

`1024` is fine — we'll never search a tree that deep in any realistic position. (If you do, you've got bigger problems.)

## Make: the basic shape

For most moves the steps are:

1. Save the irreversible state into the undo stack.
2. Update castling rights (lose the right if king or relevant rook moved).
3. Update the half-move clock (reset on pawn moves or captures; increment otherwise).
4. Move the piece: XOR the from-square and to-square on the moving piece's bitboard. Update the mailbox.
5. If capturing: remove the captured piece from its bitboard. Update the mailbox.
6. Update the en-passant square (set on double pawn push, clear otherwise).
7. Flip side to move; if black just moved, increment fullmove number.

Then there are four special cases — castling, en-passant, promotion, double push — that tweak the basic pattern.

One rule we'll stick to: **all the piece-bitboard mutation happens before we flip `WhiteToMove`.** Capture the current side-to-move into a local `whiteMoving` at the top of the function and use that consistently. The mental model is simple — `whiteMoving` is the side that's *making* the move, and once we're done mutating, `WhiteToMove` flips to the opponent at the very end.

Set up the skeleton in `PositionMake.cs`:

```csharp
using System.Numerics;

public sealed partial class Position
{
    public void MakeMove(Move move)
    {
        int from = move.From;
        int to   = move.To;
        Piece moving = Squares[from];

        bool whiteMoving = WhiteToMove;     // the side doing the move

        Piece captured = move.IsEnPassant
            ? (whiteMoving ? Piece.BlackPawn : Piece.WhitePawn)
            : Squares[to];

        // 1. Save irreversible state
        _undoStack[_undoTop++] = new UndoInfo
        {
            Castling      = Castling,
            EpSquare      = EpSquare,
            HalfmoveClock = HalfmoveClock,
            Captured      = captured,
        };

        // 2. Update castling rights based on from/to squares
        Castling &= CastlingRightsMask[from] & CastlingRightsMask[to];

        // 3. Halfmove clock
        if (moving == Piece.WhitePawn || moving == Piece.BlackPawn || move.IsCapture)
            HalfmoveClock = 0;
        else
            HalfmoveClock++;

        // 4. Move the piece
        ulong fromTo = (1UL << from) | (1UL << to);
        Pieces[(int)moving] ^= fromTo;
        Squares[from] = Piece.None;
        Squares[to]   = moving;

        // 5. Capture (default case — special-cased below for ep / promo-capture)
        if (move.IsCapture && !move.IsEnPassant)
        {
            Pieces[(int)captured] ^= 1UL << to;
            // Squares[to] is already overwritten with the moving piece.
        }

        ApplySpecialMoves(move, to, captured, whiteMoving);

        // 6. EP square — set on double push only
        EpSquare = move.Flag == MoveFlag.DoublePawnPush
            ? (whiteMoving ? to - 8 : to + 8)
            : -1;

        // 7. Side / fullmove — the flip is the last thing we touch
        if (!whiteMoving) FullmoveNumber++;
        WhiteToMove = !whiteMoving;

        RebuildOccupancy();
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
}
```

`CastlingRightsMask` is a 64-entry table whose value at each square tells us which rights to *preserve* when a piece moves to or from that square. Almost every entry is `~0` (no change); only the king's and rooks' starting squares clear their own rights. Put it in `Position.cs` alongside the other fields:

```csharp
public sealed partial class Position
{
    static readonly CastlingRights[] CastlingRightsMask = BuildCastlingMask();

    static CastlingRights[] BuildCastlingMask()
    {
        var m = new CastlingRights[64];
        for (int i = 0; i < 64; i++)
            m[i] = (CastlingRights)0xF;   // all bits set = no change

        // White rooks
        m[0]  &= ~CastlingRights.WhiteQueenside;   // a1
        m[7]  &= ~CastlingRights.WhiteKingside;    // h1
        // White king
        m[4]  &= ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside);
        // Black rooks
        m[56] &= ~CastlingRights.BlackQueenside;   // a8
        m[63] &= ~CastlingRights.BlackKingside;    // h8
        // Black king
        m[60] &= ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside);

        return m;
    }
}
```

We AND with the mask for both the from-square *and* the to-square. The to-square check is the bit people miss — if our rook captures the enemy's rook on its home corner, the enemy lost that castling right too.

## Special-move handling

Because special-moves run *before* the side flip, `whiteMoving` is straightforwardly the side doing the move. No mental gymnastics — if `whiteMoving == true`, we're moving white pieces.

```csharp
void ApplySpecialMoves(Move move, int to, Piece captured, bool whiteMoving)
{
    switch (move.Flag)
    {
        case MoveFlag.EnPassant:
        {
            // Captured pawn is one rank behind the to-square (from the mover's POV).
            int capSq = whiteMoving ? to - 8 : to + 8;
            Pieces[(int)captured] ^= 1UL << capSq;
            Squares[capSq] = Piece.None;
            break;
        }
        case MoveFlag.KingsideCastle:
            if (whiteMoving) MoveRookForCastle(7, 5);     // white  h1 → f1
            else             MoveRookForCastle(63, 61);   // black  h8 → f8
            break;

        case MoveFlag.QueensideCastle:
            if (whiteMoving) MoveRookForCastle(0, 3);     // white  a1 → d1
            else             MoveRookForCastle(56, 59);   // black  a8 → d8
            break;

        default:
            if (move.IsPromotion)
            {
                // The pawn was placed on `to` in step 4. Replace it with the promo piece.
                Piece pawn  = whiteMoving ? Piece.WhitePawn : Piece.BlackPawn;
                int colorOffset = whiteMoving ? 0 : 6;       // 0 = white pieces, 6 = black
                Piece promo = (Piece)(colorOffset + 1 + move.PromotionPieceIndex);
                Pieces[(int)pawn]  ^= 1UL << to;
                Pieces[(int)promo] ^= 1UL << to;
                Squares[to] = promo;
            }
            break;
    }
}

void MoveRookForCastle(int rookFrom, int rookTo)
{
    Piece rook = Squares[rookFrom];
    ulong fromTo = (1UL << rookFrom) | (1UL << rookTo);
    Pieces[(int)rook] ^= fromTo;
    Squares[rookFrom] = Piece.None;
    Squares[rookTo]   = rook;
}
```

Promotion piece arithmetic: piece codes are laid out `WhitePawn=0, WhiteKnight=1, ..., WhiteKing=5, BlackPawn=6, BlackKnight=7, ..., BlackKing=11`, so `(colorOffset + 1 + PromotionPieceIndex)` produces the right code for both colours when `PromotionPieceIndex` is `0=N, 1=B, 2=R, 3=Q`.

## Unmake

Unmake is the mirror. We flip the side back *first* so `whiteMoving` again represents the side that originally made this move, then walk back through what MakeMove did:

```csharp
public void UnmakeMove(Move move)
{
    int from = move.From;
    int to   = move.To;

    // Flip side back first — symmetric to make's "flip last".
    WhiteToMove = !WhiteToMove;
    if (!WhiteToMove) FullmoveNumber--;
    bool whiteMoving = WhiteToMove;

    UndoInfo u = _undoStack[--_undoTop];
    Castling      = u.Castling;
    EpSquare      = u.EpSquare;
    HalfmoveClock = u.HalfmoveClock;

    // If we promoted, swap the promoted piece back to a pawn so we can move
    // it back as a pawn.
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

    // Restore captured piece.
    if (u.Captured != Piece.None)
    {
        int capSq = move.IsEnPassant
            ? (whiteMoving ? to - 8 : to + 8)
            : to;
        Pieces[(int)u.Captured] ^= 1UL << capSq;
        Squares[capSq] = u.Captured;
    }

    // Undo the castling rook move.
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
```

## The make / unmake invariant table

Every mutation MakeMove performs must have a matching mutation that UnmakeMove undoes. Easy to lose track of as the function grows — here's the checklist:

| Field                  | Make does                                  | Unmake does                                  |
|------------------------|--------------------------------------------|----------------------------------------------|
| `_undoStack`           | Push `UndoInfo` (castling, ep, clock, captured) | Pop |
| `Castling`             | AND with `Mask[from] & Mask[to]`           | Restore from undo                            |
| `EpSquare`             | Set on double push, else clear             | Restore from undo                            |
| `HalfmoveClock`        | Reset on pawn-move / capture, else ++      | Restore from undo                            |
| `FullmoveNumber`       | `++` if black moved                        | `--` if black moved                          |
| `WhiteToMove`          | XOR (flip)                                 | XOR (flip back) — first thing                |
| `Pieces[moving]`       | XOR `fromTo`                               | XOR `fromTo`                                 |
| `Squares[from]`        | Set to `None`                              | Set back to mover                            |
| `Squares[to]`          | Set to mover                               | Set to `None` (or captured / promoted-piece) |
| Capture                | XOR captured bit off enemy's bitboard      | XOR it back on                               |
| Castling rook          | Move (XOR + mailbox)                       | Move back (XOR + mailbox)                    |
| EP captured pawn       | Remove from rank behind                    | Restore                                      |
| Promotion              | Swap pawn → promoted piece on `to`         | Swap promoted piece → pawn                   |
| `WhiteOccupied`, etc.  | `RebuildOccupancy`                         | `RebuildOccupancy`                           |

Every row must be a true match — if one side adds a mutation and the other doesn't undo it, perft will diverge from the reference. The validator we're about to introduce makes mismatches show up at the first faulty move rather than as a wrong total at the end.

## A validator

Part 2 mentioned this as a future addition — here's the moment to deliver. After every `MakeMove` (and `UnmakeMove`) the position should be **internally consistent**: every set bit in `Pieces[p]` matches `Squares[sq] == p`; piece bitboards don't overlap; `WhiteOccupied / BlackOccupied / AllOccupied` are the recomputed unions. A short helper that asserts all three is cheap enough to call in test mode after every move:

```csharp
public sealed partial class Position
{
    public void Validate()
    {
        ulong w = 0, b = 0;
        for (int p = 0; p < 12; p++)
        {
            ulong pieces = Pieces[p];

            // No square is claimed by two piece bitboards at once.
            for (int q = p + 1; q < 12; q++)
                if ((pieces & Pieces[q]) != 0)
                    throw new InvalidOperationException(
                        $"Bitboards {(Piece)p} and {(Piece)q} overlap");

            // Every set bit must match the mailbox.
            ulong bb = pieces;
            while (bb != 0)
            {
                int sq = BitOperations.TrailingZeroCount(bb);
                if ((int)Squares[sq] != p)
                    throw new InvalidOperationException(
                        $"Bitboard says {(Piece)p} on {sq} but mailbox says {Squares[sq]}");
                bb &= bb - 1;
            }

            if (p < 6) w |= pieces; else b |= pieces;
        }

        // Mailbox → bitboards.
        for (int sq = 0; sq < 64; sq++)
        {
            Piece m = Squares[sq];
            if (m == Piece.None)
            {
                if (((w | b) & (1UL << sq)) != 0)
                    throw new InvalidOperationException($"Mailbox says empty on {sq} but bitboards say occupied");
            }
            else if ((Pieces[(int)m] & (1UL << sq)) == 0)
            {
                throw new InvalidOperationException($"Mailbox has {m} on {sq} but its bitboard doesn't");
            }
        }

        if (WhiteOccupied != w) throw new InvalidOperationException("WhiteOccupied out of sync");
        if (BlackOccupied != b) throw new InvalidOperationException("BlackOccupied out of sync");
        if (AllOccupied != (w | b)) throw new InvalidOperationException("AllOccupied out of sync");
    }
}
```

Wrap a perft run in calls to it — `pos.Validate()` before and after every `MakeMove`/`UnmakeMove` pair — and any state-sync bug will throw at the move that introduces it, not 20 plies later when the totals diverge.

## Plugging into perft

Now we have everything. The perft skeleton from Part 1 needs only one change — filter out pseudo-legal moves that leave our king in check:

```csharp
using System.Numerics;

public static class Perft
{
    public static ulong Run(Position pos, int depth)
    {
        if (depth == 0) return 1UL;

        Span<Move> buf = stackalloc Move[256];
        int n = MoveGenerator.GeneratePseudoLegal(pos, buf);

        ulong nodes = 0;
        for (int i = 0; i < n; i++)
        {
            Move m = buf[i];
            pos.MakeMove(m);

            // Was our king (the side that just moved) left in check? If so, illegal.
            int ourKingSq = BitOperations.TrailingZeroCount(
                pos.WhiteToMove
                    ? pos.Pieces[(int)Piece.BlackKing]   // we = black if it's now white's turn
                    : pos.Pieces[(int)Piece.WhiteKing]);

            int opponent = pos.WhiteToMove ? 0 : 1;       // colour to move = who would attack us
            bool stillLegal = !Attacks.IsAttackedBy(pos, ourKingSq, opponent);

            if (stillLegal)
                nodes += Run(pos, depth - 1);

            pos.UnmakeMove(m);
        }

        return nodes;
    }
}
```

The "our king" lookup is `pos.WhiteToMove ? BlackKing : WhiteKing` — because the side to move *after* `MakeMove` is the opponent of the side that just moved.

There's a second consideration for castling: we let pseudo-legal castling through without checking whether the king passes through an attacked square or whether the king was in check to start with. The "still legal" test after `MakeMove` catches "left king in check" but not "king passed through an attacked square". For now we'll add an explicit guard:

```csharp
// Inside the perft loop, before MakeMove:
if (m.IsCastle && !IsCastleLegal(pos, m))
    continue;
```

```csharp
static bool IsCastleLegal(Position pos, Move m)
{
    int kingFrom = m.From;
    int kingTo   = m.To;
    int transit  = (kingFrom + kingTo) / 2;
    int opponent = pos.WhiteToMove ? 1 : 0;

    return !Attacks.IsAttackedBy(pos, kingFrom, opponent)
        && !Attacks.IsAttackedBy(pos, transit,  opponent)
        && !Attacks.IsAttackedBy(pos, kingTo,   opponent);
}
```

Part 7 will fold this back into the move generator itself, but for now it's an honest belt-and-braces filter that keeps perft correct.

## Running it

```csharp
internal class Program
{
    static void Main()
    {
        var pos = Fen.Parse(Fen.Initial);
        for (int d = 1; d <= 5; d++)
            System.Console.WriteLine($"perft({d}) = {Perft.Run(pos, d)}");

        System.Console.WriteLine();

        var kp = Fen.Parse(Fen.Kiwipete);
        for (int d = 1; d <= 4; d++)
            System.Console.WriteLine($"kiwipete perft({d}) = {Perft.Run(kp, d)}");
    }
}
```

Expected output:

```
perft(1) = 20
perft(2) = 400
perft(3) = 8902
perft(4) = 197281
perft(5) = 4865609

kiwipete perft(1) = 48
kiwipete perft(2) = 2039
kiwipete perft(3) = 97862
kiwipete perft(4) = 4085603
```

If those match — congratulations, our move generator is *correct*. Slow, but correct. Depth 5 on the initial position will take a few seconds; Kiwipete depth 4 will take longer still. The sliders and the make-then-test filter are doing far more work than necessary, and that's what Parts 6 and 7 will fix.

If they *don't* match: divide-and-conquer. Drop in a "divide" routine that prints node counts per root move and compare against any open reference engine (Stockfish has a `go perft N` command). The first root move whose subtotal disagrees is the one to drill into.

```csharp
public static void Divide(Position pos, int depth)
{
    Span<Move> buf = stackalloc Move[256];
    int n = MoveGenerator.GeneratePseudoLegal(pos, buf);
    ulong total = 0;
    for (int i = 0; i < n; i++)
    {
        Move m = buf[i];
        if (m.IsCastle && !IsCastleLegal(pos, m)) continue;
        pos.MakeMove(m);

        int ksq = BitOperations.TrailingZeroCount(
            pos.WhiteToMove ? pos.Pieces[(int)Piece.BlackKing] : pos.Pieces[(int)Piece.WhiteKing]);
        int opp = pos.WhiteToMove ? 0 : 1;
        if (!Attacks.IsAttackedBy(pos, ksq, opp))
        {
            ulong sub = depth == 1 ? 1 : Run(pos, depth - 1);
            total += sub;
            System.Console.WriteLine($"{m.ToUci()}: {sub}");
        }
        pos.UnmakeMove(m);
    }
    System.Console.WriteLine($"Total: {total}");
}
```

## What's next

Part 6: **magic bitboards**. The classical ray-scan is the slowest part of our generator — four bitscans and four conditional XORs per slider per square. Magic bitboards replace that with a single `mul + shift + lookup`, in exchange for ~840 KiB of attack tables and a one-time startup cost to find the magic constants. Perft numbers stay the same; runtime gets several times shorter.
