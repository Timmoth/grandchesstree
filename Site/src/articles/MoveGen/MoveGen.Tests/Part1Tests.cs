using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class Part1Tests
{
    const string InitialFen  = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    const string KiwipeteFen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -";

    [Fact]
    public void FenBoard_renders_initial_position_with_all_pieces()
    {
        string s = FenBoard.Render(InitialFen);
        Assert.Contains("r n b q k b n r", s); // 8th rank
        Assert.Contains("R N B Q K B N R", s); // 1st rank
        Assert.Contains("a b c d e f g h", s); // file labels
    }

    [Fact]
    public void FenBoard_renders_kiwipete_with_empty_squares_as_dots()
    {
        string s = FenBoard.Render(KiwipeteFen);
        Assert.Contains("r . . . k . . r", s);
        Assert.Contains("R . . . K . . R", s);
    }

    [Fact]
    public void Perft_at_depth_zero_returns_one()
    {
        Assert.Equal(1UL, Perft.Run(new Position(), 0));
    }
}
