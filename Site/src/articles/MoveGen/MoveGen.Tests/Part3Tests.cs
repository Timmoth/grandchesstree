using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class Part3Tests
{
    static int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    [Fact]
    public void From_to_and_flag_round_trip()
    {
        var m = new Move(Sq("a1"), Sq("h8"), MoveFlag.Capture);
        Assert.Equal(Sq("a1"), m.From);
        Assert.Equal(Sq("h8"), m.To);
        Assert.Equal(MoveFlag.Capture, m.Flag);
    }

    [Theory]
    [InlineData(MoveFlag.Capture,            true,  false, false, true)]
    [InlineData(MoveFlag.EnPassant,          true,  false, false, true)]
    [InlineData(MoveFlag.PromoCaptureQueen,  true,  true,  false, false)]
    [InlineData(MoveFlag.PromoCaptureKnight, true,  true,  false, false)]
    [InlineData(MoveFlag.PromoteQueen,       false, true,  false, false)]
    [InlineData(MoveFlag.PromoteKnight,      false, true,  false, false)]
    [InlineData(MoveFlag.KingsideCastle,     false, false, true,  false)]
    [InlineData(MoveFlag.QueensideCastle,    false, false, true,  false)]
    [InlineData(MoveFlag.Quiet,              false, false, false, false)]
    [InlineData(MoveFlag.DoublePawnPush,     false, false, false, false)]
    public void Predicates(MoveFlag flag, bool capture, bool promo, bool castle, bool epOnlyIfCapture)
    {
        var m = new Move(0, 1, flag);
        Assert.Equal(capture, m.IsCapture);
        Assert.Equal(promo,   m.IsPromotion);
        Assert.Equal(castle,  m.IsCastle);
        Assert.Equal(flag == MoveFlag.EnPassant, m.IsEnPassant);
        // ep is a capture
        if (flag == MoveFlag.EnPassant) Assert.True(epOnlyIfCapture && m.IsCapture);
    }

    [Theory]
    [InlineData(MoveFlag.PromoteKnight,        0)]
    [InlineData(MoveFlag.PromoteBishop,        1)]
    [InlineData(MoveFlag.PromoteRook,          2)]
    [InlineData(MoveFlag.PromoteQueen,         3)]
    [InlineData(MoveFlag.PromoCaptureKnight,   0)]
    [InlineData(MoveFlag.PromoCaptureBishop,   1)]
    [InlineData(MoveFlag.PromoCaptureRook,     2)]
    [InlineData(MoveFlag.PromoCaptureQueen,    3)]
    public void Promotion_piece_index(MoveFlag flag, int expected)
    {
        Assert.Equal(expected, new Move(0, 1, flag).PromotionPieceIndex);
    }

    [Fact]
    public void Uci_quiet_move()
    {
        Assert.Equal("e2e4", new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush).ToUci());
    }

    [Fact]
    public void Uci_castle_uses_king_squares_only()
    {
        Assert.Equal("e1g1", new Move(Sq("e1"), Sq("g1"), MoveFlag.KingsideCastle).ToUci());
    }

    [Theory]
    [InlineData(MoveFlag.PromoteKnight, "e7e8n")]
    [InlineData(MoveFlag.PromoteBishop, "e7e8b")]
    [InlineData(MoveFlag.PromoteRook,   "e7e8r")]
    [InlineData(MoveFlag.PromoteQueen,  "e7e8q")]
    public void Uci_promotion_appends_letter(MoveFlag flag, string expected)
    {
        Assert.Equal(expected, new Move(Sq("e7"), Sq("e8"), flag).ToUci());
    }

    [Fact]
    public void Equality_by_value()
    {
        var a = new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush);
        var b = new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush);
        var c = new Move(Sq("e2"), Sq("e4"), MoveFlag.Quiet);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
    }
}
