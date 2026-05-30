using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class ValidateTests
{
    [Fact]
    public void Validate_succeeds_on_freshly_parsed_position()
    {
        Fen.Parse(Fen.Initial).Validate();
        Fen.Parse(Fen.Kiwipete).Validate();
    }

    [Fact]
    public void Validate_succeeds_after_make_and_unmake()
    {
        var pos = Fen.Parse(Fen.Initial);
        int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

        var m = new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush);
        pos.MakeMove(m);
        pos.Validate();
        pos.UnmakeMove(m);
        pos.Validate();
    }

    [Fact]
    public void Validate_catches_bitboard_mailbox_mismatch()
    {
        var pos = Fen.Parse(Fen.Initial);
        // Deliberately corrupt the mailbox without touching the bitboards.
        pos.Squares[12] = Piece.None;     // claim e2 is empty
        Assert.Throws<System.InvalidOperationException>(() => pos.Validate());
    }

    [Fact]
    public void Validate_catches_overlapping_bitboards()
    {
        var pos = Fen.Parse(Fen.Initial);
        // Force the white-knight bitboard to also claim e2 (white pawn lives there).
        pos.Pieces[(int)Piece.WhiteKnight] |= 1UL << 12;
        Assert.Throws<System.InvalidOperationException>(() => pos.Validate());
    }

    [Fact]
    public void Validate_holds_after_every_move_in_perft_depth_3()
    {
        // Walk the legal move tree to depth 3 from Kiwipete and assert the
        // position is consistent before and after every MakeMove / UnmakeMove.
        var pos = Fen.Parse(Fen.Kiwipete);
        ValidateRecursive(pos, 3);
    }

    static void ValidateRecursive(Position pos, int depth)
    {
        pos.Validate();
        if (depth == 0) return;

        System.Span<Move> buf = stackalloc Move[256];
        int n = LegalMoveGenerator.Generate(pos, buf);
        for (int i = 0; i < n; i++)
        {
            pos.MakeMove(buf[i]);
            ValidateRecursive(pos, depth - 1);
            pos.UnmakeMove(buf[i]);
            pos.Validate();
        }
    }
}
