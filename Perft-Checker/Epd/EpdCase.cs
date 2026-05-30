namespace PerftChecker.Epd;

/// <summary>
/// One (FEN, depth, expected node count) row.
///
/// Originally sourced from EPD files; now usually populated from the
/// fen_corpus static_analysis.jsonl pipeline, which carries the FEN's
/// originating source URLs and feature tags alongside the perft count.
/// Both `SourceUrls` and `Tags` are optional — null/empty when the row
/// came from a legacy EPD file with no surrounding context.
///
/// `ExpectedDivide` (uci_move → child_node_count) is populated only when
/// the reader is asked to include divides — used by --drill-down to
/// pinpoint the first diverging root move. Null otherwise.
/// </summary>
public sealed record EpdCase(
    string Fen,
    int    Depth,
    ulong  Expected,
    string SourceFile,
    int    SourceLine,
    IReadOnlyList<string>? SourceUrls     = null,
    IReadOnlyList<string>? Tags           = null,
    IReadOnlyDictionary<string, ulong>? ExpectedDivide = null,
    string? ContextQuality                = null);
