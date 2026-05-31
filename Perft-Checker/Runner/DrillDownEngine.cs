using PerftChecker.Engines;

namespace PerftChecker.Runner;

/// <summary>
/// Pinpoints the exact (sub-)position where the test engine's move
/// generator first diverges from a trusted oracle.
///
/// Standard manual perft-debug workflow, automated:
///
///   1. divide(F, N) on test engine vs divide(F, N) expected. Find first
///      move m whose child count differs.
///   2. Apply m, descend with (F', N-1). The new expected divide is
///      computed by the TGCT oracle since the corpus only has divides
///      for the starting FEN.
///   3. At N = 1 the divide IS the move list — the diff produces extra /
///      missing / wrong moves at the exact leaf position.
///
/// One TGCT round-trip per level (the new `divide:<d>:<mb>:<fen>:<moves>`
/// returns divide + resulting-fen in a single call).
/// </summary>
public sealed class DrillDownEngine
{
    readonly UciEngineDriver _test;
    readonly TgctOracle      _oracle;
    readonly int             _timeoutMs;

    public DrillDownEngine(UciEngineDriver test, TgctOracle oracle, int timeoutSeconds)
    {
        _test      = test;
        _oracle    = oracle;
        _timeoutMs = timeoutSeconds * 1000;
    }

    public async Task<DrillDownReport?> DrillAsync(
        string startingFen,
        int    startingDepth,
        IReadOnlyDictionary<string, ulong>? expectedDivideAtStart,
        CancellationToken ct)
    {
        var moveChain = new List<string>();
        string currentFen   = startingFen;
        int    currentDepth = startingDepth;

        // For the top-level position, the corpus already has the expected
        // divide — pass it through to save a round-trip. For deeper
        // positions we always ask TGCT.
        IReadOnlyDictionary<string, ulong>? expected = expectedDivideAtStart;

        while (true)
        {
            if (currentDepth < 1) return null;

            if (expected is null)
            {
                // Ask the oracle for divide(currentFen, currentDepth).
                // moves="" because currentFen is already the post-move
                // position from the prior iteration.
                var oracleR = await _oracle.DivideAsync(
                    currentFen, "", currentDepth, _timeoutMs, ct).ConfigureAwait(false);
                expected = oracleR.Divide;
            }

            // Test engine's divide. Drives the engine via its own UCI
            // pipe; same engine instance used for the original perft call.
            var actualR = await _test.RunDivideAsync(
                currentFen, currentDepth, _timeoutMs, ct).ConfigureAwait(false);
            if (actualR is not UciDivideResult.Success actual)
            {
                return new DrillDownReport
                {
                    BugDepth     = currentDepth,
                    MoveSequence = moveChain,
                    LeafFen      = currentFen,
                    Note         = actualR is UciDivideResult.Timeout
                                   ? "test engine timed out during drill-down divide"
                                   : "test engine error during drill-down divide",
                };
            }

            // Compare divides. At depth 1 a "child count" of 1 means the
            // move exists; an absent move means the engine didn't generate
            // it. So extra/missing-set logic applies regardless of depth.
            var diff = DiffDivides(expected, actual.Divide);

            // If at depth 1, the divide IS the move list — bottom out.
            if (currentDepth == 1)
            {
                // Some engines (Jet, others) report Nodes: N at d=1 but
                // skip the per-move breakdown. Without per-move data we
                // can't enumerate which moves are wrong — but we *can*
                // still draw a conclusion from the totals.
                if (actual.Divide.Count == 0)
                {
                    ulong expectedTotal = 0;
                    foreach (var v in expected.Values) expectedTotal += v;
                    string note = actual.Total == expectedTotal
                        ? "test engine d=1 total matches truth but emits no per-move divide lines at this depth; "
                          + "can't pinpoint a leaf move-gen bug from here. The higher-depth mismatch "
                          + "likely lives in make-move / unmake state."
                        : $"test engine d=1 total ({actual.Total:N0}) disagrees with truth ({expectedTotal:N0}), "
                          + "but the engine emits no per-move divide lines at d=1 — drill-down can't enumerate "
                          + "which moves are missing or extra at this leaf.";
                    return new DrillDownReport
                    {
                        BugDepth     = 1,
                        MoveSequence = moveChain,
                        LeafFen      = currentFen,
                        Note         = note,
                    };
                }

                return new DrillDownReport
                {
                    BugDepth     = 1,
                    MoveSequence = moveChain,
                    LeafFen      = currentFen,
                    ExtraMoves   = diff.OnlyInActual.ToList(),
                    MissingMoves = diff.OnlyInExpected.ToList(),
                    WrongCount   = diff.WrongCount,
                };
            }

            // Deeper level: at least one count is wrong (or a move is
            // missing/extra). Pick the first divergent move and descend.
            string? nextMove = PickFirstDivergent(diff);
            if (nextMove is null)
            {
                // Identical divides at this level — engine is correct
                // here despite the higher-level mismatch. Unusual; this
                // can happen if the test engine has a transient bug
                // higher up. Report as a "no divergence found below".
                return new DrillDownReport
                {
                    BugDepth     = currentDepth,
                    MoveSequence = moveChain,
                    LeafFen      = currentFen,
                    Note         = "divides match at this depth despite a higher-level mismatch",
                };
            }

            // If the divergent move is one only the actual side produced
            // (illegal/phantom move from the test engine's POV), we can't
            // descend through it — record and bail.
            if (!expected.ContainsKey(nextMove))
            {
                return new DrillDownReport
                {
                    BugDepth     = currentDepth,
                    MoveSequence = moveChain,
                    LeafFen      = currentFen,
                    ExtraMoves   = new() { nextMove },
                    MissingMoves = diff.OnlyInExpected.ToList(),
                    WrongCount   = diff.WrongCount,
                    Note         = "test engine produced a phantom root move; cannot descend",
                };
            }

            // Descend.
            moveChain.Add(nextMove);
            var step = await _oracle.DivideAsync(
                startingFen,
                string.Join(' ', moveChain),
                currentDepth - 1,
                _timeoutMs, ct).ConfigureAwait(false);
            currentFen   = step.ResultFen;
            currentDepth = currentDepth - 1;
            expected     = step.Divide;
        }
    }

    static string? PickFirstDivergent(DivideDiff diff)
    {
        // Prefer a "missing" move (engine failed to generate it) since
        // those are usually the actionable bugs in move-gen. If only
        // "wrong-count" entries exist, take the lexicographically first.
        foreach (var m in diff.OnlyInExpected)
            return m;
        foreach (var kv in diff.WrongCount)
            return kv.Key;
        foreach (var m in diff.OnlyInActual)
            return m;
        return null;
    }

    static DivideDiff DiffDivides(
        IReadOnlyDictionary<string, ulong> expected,
        IReadOnlyDictionary<string, ulong> actual)
    {
        var onlyExpected = new SortedSet<string>(StringComparer.Ordinal);
        var onlyActual   = new SortedSet<string>(StringComparer.Ordinal);
        var wrongCount   = new SortedDictionary<string, (ulong Expected, ulong Actual)>(
                                StringComparer.Ordinal);

        foreach (var (mv, ev) in expected)
        {
            if (!actual.TryGetValue(mv, out var av)) onlyExpected.Add(mv);
            else if (av != ev)                       wrongCount[mv] = (ev, av);
        }
        foreach (var (mv, _) in actual)
            if (!expected.ContainsKey(mv)) onlyActual.Add(mv);

        return new DivideDiff(onlyExpected, onlyActual, wrongCount);
    }

    sealed record DivideDiff(
        SortedSet<string>                                 OnlyInExpected,
        SortedSet<string>                                 OnlyInActual,
        SortedDictionary<string, (ulong Expected, ulong Actual)> WrongCount);
}

public sealed class DrillDownReport
{
    public int           BugDepth     { get; set; }
    public List<string>  MoveSequence { get; set; } = new();
    public string        LeafFen      { get; set; } = "";
    public List<string>  ExtraMoves   { get; set; } = new();
    public List<string>  MissingMoves { get; set; } = new();
    public SortedDictionary<string, (ulong Expected, ulong Actual)> WrongCount { get; set; }
        = new(StringComparer.Ordinal);
    public string?       Note         { get; set; }
}
