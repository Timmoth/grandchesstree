using System.Numerics;
using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class Part4Tests
{
    static int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    [Fact]
    public void Knight_in_center_attacks_eight_squares()
    {
        Assert.Equal(8, BitOperations.PopCount(Attacks.Knight[Sq("d4")]));
    }

    [Fact]
    public void Knight_on_corner_attacks_two_squares()
    {
        Assert.Equal(2, BitOperations.PopCount(Attacks.Knight[Sq("a1")]));
    }

    [Fact]
    public void King_in_center_attacks_eight_squares()
    {
        Assert.Equal(8, BitOperations.PopCount(Attacks.King[Sq("e4")]));
    }

    [Fact]
    public void King_in_corner_attacks_three_squares()
    {
        Assert.Equal(3, BitOperations.PopCount(Attacks.King[Sq("a1")]));
    }

    [Fact]
    public void White_pawn_attacks_two_diagonals()
    {
        ulong attacks = Attacks.Pawn[0, Sq("e2")];
        Assert.Equal((1UL << Sq("d3")) | (1UL << Sq("f3")), attacks);
    }

    [Fact]
    public void Black_pawn_attacks_two_diagonals()
    {
        ulong attacks = Attacks.Pawn[1, Sq("e7")];
        Assert.Equal((1UL << Sq("d6")) | (1UL << Sq("f6")), attacks);
    }

    [Fact]
    public void A_file_pawn_attacks_one_square()
    {
        Assert.Equal(1, BitOperations.PopCount(Attacks.Pawn[0, Sq("a2")])); // attacks b3
        Assert.Equal(1, BitOperations.PopCount(Attacks.Pawn[1, Sq("h7")])); // attacks g6
    }

    [Fact]
    public void Rook_on_empty_board_attacks_14_squares()
    {
        Assert.Equal(14, BitOperations.PopCount(Attacks.RookAttacks(Sq("d4"), 0UL)));
    }

    [Fact]
    public void Bishop_on_empty_board_centre_attacks_13_squares()
    {
        Assert.Equal(13, BitOperations.PopCount(Attacks.BishopAttacks(Sq("d4"), 0UL)));
    }

    [Fact]
    public void Queen_on_empty_board_centre_attacks_27_squares()
    {
        Assert.Equal(27, BitOperations.PopCount(Attacks.QueenAttacks(Sq("d4"), 0UL)));
    }

    [Fact]
    public void Rook_blocked_by_own_piece_includes_blocker()
    {
        // Rook on a1, blocker on a4. Attack set: a2, a3, a4 + entire 1st rank b1..h1.
        ulong occ = (1UL << Sq("a1")) | (1UL << Sq("a4"));
        ulong attacks = Attacks.RookAttacks(Sq("a1"), occ);
        Assert.NotEqual(0UL, attacks & (1UL << Sq("a4")));
        Assert.Equal   (0UL, attacks & (1UL << Sq("a5")));   // past blocker excluded
    }

    [Fact]
    public void Initial_position_has_20_pseudo_legal_moves()
    {
        var pos = Fen.Parse(Fen.Initial);
        Span<Move> buf = stackalloc Move[256];
        Assert.Equal(20, MoveGenerator.GeneratePseudoLegal(pos, buf));
    }

    [Fact]
    public void Kiwipete_has_48_pseudo_legal_moves()
    {
        var pos = Fen.Parse(Fen.Kiwipete);
        Span<Move> buf = stackalloc Move[256];
        Assert.Equal(48, MoveGenerator.GeneratePseudoLegal(pos, buf));
    }

    [Fact]
    public void Initial_position_for_black_also_has_20_moves()
    {
        // Same shape mirrored.
        var pos = Fen.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1");
        Span<Move> buf = stackalloc Move[256];
        Assert.Equal(20, MoveGenerator.GeneratePseudoLegal(pos, buf));
    }

    [Fact]
    public void Pawn_promotion_emits_four_moves_per_target()
    {
        // White pawn on e7 with e8 empty (black king parked out of the way).
        var pos = Fen.Parse("7k/4P3/8/8/8/8/8/4K3 w - - 0 1");
        Span<Move> buf = stackalloc Move[256];
        int n = MoveGenerator.GeneratePseudoLegal(pos, buf);

        int promos = 0;
        for (int i = 0; i < n; i++)
            if (buf[i].IsPromotion && buf[i].From == Sq("e7") && buf[i].To == Sq("e8"))
                promos++;
        Assert.Equal(4, promos);
    }

    [Fact]
    public void En_passant_target_appears_in_move_list()
    {
        // After 1.e4 d5 2.e5 f5, white can capture en passant on f6.
        var pos = Fen.Parse("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");
        Span<Move> buf = stackalloc Move[256];
        int n = MoveGenerator.GeneratePseudoLegal(pos, buf);

        bool found = false;
        for (int i = 0; i < n; i++)
            if (buf[i].IsEnPassant && buf[i].From == Sq("e5") && buf[i].To == Sq("f6"))
                found = true;
        Assert.True(found, "Expected en-passant capture e5xf6");
    }

    [Fact]
    public void Castling_moves_appear_when_squares_empty_and_rights_held()
    {
        // Position with white castling kingside available, squares clear.
        var pos = Fen.Parse("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");
        Span<Move> buf = stackalloc Move[256];
        int n = MoveGenerator.GeneratePseudoLegal(pos, buf);

        bool ks = false, qs = false;
        for (int i = 0; i < n; i++)
        {
            if (buf[i].Flag == MoveFlag.KingsideCastle  && buf[i].From == 4 && buf[i].To == 6) ks = true;
            if (buf[i].Flag == MoveFlag.QueensideCastle && buf[i].From == 4 && buf[i].To == 2) qs = true;
        }
        Assert.True(ks);
        Assert.True(qs);
    }

    [Fact]
    public void IsAttackedBy_detects_check_from_rook()
    {
        // Black rook on e8, white king on e1, e-file otherwise empty.
        var pos = Fen.Parse("4r3/8/8/8/8/8/8/4K3 w - - 0 1");
        Assert.True(Attacks.IsAttackedBy(pos, 4, byColor: 1));  // 4 = e1, black attacking
    }

    [Fact]
    public void IsAttackedBy_blocked_by_friendly_piece()
    {
        // Black rook on e8, black pawn on e4 blocks attack to e1.
        var pos = Fen.Parse("4r3/8/8/8/4p3/8/8/4K3 w - - 0 1");
        Assert.False(Attacks.IsAttackedBy(pos, 4, byColor: 1));
    }
}
