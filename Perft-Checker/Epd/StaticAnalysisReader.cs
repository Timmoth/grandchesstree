using System.Text.Json;

namespace PerftChecker.Epd;

/// <summary>
/// Reads the fen_corpus static_analysis.jsonl file (one JSON object per
/// line) and yields one EpdCase per (FEN, depth) pair where the
/// corresponding `d<N>` field is populated (non-zero). Zero values are
/// treated as "not yet computed" — the future perft-population pipeline
/// fills them in.
///
/// Carries `source_urls` and `tags` from each row into the EpdCase so
/// downstream reporting can surface them per-failure and aggregate them
/// across all failures.
/// </summary>
public static class StaticAnalysisReader
{
    public static IEnumerable<EpdCase> ReadFile(string path, bool includeDivides = false)
    {
        string label = Path.GetFileName(path);
        int lineNum = 0;
        using var sr = new StreamReader(path);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("fen", out var fenEl)) continue;
                string? fen = fenEl.GetString();
                if (string.IsNullOrEmpty(fen)) continue;

                IReadOnlyList<string>? urls = ReadStringArray(root, "source_urls");
                IReadOnlyList<string>? tags = ReadStringArray(root, "tags");
                string? quality = root.TryGetProperty("context_quality", out var qEl)
                                  ? qEl.GetString() : null;

                for (int d = 1; d <= 7; d++)
                {
                    if (!root.TryGetProperty($"d{d}", out var dEl)) continue;
                    if (dEl.ValueKind != JsonValueKind.Number)     continue;

                    ulong expected;
                    if (!dEl.TryGetUInt64(out expected)) continue;
                    // Zero = not yet populated by the perft pipeline; skip.
                    // A real perft-1 of 0 (checkmate) will need to be
                    // distinguished via a `tags` entry such as "checkmate"
                    // before we surface it here.
                    if (expected == 0) continue;

                    IReadOnlyDictionary<string, ulong>? divide = null;
                    if (includeDivides)
                        divide = ReadDivide(root, $"divide_d{d}");

                    yield return new EpdCase(
                        fen, d, expected, label, lineNum, urls, tags, divide,
                        ContextQuality: quality);
                }
            }
        }
    }

    static IReadOnlyDictionary<string, ulong>? ReadDivide(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el))   return null;
        if (el.ValueKind != JsonValueKind.Object)    return null;
        var map = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number
                && prop.Value.TryGetUInt64(out var v))
            {
                map[prop.Name] = v;
            }
        }
        return map.Count == 0 ? null : map;
    }

    static IReadOnlyList<string>? ReadStringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind != JsonValueKind.Array)   return null;
        var list = new List<string>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? s = item.GetString();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
        }
        return list.Count == 0 ? null : list;
    }
}
