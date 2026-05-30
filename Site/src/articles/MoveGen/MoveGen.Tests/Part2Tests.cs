using System.Numerics;
using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class Part2Tests
{
    [Fact]
    public void Parse_initial_position_has_32_pieces()
    {
        var pos = Fen.Parse(Fen.Initial);
        Assert.Equal(32, BitOperations.PopCount(pos.AllOccupied));
        Assert.Equal(16, BitOperations.PopCount(pos.WhiteOccupied));
        Assert.Equal(16, BitOperations.PopCount(pos.BlackOccupied));
        Assert.Equal(0UL, pos.WhiteOccupied & pos.BlackOccupied);
    }

    [Fact]
    public void Parse_initial_position_white_king_on_e1()
    {
        var pos = Fen.Parse(Fen.Initial);
        // e1 = file 4 + rank 0 * 8 = 4
        Assert.Equal(Piece.WhiteKing, pos.Squares[4]);
        Assert.Equal(1UL << 4, pos.Pieces[(int)Piece.WhiteKing]);
    }

    [Fact]
    public void Parse_initial_position_black_king_on_e8()
    {
        var pos = Fen.Parse(Fen.Initial);
        // e8 = file 4 + rank 7 * 8 = 60
        Assert.Equal(Piece.BlackKing, pos.Squares[60]);
        Assert.Equal(1UL << 60, pos.Pieces[(int)Piece.BlackKing]);
    }

    [Fact]
    public void Parse_initial_position_white_pawns_on_rank_2()
    {
        var pos = Fen.Parse(Fen.Initial);
        Assert.Equal(0x000000000000FF00UL, pos.Pieces[(int)Piece.WhitePawn]);
        Assert.Equal(0x00FF000000000000UL, pos.Pieces[(int)Piece.BlackPawn]);
    }

    [Fact]
    public void Parse_initial_position_state_fields()
    {
        var pos = Fen.Parse(Fen.Initial);
        Assert.True(pos.WhiteToMove);
        Assert.Equal(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside |
                     CastlingRights.BlackKingside | CastlingRights.BlackQueenside, pos.Castling);
        Assert.Equal(-1, pos.EpSquare);
        Assert.Equal(0, pos.HalfmoveClock);
        Assert.Equal(1, pos.FullmoveNumber);
    }

    [Fact]
    public void Parse_kiwipete_loads_state_with_missing_trailing_fields()
    {
        var pos = Fen.Parse(Fen.Kiwipete);
        Assert.True(pos.WhiteToMove);
        Assert.Equal(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside |
                     CastlingRights.BlackKingside | CastlingRights.BlackQueenside, pos.Castling);
        Assert.Equal(-1, pos.EpSquare);
        Assert.Equal(0, pos.HalfmoveClock);
        Assert.Equal(1, pos.FullmoveNumber);
    }

    [Fact]
    public void Fen_round_trips_initial_position()
    {
        var pos = Fen.Parse(Fen.Initial);
        Assert.Equal(Fen.Initial, Fen.Write(pos));
    }

    [Fact]
    public void Fen_round_trips_kiwipete_with_explicit_clocks()
    {
        var pos = Fen.Parse(Fen.Kiwipete);
        // Input omits halfmove/fullmove; round-trip fills them in.
        Assert.Equal(Fen.Kiwipete + " 0 1", Fen.Write(pos));
    }

    [Fact]
    public void Fen_parses_en_passant_square()
    {
        var pos = Fen.Parse("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2");
        // e6 = file 4 + rank 5 * 8 = 44
        Assert.Equal(44, pos.EpSquare);
    }

    [Fact]
    public void Mailbox_and_bitboards_agree_on_kiwipete()
    {
        var pos = Fen.Parse(Fen.Kiwipete);
        for (int sq = 0; sq < 64; sq++)
        {
            Piece m = pos.Squares[sq];
            if (m == Piece.None)
            {
                Assert.Equal(0UL, pos.AllOccupied & (1UL << sq));
            }
            else
            {
                Assert.NotEqual(0UL, pos.Pieces[(int)m] & (1UL << sq));
                Assert.NotEqual(0UL, pos.AllOccupied & (1UL << sq));
            }
        }
    }
}
