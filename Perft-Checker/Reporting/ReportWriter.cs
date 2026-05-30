using System.Text.Json;
using PerftChecker.Runner;

namespace PerftChecker.Reporting;

public static class ReportWriter
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(string path, Report report)
    {
        string json = JsonSerializer.Serialize(report, Options);
        File.WriteAllText(path, json);
    }

    public static FailureEntry MakeFailure(CaseResult r)
    {
        var fe = new FailureEntry
        {
            Fen      = r.Case.Fen,
            Depth    = r.Case.Depth,
            Expected = r.Case.Expected,
            Actual   = r.Actual,
            ElapsedSeconds = r.ElapsedSeconds,
            Source   = $"{r.Case.SourceFile}:{r.Case.SourceLine}",
            Message  = r.ErrorMessage,
            EngineOutput = r.RawEngineOutput,
        };
        fe.Kind = r.Status switch
        {
            CaseStatus.Mismatch    => "mismatch",
            CaseStatus.Timeout     => "timeout",
            CaseStatus.EngineError => "error",
            _                      => "unknown",
        };
        if (r.Status == CaseStatus.Mismatch && r.Actual.HasValue)
            fe.Diff = (long)r.Actual.Value - (long)r.Case.Expected;

        // Carry through static-analysis metadata if it's present on the case.
        if (r.Case.SourceUrls is { Count: > 0 })
            fe.SourceUrls = new List<string>(r.Case.SourceUrls);
        if (r.Case.Tags is { Count: > 0 })
            fe.Tags = new List<string>(r.Case.Tags);

        if (r.DrillDown is { } dd)
        {
            var de = new DrillDownEntry
            {
                BugDepth     = dd.BugDepth,
                MoveSequence = new List<string>(dd.MoveSequence),
                LeafFen      = dd.LeafFen,
                ExtraMoves   = new List<string>(dd.ExtraMoves),
                MissingMoves = new List<string>(dd.MissingMoves),
                Note         = dd.Note,
            };
            foreach (var kv in dd.WrongCount)
            {
                de.WrongCount[kv.Key] = new WrongCountEntry
                {
                    Expected = kv.Value.Expected,
                    Actual   = kv.Value.Actual,
                    Diff     = (long)kv.Value.Actual - (long)kv.Value.Expected,
                };
            }
            fe.DrillDown = de;
        }

        return fe;
    }

    /// <summary>
    /// Computes the tag cross-tabulation across the failure set. A failure
    /// without tags (e.g. from a legacy EPD-only run) doesn't contribute to
    /// the denominator — `FailuresWithTags` is the size of the population
    /// the fractions are computed against. Each failure contributes its
    /// tags as a *set* (no double-counting if a tag appears twice).
    /// </summary>
    public static TagAggregation ComputeAggregation(IEnumerable<FailureEntry> failures)
    {
        var counts = new Dictionary<string, int>();
        int withTags = 0;
        foreach (var f in failures)
        {
            if (f.Tags is null || f.Tags.Count == 0) continue;
            withTags++;
            foreach (var tag in new HashSet<string>(f.Tags))
                counts[tag] = counts.GetValueOrDefault(tag, 0) + 1;
        }
        var fractions = new Dictionary<string, double>();
        if (withTags > 0)
            foreach (var kv in counts)
                fractions[kv.Key] = (double)kv.Value / withTags;
        return new TagAggregation
        {
            FailuresWithTags = withTags,
            CountByTag       = counts,
            FractionByTag    = fractions,
        };
    }
}
