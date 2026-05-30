using System.Text;

namespace MoveGen.App;

public static class PositionPrinter
{
    public static string Render(Position pos)
    {
        var sb = new StringBuilder();

        for (int rank = 7; rank >= 0; rank--)
        {
            sb.Append(rank + 1).Append(' ');
            for (int file = 0; file < 8; file++)
            {
                Piece p = pos.Squares[rank * 8 + file];
                sb.Append(p == Piece.None ? '.' : "PNBRQKpnbrqk"[(int)p]);
                sb.Append(' ');
            }
            sb.AppendLine();
        }
        sb.AppendLine("  a b c d e f g h");

        sb.Append("Side to move:   ").AppendLine(pos.WhiteToMove ? "white" : "black");
        sb.Append("Castling:       ").AppendLine(Fen.Write(pos).Split(' ')[2]);
        sb.Append("En passant:     ").AppendLine(pos.EpSquare < 0 ? "-" : Fen.SquareName(pos.EpSquare));
        sb.Append("Halfmove clock: ").AppendLine(pos.HalfmoveClock.ToString());
        sb.Append("Fullmove:       ").AppendLine(pos.FullmoveNumber.ToString());

        return sb.ToString();
    }
}
