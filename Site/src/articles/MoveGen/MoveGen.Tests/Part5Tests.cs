using System.Numerics;
using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class Part5Tests
{
    static int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    // === Standard perft test positions ===========================================
    const string Pos3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    const string Pos4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    const string Pos5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";

    [Theory]
    [InlineData(1, 20UL)]
    [InlineData(2, 400UL)]
    [InlineData(3, 8902UL)]
    [InlineData(4, 197281UL)]
    public void Perft_initial(int depth, ulong expected)
    {
        var pos = Fen.Parse(Fen.Initial);
        Assert.Equal(expected, Perft.Run(pos, depth));
    }

    [Theory]
    [InlineData(1, 48UL)]
    [InlineData(2, 2039UL)]
    [InlineData(3, 97862UL)]
    public void Perft_kiwipete(int depth, ulong expected)
    {
        var pos = Fen.Parse(Fen.Kiwipete);
        Assert.Equal(expected, Perft.Run(pos, depth));
    }

    [Theory]
    [InlineData(1, 14UL)]
    [InlineData(2, 191UL)]
    [InlineData(3, 2812UL)]
    [InlineData(4, 43238UL)]
    public void Perft_position_3(int depth, ulong expected)
    {
        var pos = Fen.Parse(Pos3);
        Assert.Equal(expected, Perft.Run(pos, depth));
    }

    [Theory]
    [InlineData(1, 6UL)]
    [InlineData(2, 264UL)]
    [InlineData(3, 9467UL)]
    public void Perft_position_4(int depth, ulong expected)
    {
        var pos = Fen.Parse(Pos4);
        Assert.Equal(expected, Perft.Run(pos, depth));
    }

    [Theory]
    [InlineData(1, 44UL)]
    [InlineData(2, 1486UL)]
    [InlineData(3, 62379UL)]
    public void Perft_position_5(int depth, ulong expected)
    {
        var pos = Fen.Parse(Pos5);
        Assert.Equal(expected, Perft.Run(pos, depth));
    }

    // === Make/unmake round-trip ==================================================

    [Fact]
    public void Make_then_unmake_restores_initial_position()
    {
        var pos = Fen.Parse(Fen.Initial);
        string before = Fen.Write(pos);

        // 1. e2-e4 (double push)
        pos.MakeMove(new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush));
        Assert.NotEqual(before, Fen.Write(pos));

        pos.UnmakeMove(new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush));
        Assert.Equal(before, Fen.Write(pos));
    }

    [Fact]
    public void Double_pawn_push_sets_ep_square()
    {
        var pos = Fen.Parse(Fen.Initial);
        pos.MakeMove(new Move(Sq("e2"), Sq("e4"), MoveFlag.DoublePawnPush));
        Assert.Equal(Sq("e3"), pos.EpSquare);
    }

    [Fact]
    public void Black_double_push_sets_ep_square()
    {
        var pos = Fen.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1");
        pos.MakeMove(new Move(Sq("e7"), Sq("e5"), MoveFlag.DoublePawnPush));
        Assert.Equal(Sq("e6"), pos.EpSquare);
    }

    [Fact]
    public void King_move_clears_castling_rights()
    {
        var pos = Fen.Parse("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        pos.MakeMove(new Move(Sq("e1"), Sq("e2"), MoveFlag.Quiet));
        Assert.Equal(0, (int)(pos.Castling & (CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside)));
        Assert.NotEqual((CastlingRights)0, pos.Castling & (CastlingRights.BlackKingside | CastlingRights.BlackQueenside));
    }

    [Fact]
    public void Rook_capture_on_corner_removes_opponent_castling_right()
    {
        // White rook on a1 captures black rook on a8.
        var pos = Fen.Parse("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        // We need a path; remove the a-file pieces between. The simpler test:
        var pos2 = Fen.Parse("r3k3/8/8/8/8/8/8/R3K3 w Qq - 0 1");
        pos2.MakeMove(new Move(Sq("a1"), Sq("a8"), MoveFlag.Capture));
        // Both queenside rights should now be gone — white's (its rook moved)
        // and black's (its rook captured).
        Assert.Equal(CastlingRights.None,
            pos2.Castling & (CastlingRights.WhiteQueenside | CastlingRights.BlackQueenside));
    }

    [Fact]
    public void Promotion_replaces_pawn_with_promoted_piece()
    {
        var pos = Fen.Parse("7k/4P3/8/8/8/8/8/4K3 w - - 0 1");
        pos.MakeMove(new Move(Sq("e7"), Sq("e8"), MoveFlag.PromoteQueen));
        Assert.Equal(Piece.WhiteQueen, pos.Squares[Sq("e8")]);
        Assert.Equal(0UL, pos.Pieces[(int)Piece.WhitePawn]);
    }

    [Fact]
    public void Promotion_unmake_restores_pawn()
    {
        var pos = Fen.Parse("7k/4P3/8/8/8/8/8/4K3 w - - 0 1");
        string before = Fen.Write(pos);
        var move = new Move(Sq("e7"), Sq("e8"), MoveFlag.PromoteQueen);
        pos.MakeMove(move);
        pos.UnmakeMove(move);
        Assert.Equal(before, Fen.Write(pos));
    }

    [Fact]
    public void Castling_moves_both_king_and_rook()
    {
        var pos = Fen.Parse("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        pos.MakeMove(new Move(Sq("e1"), Sq("g1"), MoveFlag.KingsideCastle));
        Assert.Equal(Piece.WhiteKing, pos.Squares[Sq("g1")]);
        Assert.Equal(Piece.WhiteRook, pos.Squares[Sq("f1")]);
        Assert.Equal(Piece.None,      pos.Squares[Sq("e1")]);
        Assert.Equal(Piece.None,      pos.Squares[Sq("h1")]);
    }

    [Fact]
    public void Castling_unmake_restores_pieces()
    {
        var pos = Fen.Parse("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        string before = Fen.Write(pos);
        var m = new Move(Sq("e1"), Sq("g1"), MoveFlag.KingsideCastle);
        pos.MakeMove(m);
        pos.UnmakeMove(m);
        Assert.Equal(before, Fen.Write(pos));
    }

    [Fact]
    public void En_passant_removes_captured_pawn_from_rank_behind()
    {
        // After 1.e4 d5 2.e5 f5 — white pawn e5, black pawn f5, ep target f6.
        var pos = Fen.Parse("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");
        pos.MakeMove(new Move(Sq("e5"), Sq("f6"), MoveFlag.EnPassant));
        Assert.Equal(Piece.WhitePawn, pos.Squares[Sq("f6")]);
        Assert.Equal(Piece.None,      pos.Squares[Sq("f5")]);   // captured pawn gone
        Assert.Equal(Piece.None,      pos.Squares[Sq("e5")]);
    }

    [Fact]
    public void En_passant_unmake_restores_both_pawns()
    {
        var pos = Fen.Parse("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");
        string before = Fen.Write(pos);
        var m = new Move(Sq("e5"), Sq("f6"), MoveFlag.EnPassant);
        pos.MakeMove(m);
        pos.UnmakeMove(m);
        Assert.Equal(before, Fen.Write(pos));
    }
}
