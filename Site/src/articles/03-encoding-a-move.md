# Move Generation, Part 3 — Encoding a Move

We've got a `Position` we can load from FEN and print back. Next we need a representation for the moves we'll generate from it. A move needs to carry enough information that, given a `Position`, we can both apply it and undo it without ambiguity.

Today we'll build a 16-bit `Move` struct and the UCI string helpers that go with it. Short article, mostly mechanical — but every line of code in Parts 4–7 will use this type.

## What a move needs to carry

Chess has more than one kind of move:

- A **quiet** move: piece moves to an empty square.
- A **capture**: piece moves to a square holding an enemy piece.
- A **double pawn push**: pawn from rank 2 (or 7) advances two squares. We need to flag it because it sets up the en-passant target.
- **Castling**: kingside and queenside, two variants. The rook moves too — but the move itself only records the king's from/to.
- An **en-passant capture**: looks like a pawn capture, but the captured pawn is *not* on the destination square — it's one rank behind.
- A **promotion**: pawn reaches the back rank and becomes one of four pieces.
- A **promotion capture**: a promotion *and* a capture in one move.

To distinguish all of those, plus carry from and to squares, we need:

- 6 bits for the **from** square (0..63).
- 6 bits for the **to** square (0..63).
- 4 bits for a **flag** that tells make/unmake what kind of move this is.

Total: 16 bits. Comfortably fits in a `ushort`.

## The flag table

The convention we'll use — borrowed straight from the chess-programming wiki — lets us pack the seven kinds of move (and the four promotion piece choices) into one nibble with structure we can exploit:

| Code | Promo | Cap | S1 | S0 | Meaning                  |
|-----:|:-----:|:---:|:--:|:--:|--------------------------|
|  0   |  0    |  0  |  0 |  0 | Quiet                    |
|  1   |  0    |  0  |  0 |  1 | Double pawn push         |
|  2   |  0    |  0  |  1 |  0 | Kingside castle          |
|  3   |  0    |  0  |  1 |  1 | Queenside castle         |
|  4   |  0    |  1  |  0 |  0 | Capture                  |
|  5   |  0    |  1  |  0 |  1 | En-passant capture       |
|  8   |  1    |  0  |  0 |  0 | Promotion to knight      |
|  9   |  1    |  0  |  0 |  1 | Promotion to bishop      |
| 10   |  1    |  0  |  1 |  0 | Promotion to rook        |
| 11   |  1    |  0  |  1 |  1 | Promotion to queen       |
| 12   |  1    |  1  |  0 |  0 | Promo-capture to knight  |
| 13   |  1    |  1  |  0 |  1 | Promo-capture to bishop  |
| 14   |  1    |  1  |  1 |  0 | Promo-capture to rook    |
| 15   |  1    |  1  |  1 |  1 | Promo-capture to queen   |

The structure: **bit 3 = "is promotion"**, **bit 2 = "is capture"**, **bits 1-0 = sub-type / promotion piece**. So once we have a flag, the predicates `IsCapture`, `IsPromotion`, and "which piece did we promote to?" are all one masked compare.

The promotion sub-codes follow the chess-conventional ordering — `00 = N, 01 = B, 10 = R, 11 = Q`. We'll lean on that in Part 4's move-emit loop: a single `for` over `i = 0..3` walks all four promotion variants by adding `i` to a base flag. Knight first because it's the lowest value; queen last because it's by far the most common choice and we'll want to score it highest in move ordering later.

## The `Move` struct

```csharp
using System;

public enum MoveFlag : byte
{
    Quiet                = 0,
    DoublePawnPush       = 1,
    KingsideCastle       = 2,
    QueensideCastle      = 3,
    Capture              = 4,
    EnPassant            = 5,
    PromoteKnight        = 8,
    PromoteBishop        = 9,
    PromoteRook          = 10,
    PromoteQueen         = 11,
    PromoCaptureKnight   = 12,
    PromoCaptureBishop   = 13,
    PromoCaptureRook     = 14,
    PromoCaptureQueen    = 15,
}

public readonly struct Move : IEquatable<Move>
{
    private readonly ushort _value;

    public Move(int from, int to, MoveFlag flag)
    {
        _value = (ushort)(((int)flag << 12) | ((from & 0x3F) << 6) | (to & 0x3F));
    }

    public int      To    =>             _value         & 0x3F;
    public int      From  =>            (_value >>  6)  & 0x3F;
    public MoveFlag Flag  => (MoveFlag)((_value >> 12)  & 0x0F);

    public bool IsCapture   => ((int)Flag & 0b0100) != 0;
    public bool IsPromotion => ((int)Flag & 0b1000) != 0;
    public bool IsEnPassant => Flag == MoveFlag.EnPassant;
    public bool IsCastle    => Flag == MoveFlag.KingsideCastle
                            || Flag == MoveFlag.QueensideCastle;

    /// 0=Knight, 1=Bishop, 2=Rook, 3=Queen. Only valid when IsPromotion.
    public int PromotionPieceIndex => (int)Flag & 0b0011;

    public bool Equals(Move other) => _value == other._value;
    public override bool Equals(object? o) => o is Move m && Equals(m);
    public override int GetHashCode() => _value;
    public static bool operator ==(Move a, Move b) => a._value == b._value;
    public static bool operator !=(Move a, Move b) => a._value != b._value;
}
```

`IsCapture` works for both straight captures *and* promo-captures *and* en-passant — they all set bit 2. `IsPromotion` works for both promotions and promo-captures — both set bit 3. This is the payoff for the carefully-chosen flag numbering.

The `& 0x3F` and `& 0x0F` masks aren't strictly necessary inside the constructor (legal callers pass values that fit), but they guard against the occasional sign-extended `int` slipping through and corrupting an adjacent field.

## UCI strings

UCI is the format engines use to exchange moves over `stdin`/`stdout`: from-square + to-square + an optional promotion piece letter. `e2e4`, `e1g1` (white kingside castle), `e7e8q` (queen promotion), `e5d6` (an en-passant capture — note we don't write `ep` anywhere; the position context tells the engine which it is).

Going from a `Move` to UCI is direct:

```csharp
using System.Text;

public static class MoveIo
{
    public static string ToUci(this Move m)
    {
        var sb = new StringBuilder(5);
        AppendSquare(sb, m.From);
        AppendSquare(sb, m.To);
        if (m.IsPromotion)
            sb.Append("nbrq"[m.PromotionPieceIndex]);
        return sb.ToString();
    }

    static void AppendSquare(StringBuilder sb, int sq)
    {
        sb.Append((char)('a' + (sq & 7)));
        sb.Append((char)('1' + (sq >> 3)));
    }
}
```

The other direction — UCI → `Move` — is trickier, because the UCI string alone doesn't say whether it's a capture, a castle, or an en-passant capture. To disambiguate we'd need the current `Position`. We'll defer that until Part 4 lands a generator: at that point we can parse a UCI string by generating all moves and finding the one whose `ToUci()` matches. Simple and correct.

## Verifying it works

Drop this into `Program.cs`:

```csharp
internal class Program
{
    static void Main()
    {
        int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

        Move pawnPush = new(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush);
        Move castle   = new(Sq("e1"), Sq("g1"), MoveFlag.KingsideCastle);
        Move promo    = new(Sq("e7"), Sq("e8"), MoveFlag.PromoteQueen);
        Move ep       = new(Sq("e5"), Sq("d6"), MoveFlag.EnPassant);

        foreach (var m in new[] { pawnPush, castle, promo, ep })
        {
            System.Console.WriteLine(
                $"{m.ToUci(),-5}  flag={m.Flag,-15} "
              + $"capture={m.IsCapture} promo={m.IsPromotion} "
              + $"castle={m.IsCastle} ep={m.IsEnPassant}");
        }

        // Round-trip sanity: pack and unpack
        Move m2 = new(Sq("a1"), Sq("h8"), MoveFlag.Capture);
        System.Diagnostics.Debug.Assert(m2.From == Sq("a1"));
        System.Diagnostics.Debug.Assert(m2.To   == Sq("h8"));
        System.Diagnostics.Debug.Assert(m2.IsCapture);
    }
}
```

Output:

```
e2e4   flag=DoublePawnPush   capture=False promo=False castle=False ep=False
e1g1   flag=KingsideCastle   capture=False promo=False castle=True  ep=False
e7e8q  flag=PromoteQueen     capture=False promo=True  castle=False ep=False
e5d6   flag=EnPassant        capture=True  promo=False castle=False ep=True
```

The flag predicates all agree with what we'd expect. The promotion move correctly appends `q` to its UCI string. The en-passant move reports `IsCapture` even though no piece sits on `d6` — that's exactly the property we'll lean on in Part 5 when MakeMove handles the irregular capture target.

## Recap

- A move is three pieces of information: from square, to square, kind.
- We pack them into 16 bits: `flag << 12 | from << 6 | to`.
- The flag's bit pattern makes `IsCapture` and `IsPromotion` cheap masked compares.
- UCI is from + to + optional promotion letter.

## What's next

Part 4 is the heart of the series: we'll build a **pseudo-legal move generator**. Given a `Position`, it'll hand back every move that follows the rules of piece movement — pawns push, knights hop, sliders slide — without yet checking whether each move leaves the king in check. That's enough to make the perft skeleton from Part 1 actually do something — at depth 1 from the initial position we should count exactly 20 moves.
