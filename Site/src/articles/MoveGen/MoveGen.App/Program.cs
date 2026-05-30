using MoveGen.App;
using System.Diagnostics;

if (args.Length > 0 && args[0] == "--demo")
{
    RunDemo();
    return;
}

// Default: UCI mode.
Magic.Init();
var pos = Fen.Parse(Fen.Initial);

string? line;
while ((line = Console.ReadLine()) != null)
{
    line = line.Trim();
    if (line.Length == 0) continue;

    if (line == "uci")
    {
        Console.WriteLine("id name MoveGen 0.1");
        Console.WriteLine("id author articles/move-generation");
        Console.WriteLine("uciok");
    }
    else if (line == "isready")
    {
        Console.WriteLine("readyok");
    }
    else if (line == "ucinewgame")
    {
        pos = Fen.Parse(Fen.Initial);
    }
    else if (line.StartsWith("position "))
    {
        HandlePosition(line.Substring("position ".Length), ref pos);
    }
    else if (line.StartsWith("go perft "))
    {
        if (int.TryParse(line.AsSpan("go perft ".Length), out int depth))
        {
            ulong nodes = Perft.Run(pos, depth);
            Console.WriteLine($"Nodes searched: {nodes}");
        }
    }
    else if (line == "quit")
    {
        break;
    }
    // Unknown commands silently ignored — UCI engines must tolerate junk.
}

static void HandlePosition(string s, ref Position pos)
{
    // "startpos [moves ...]" or "fen <fen> [moves ...]"
    if (s.StartsWith("startpos"))
    {
        pos = Fen.Parse(Fen.Initial);
        int mi = s.IndexOf("moves", StringComparison.Ordinal);
        if (mi >= 0) ApplyMoves(pos, s.Substring(mi + 5).Trim());
    }
    else if (s.StartsWith("fen "))
    {
        string rest = s.Substring(4);
        int mi = rest.IndexOf(" moves ", StringComparison.Ordinal);
        if (mi >= 0)
        {
            pos = Fen.Parse(rest.Substring(0, mi));
            ApplyMoves(pos, rest.Substring(mi + 7).Trim());
        }
        else
        {
            pos = Fen.Parse(rest);
        }
    }
}

static void ApplyMoves(Position pos, string movesStr)
{
    if (movesStr.Length == 0) return;
    Span<Move> buf = stackalloc Move[256];
    foreach (var token in movesStr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        int n = LegalMoveGenerator.Generate(pos, buf);
        Move? match = null;
        for (int i = 0; i < n; i++)
            if (buf[i].ToUci() == token) { match = buf[i]; break; }
        if (match is null) return;
        pos.MakeMove(match.Value);
    }
}

static void RunDemo()
{
    Magic.Init();
    var p = Fen.Parse(Fen.Initial);
    for (int d = 1; d <= 6; d++)
    {
        var sw = Stopwatch.StartNew();
        ulong nodes = Perft.Run(p, d);
        sw.Stop();
        Console.WriteLine($"perft({d}) = {nodes,12}   {sw.Elapsed.TotalSeconds,7:F2}s");
    }
}
