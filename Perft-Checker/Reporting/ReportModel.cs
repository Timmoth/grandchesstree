namespace PerftChecker.Reporting;

public sealed class Report
{
    public string Tool { get; set; } = "perftcheck";
    public string Version { get; set; } = "0.3.0";
    public string Engine { get; set; } = "";
    public string EngineId { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public double DurationSeconds { get; set; }
    public RunOptions Options { get; set; } = new();
    public Totals Totals { get; set; } = new();
    public List<FailureEntry> Failures { get; set; } = new();

    /// <summary>
    /// Cross-tabulates `tags` across the failure set so a developer can see
    /// "of the 17 failures, 14 had `en_passant_capture_possible`,
    /// 12 had `castling_white_kingside`, …". Empty when no failures have
    /// tags (e.g. a legacy EPD-only run).
    /// </summary>
    public TagAggregation FailureTags { get; set; } = new();
}

public sealed class RunOptions
{
    public int DepthMin { get; set; }
    public int DepthCap { get; set; }
    public int TimeoutSeconds { get; set; }
    public string? StaticAnalysisFile { get; set; }
    public List<string> EpdFiles { get; set; } = new();
    public string? Filter { get; set; }
    public int? Limit { get; set; }
    public bool FailFast { get; set; }
}

public sealed class Totals
{
    public int Cases   { get; set; }
    public int Passed  { get; set; }
    public int Failed  { get; set; }
    public int Timeout { get; set; }
    public int Error   { get; set; }
}

public sealed class FailureEntry
{
    public string Kind { get; set; } = "";          // "mismatch" | "timeout" | "error"
    public string Fen  { get; set; } = "";
    public int    Depth    { get; set; }
    public ulong? Expected { get; set; }
    public ulong? Actual   { get; set; }
    public long?  Diff     { get; set; }
    public double ElapsedSeconds { get; set; }
    public string Source { get; set; } = "";        // "<file>:<line>"
    public string? Message { get; set; }
    public string? EngineOutput { get; set; }       // truncated

    /// <summary>Source URLs for this position from the static-analysis row.
    /// Sorted best-context-first (smallest page first).</summary>
    public List<string>? SourceUrls { get; set; }

    /// <summary>Deterministic feature tags for this position
    /// (castling/EP/material/phase/etc.).</summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Populated when --drill-down is enabled and the failure is a
    /// mismatch (not a timeout/error). Pinpoints the exact sub-position
    /// where the engine's move-gen first diverges from truth.
    /// </summary>
    public DrillDownEntry? DrillDown { get; set; }
}

public sealed class DrillDownEntry
{
    /// <summary>Depth at which the leaf divergence sits (1 = pure move-gen bug).</summary>
    public int           BugDepth     { get; set; }

    /// <summary>UCI moves taken from the failure FEN to reach the leaf.</summary>
    public List<string>  MoveSequence { get; set; } = new();

    /// <summary>Position at the leaf (after applying MoveSequence to the failure FEN).</summary>
    public string        LeafFen      { get; set; } = "";

    /// <summary>Moves the engine generated at the leaf but shouldn't have.</summary>
    public List<string>  ExtraMoves   { get; set; } = new();

    /// <summary>Moves the engine failed to generate at the leaf.</summary>
    public List<string>  MissingMoves { get; set; } = new();

    /// <summary>For BugDepth&gt;1: moves whose sub-perft count differs.</summary>
    public Dictionary<string, WrongCountEntry> WrongCount { get; set; } = new();

    /// <summary>Diagnostic note when drill-down couldn't proceed cleanly.</summary>
    public string?       Note         { get; set; }
}

public sealed class WrongCountEntry
{
    public ulong Expected { get; set; }
    public ulong Actual   { get; set; }
    public long  Diff     { get; set; }
}

/// <summary>
/// How often each tag occurs across the failure set. `CountByTag` is the
/// raw count; `FractionByTag` is `count / failures-with-tags`. A high
/// fraction is the signal an engine developer wants — "this tag is
/// present in N% of failures, so it's a likely root cause".
/// </summary>
public sealed class TagAggregation
{
    public int FailuresWithTags { get; set; }
    public Dictionary<string, int> CountByTag { get; set; } = new();
    public Dictionary<string, double> FractionByTag { get; set; } = new();
}
