namespace MoveGen.App;

public static class Perft
{
    public static ulong Run(Position pos, int depth)
    {
        if (depth == 0) return 1UL;

        Span<Move> buf = stackalloc Move[256];
        int n = LegalMoveGenerator.Generate(pos, buf);

        if (depth == 1) return (ulong)n;

        ulong nodes = 0;
        for (int i = 0; i < n; i++)
        {
            pos.MakeMove(buf[i]);
            nodes += Run(pos, depth - 1);
            pos.UnmakeMove(buf[i]);
        }
        return nodes;
    }
}
