using System.Text;

namespace MoveGen.App;

public static class FenBoard
{
    public static string Render(string fen)
    {
        string placement = fen.Split(' ')[0];
        string[] ranks   = placement.Split('/'); // rank 8 first, rank 1 last

        var sb = new StringBuilder();
        for (int r = 0; r < 8; r++)
        {
            sb.Append(8 - r).Append(' ');
            foreach (char c in ranks[r])
            {
                if (char.IsDigit(c))
                {
                    int empties = c - '0';
                    for (int i = 0; i < empties; i++) sb.Append(". ");
                }
                else
                {
                    sb.Append(c).Append(' ');
                }
            }
            sb.AppendLine();
        }
        sb.Append("  a b c d e f g h");
        return sb.ToString();
    }
}
