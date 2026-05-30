using System.Reflection;

namespace PerftChecker.Epd;

/// <summary>
/// Reads EPD-style perft files in the format used by Chess-EPDs / Ethereal:
///   <FEN> ; D1 <count> ; D2 <count> ; ... ; Dn <count>
/// </summary>
public static class EpdReader
{
    const string ResourcePrefix = "PerftChecker.data.";

    public static readonly string[] BundledNames =
        Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".epd", StringComparison.Ordinal))
            .Select(n => n.Substring(ResourcePrefix.Length))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    public static IEnumerable<EpdCase> ReadBundled(string logicalName)
    {
        string resourceName = $"PerftChecker.data.{logicalName}";
        var asm = Assembly.GetExecutingAssembly();
        using Stream? s = asm.GetManifestResourceStream(resourceName);
        if (s is null)
            throw new FileNotFoundException(
                $"Bundled EPD '{logicalName}' (resource '{resourceName}') not found.");

        using var sr = new StreamReader(s);
        foreach (var c in ReadStream(sr, logicalName))
            yield return c;
    }

    public static IEnumerable<EpdCase> ReadFile(string path)
    {
        using var sr = new StreamReader(path);
        foreach (var c in ReadStream(sr, Path.GetFileName(path)))
            yield return c;
    }

    static IEnumerable<EpdCase> ReadStream(StreamReader sr, string sourceLabel)
    {
        int lineNum = 0;
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith('#')) continue;

            // <FEN> ; D1 N ; D2 N ; ...
            string[] parts = line.Split(';', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            string fen = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string seg = parts[i];
                if (seg.Length < 3 || (seg[0] != 'D' && seg[0] != 'd'))
                    continue;

                // Expect "D<num> <count>" — splits on whitespace.
                string[] tok = seg.Split(' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tok.Length != 2) continue;

                if (!int.TryParse(tok[0].AsSpan(1), out int depth)) continue;
                if (!ulong.TryParse(tok[1], out ulong expected)) continue;

                yield return new EpdCase(fen, depth, expected, sourceLabel, lineNum);
            }
        }
    }
}
