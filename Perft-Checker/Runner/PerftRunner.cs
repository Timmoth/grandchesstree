using PerftChecker.Engines;
using PerftChecker.Epd;

namespace PerftChecker.Runner;

public sealed class PerftRunner
{
    public required string EnginePath { get; init; }
    public required int    TimeoutSeconds { get; init; }
    public required bool   FailFast { get; init; }
    public string PerftCommand { get; init; } = "go perft";

    /// <summary>If true, the engine's perft total may be reported as just
    /// a bare integer on its own line (Stormphrax, etc.).</summary>
    public bool AcceptBareNumberTotal { get; init; }

    /// <summary>Optional TGCT oracle used to drill into mismatches.
    /// When null, mismatches are reported but not drilled.</summary>
    public TgctOracle? Oracle { get; init; }

    UciEngineDriver? _driver;
    string _engineId = "unknown";

    public string EngineId => _engineId;

    public async Task<IReadOnlyList<CaseResult>> RunAsync(
        IReadOnlyList<EpdCase> cases,
        Action<CaseResult>     onCaseComplete,
        CancellationToken      ct)
    {
        await EnsureDriverAsync(ct).ConfigureAwait(false);
        var results = new List<CaseResult>(cases.Count);
        int timeoutMs = TimeoutSeconds * 1000;

        foreach (var c in cases)
        {
            if (ct.IsCancellationRequested) break;

            CaseResult r;
            try
            {
                var output = await _driver!.RunPerftAsync(c.Fen, c.Depth, timeoutMs, ct)
                    .ConfigureAwait(false);

                r = output switch
                {
                    PerftRunOutput.Success s when s.Nodes == c.Expected =>
                        new CaseResult(c, CaseStatus.Pass, s.Nodes, s.Elapsed.TotalSeconds, null, null),

                    PerftRunOutput.Success s =>
                        new CaseResult(c, CaseStatus.Mismatch, s.Nodes, s.Elapsed.TotalSeconds, null, null),

                    PerftRunOutput.Timeout t =>
                        new CaseResult(c, CaseStatus.Timeout, null, t.Elapsed.TotalSeconds, "case timed out", Trunc(t.RawOutput)),

                    PerftRunOutput.EngineError e =>
                        new CaseResult(c, CaseStatus.EngineError, null, e.Elapsed.TotalSeconds, e.Message, Trunc(e.RawOutput)),

                    _ => throw new InvalidOperationException("unreachable"),
                };
            }
            catch (EngineProtocolException ex)
            {
                r = new CaseResult(c, CaseStatus.EngineError, null, 0, ex.Message, null);
            }

            // Replace the engine if it crashed / timed out — the contract
            // of the driver is that it spins up fresh after a timeout.
            if (r.Status is CaseStatus.Timeout or CaseStatus.EngineError)
            {
                await DisposeDriverAsync().ConfigureAwait(false);
                await EnsureDriverAsync(ct).ConfigureAwait(false);
            }

            // Drill-down on mismatches when the oracle is wired up.
            if (Oracle is not null && r.Status == CaseStatus.Mismatch)
            {
                try
                {
                    var drill = new DrillDownEngine(_driver!, Oracle, TimeoutSeconds);
                    var report = await drill.DrillAsync(
                        c.Fen, c.Depth, c.ExpectedDivide, ct).ConfigureAwait(false);
                    if (report is not null) r = r with { DrillDown = report };
                }
                catch (EngineProtocolException ex)
                {
                    r = r with { DrillDown = new DrillDownReport
                    {
                        BugDepth     = c.Depth,
                        MoveSequence = new(),
                        LeafFen      = c.Fen,
                        Note         = $"drill-down aborted: {ex.Message}",
                    }};
                    // Oracle may have crashed mid-drill; replace driver too
                    // to be safe before the next case.
                    await DisposeDriverAsync().ConfigureAwait(false);
                    await EnsureDriverAsync(ct).ConfigureAwait(false);
                }
            }

            results.Add(r);
            onCaseComplete(r);

            if (FailFast && r.Status != CaseStatus.Pass) break;
        }

        await DisposeDriverAsync().ConfigureAwait(false);
        return results;
    }

    async Task EnsureDriverAsync(CancellationToken ct)
    {
        if (_driver is not null) return;
        var d = new UciEngineDriver(EnginePath, PerftCommand, AcceptBareNumberTotal);
        await d.StartAsync(handshakeTimeoutMs: 10_000, ct).ConfigureAwait(false);
        _driver = d;
        if (_engineId == "unknown")
            _engineId = d.EngineId;
    }

    async Task DisposeDriverAsync()
    {
        if (_driver is null) return;
        await _driver.DisposeAsync().ConfigureAwait(false);
        _driver = null;
    }

    static string Trunc(string s, int max = 512)
        => s.Length <= max ? s : s.Substring(0, max) + "…";
}
