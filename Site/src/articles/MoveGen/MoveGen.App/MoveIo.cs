using System.Text;

namespace MoveGen.App;

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
