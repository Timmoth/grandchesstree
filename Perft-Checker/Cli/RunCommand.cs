using System.ComponentModel;
using System.Diagnostics;
using PerftChecker.Engines;
using PerftChecker.Epd;
using PerftChecker.Reporting;
using PerftChecker.Runner;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PerftChecker.Cli;

public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
    const string EdgeCaseTag = "edge_case_engine_disagreement";

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-e|--engine <PATH>")]
        [Description("Path to the UCI engine executable.")]
        public required string Engine { get; init; }

        [CommandOption("-s|--static-analysis <FILE>")]
        [Description("Override path to a corpus file. Accepts either the "
                     + "gzipped binary `corpus.gctc.gz` (output of 09_pack.py) "
                     + "or the JSONL `static_analysis.jsonl` (output of "
                     + "07_describe.py + 08_perft.py). If omitted, the "
                     + "binary corpus embedded in this perftcheck build is "
                     + "used — almost always what you want.")]
        public string? StaticAnalysis { get; init; }

        [CommandOption("--depth-cap <N>")]
        [Description("Maximum perft depth to test.")]
        [DefaultValue(4)]
        public int DepthCap { get; init; }

        [CommandOption("--depth-min <N>")]
        [Description("Minimum perft depth to test.")]
        [DefaultValue(1)]
        public int DepthMin { get; init; }

        [CommandOption("--timeout <SECS>")]
        [Description("Per-case timeout in seconds.")]
        [DefaultValue(30)]
        public int Timeout { get; init; }

        [CommandOption("--filter <SUBSTR>")]
        [Description("Only run cases whose FEN contains this substring.")]
        public string? Filter { get; init; }

        [CommandOption("--quality <TIER>")]
        [Description("Restrict to one tier of context quality. Accepts "
                     + "'high', 'medium', 'low', or 'all'.")]
        [DefaultValue("all")]
        public string Quality { get; init; } = "all";

        [CommandOption("--tag <NAME>")]
        [Description("Only run cases whose static-analysis tags contain "
                     + "this exact tag. Repeatable; all listed tags must be "
                     + "present (AND).")]
        public string[]? Tag { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Test only the first N matching cases.")]
        public int? Limit { get; init; }

        [CommandOption("--report <PATH>")]
        [Description("Path to write the JSON report.")]
        [DefaultValue("perft-report.json")]
        public string Report { get; init; } = "perft-report.json";

        [CommandOption("--fail-fast")]
        [Description("Stop on the first failure.")]
        public bool FailFast { get; init; }

        [CommandOption("--quiet")]
        [Description("Suppress per-case console output.")]
        public bool Quiet { get; init; }

        [CommandOption("--perft-command <CMD>")]
        [Description("UCI command sent before the depth number, default "
                     + "'go perft'. Set to 'perft' for engines (e.g. Potential, "
                     + "Stormphrax) that accept the bare form.")]
        [DefaultValue("go perft")]
        public string PerftCommand { get; init; } = "go perft";

        [CommandOption("--bare-number-total")]
        [Description("Accept a bare integer on its own line as the perft "
                     + "total (Stormphrax and a few others print just the "
                     + "number with no `Nodes:` prefix). Off by default to "
                     + "avoid matching unrelated numeric debug output from "
                     + "chatty engines.")]
        public bool BareNumberTotal { get; init; }

        [CommandOption("--drill-down")]
        [Description("On each mismatch, drive the test engine and the "
                     + "reference engine (TGCT) through divides depth-by-depth "
                     + "until the exact leaf position is found where the "
                     + "engine's move-gen diverges. Requires --ref-engine.")]
        public bool DrillDown { get; init; }

        [CommandOption("--include-edge-cases")]
        [Description("Include positions tagged 'edge_case_engine_disagreement' — "
                     + "FENs where production engines (StockDory, Stormphrax, "
                     + "etc.) historically disagree with the Stockfish/TGCT "
                     + "oracle. Off by default to keep clean runs clean; opt "
                     + "in when stress-testing exotic move-gen paths "
                     + "(EP-blocks-check, near-50-move-rule, pathological "
                     + "piece counts).")]
        public bool IncludeEdgeCases { get; init; }

        [CommandOption("--ref-engine <PATH>")]
        [Description("Path to a trusted reference engine (TGCT/GrandChessTree) "
                     + "used as the oracle for --drill-down. Must accept the "
                     + "extended divide:<d>:<mb>:<fen>:<moves> command.")]
        public string? RefEngine { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken cancellationToken)
    {
        if (!File.Exists(s.Engine))
        {
            AnsiConsole.MarkupLine($"[red]Engine not found:[/] {s.Engine}");
            return 2;
        }
        if (s.StaticAnalysis is not null && !File.Exists(s.StaticAnalysis))
        {
            AnsiConsole.MarkupLine($"[red]static-analysis file not found:[/] {s.StaticAnalysis}");
            return 2;
        }
        if (s.DepthMin < 1 || s.DepthCap < s.DepthMin)
        {
            AnsiConsole.MarkupLine("[red]Invalid depth range.[/]");
            return 2;
        }
        if (s.DrillDown)
        {
            if (string.IsNullOrEmpty(s.RefEngine))
            {
                AnsiConsole.MarkupLine("[red]--drill-down requires --ref-engine.[/]");
                return 2;
            }
            if (!File.Exists(s.RefEngine))
            {
                AnsiConsole.MarkupLine($"[red]Reference engine not found:[/] {s.RefEngine}");
                return 2;
            }
        }

        List<EpdCase> cases;
        try
        {
            cases = LoadCases(s);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]static-analysis load failed:[/] {ex.Message}");
            return 2;
        }

        if (cases.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No cases selected.[/] If d1…d7 in the static-analysis "
                + "are all zero, the perft-population pipeline hasn't filled "
                + "them in yet — nothing to verify.");
            return 2;
        }

        TgctOracle? oracle = null;
        if (s.DrillDown)
        {
            oracle = new TgctOracle(s.RefEngine!);
            try { await oracle.StartAsync(cancellationToken); }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to launch reference engine '{s.RefEngine}':[/] {ex.Message}");
                return 2;
            }
        }

        var runner = new PerftRunner
        {
            EnginePath     = s.Engine,
            TimeoutSeconds = s.Timeout,
            FailFast       = s.FailFast,
            PerftCommand   = s.PerftCommand,
            AcceptBareNumberTotal = s.BareNumberTotal,
            Oracle         = oracle,
        };

        if (!s.Quiet)
        {
            var grid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn();
            grid.AddRow("[grey]engine[/]",          s.Engine);
            grid.AddRow("[grey]static-analysis[/]",
                s.StaticAnalysis ?? "(embedded corpus.gctc.gz)");
            grid.AddRow("[grey]quality[/]",         s.Quality);
            if (s.Tag is { Length: > 0 })
                grid.AddRow("[grey]tag filter[/]",  string.Join(" AND ", s.Tag));
            grid.AddRow("[grey]depths[/]",          $"{s.DepthMin}–{s.DepthCap}");
            grid.AddRow("[grey]cases[/]",           cases.Count.ToString());
            grid.AddRow("[grey]edge cases[/]",
                s.IncludeEdgeCases ? "[yellow]included[/]" : "excluded (default)");
            grid.AddRow("[grey]timeout[/]",         $"{s.Timeout}s");
            grid.AddRow("[grey]report[/]",          s.Report);
            AnsiConsole.Write(new Panel(grid).Header(" perftcheck ").Border(BoxBorder.Rounded));
        }

        var stopwatch = Stopwatch.StartNew();
        var ctsLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctsLifetime.Cancel(); };

        IReadOnlyList<CaseResult> results;
        if (s.Quiet)
        {
            results = await runner.RunAsync(cases, _ => {}, ctsLifetime.Token);
        }
        else
        {
            results = await AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("running", maxValue: cases.Count);
                    int pass = 0, fail = 0;
                    return await runner.RunAsync(cases, r =>
                    {
                        if (r.Status == CaseStatus.Pass) pass++;
                        else                              fail++;
                        task.Description =
                            $"[green]{pass} pass[/]  [red]{fail} fail[/]";
                        task.Increment(1);
                    }, ctsLifetime.Token);
                });
        }

        stopwatch.Stop();

        var totals = new Totals();
        var failures = new List<FailureEntry>();
        foreach (var r in results)
        {
            totals.Cases++;
            switch (r.Status)
            {
                case CaseStatus.Pass:        totals.Passed++; break;
                case CaseStatus.Mismatch:    totals.Failed++;  failures.Add(ReportWriter.MakeFailure(r)); break;
                case CaseStatus.Timeout:     totals.Timeout++; failures.Add(ReportWriter.MakeFailure(r)); break;
                case CaseStatus.EngineError: totals.Error++;   failures.Add(ReportWriter.MakeFailure(r)); break;
            }
        }

        var report = new Report
        {
            Engine          = Path.GetFullPath(s.Engine),
            EngineId        = runner.EngineId,
            StartedUtc      = DateTime.UtcNow.Subtract(stopwatch.Elapsed),
            DurationSeconds = stopwatch.Elapsed.TotalSeconds,
            Options = new RunOptions
            {
                DepthMin           = s.DepthMin,
                DepthCap           = s.DepthCap,
                TimeoutSeconds     = s.Timeout,
                StaticAnalysisFile = s.StaticAnalysis is null
                                     ? "embedded:corpus.gctc.gz"
                                     : Path.GetFileName(s.StaticAnalysis),
                Filter             = s.Filter,
                Limit              = s.Limit,
                FailFast           = s.FailFast,
                IncludeEdgeCases   = s.IncludeEdgeCases,
            },
            Totals      = totals,
            Failures    = failures,
            FailureTags = ReportWriter.ComputeAggregation(failures),
        };

        try
        {
            ReportWriter.Write(s.Report, report);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to write report '{s.Report}':[/] {ex.Message}");
            return 1;
        }

        if (!s.Quiet)
        {
            PrintSummary(report);
            PrintReportLocation(s.Report);
        }

        if (oracle is not null) await oracle.DisposeAsync().ConfigureAwait(false);

        int badCount = totals.Failed + totals.Timeout + totals.Error;
        return badCount == 0 ? 0 : 1;
    }

    static List<EpdCase> LoadCases(Settings s)
    {
        IEnumerable<EpdCase> raw;
        if (s.StaticAnalysis is null)
        {
            raw = CorpusBinaryReader.ReadEmbedded(includeDivides: s.DrillDown);
        }
        else if (IsBinaryCorpus(s.StaticAnalysis))
        {
            raw = CorpusBinaryReader.ReadFile(s.StaticAnalysis, includeDivides: s.DrillDown);
        }
        else
        {
            raw = StaticAnalysisReader.ReadFile(s.StaticAnalysis, includeDivides: s.DrillDown);
        }

        // Quality filter — both readers populate ContextQuality, so this is a
        // straight LINQ Where (no JSON re-read needed).
        if (!string.Equals(s.Quality, "all", StringComparison.OrdinalIgnoreCase))
        {
            var allowed = ParseQualityFilter(s.Quality);
            raw = raw.Where(c => c.ContextQuality is not null
                                 && allowed.Contains(c.ContextQuality));
        }

        IEnumerable<EpdCase> q = raw
            .Where(c => c.Depth >= s.DepthMin && c.Depth <= s.DepthCap);

        if (!string.IsNullOrEmpty(s.Filter))
            q = q.Where(c => c.Fen.Contains(s.Filter, StringComparison.Ordinal));

        if (s.Tag is { Length: > 0 })
        {
            var needed = new HashSet<string>(s.Tag, StringComparer.Ordinal);
            q = q.Where(c => c.Tags is not null
                             && needed.All(t => c.Tags.Contains(t)));
        }

        // Edge-case filter — off by default. The tag is set by the corpus
        // pack pipeline (09_pack.py) for FENs where production engines
        // historically diverge from the Stockfish/TGCT oracle.
        if (!s.IncludeEdgeCases)
        {
            q = q.Where(c => c.Tags is null
                             || !c.Tags.Contains(EdgeCaseTag));
        }

        if (s.Limit is int n) q = q.Take(n);
        return q.ToList();
    }

    static HashSet<string> ParseQualityFilter(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "high"   => new() { "high" },
            "medium" => new() { "medium" },
            "low"    => new() { "low" },
            "both"   => new() { "high", "medium" },
            _        => new() { "high", "medium", "low" },
        };
    }

    /// <summary>
    /// True if the file is a gzipped binary corpus (`corpus.gctc.gz`).
    /// Sniffs the gzip header + decompresses the first 4 bytes looking for
    /// the GCTC magic. Falls back to extension check when the file is too
    /// short to peek at.
    /// </summary>
    static bool IsBinaryCorpus(string path)
    {
        if (path.EndsWith(".gctc.gz", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gctc",    StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[2];
            if (fs.Read(head) < 2) return false;
            if (head[0] != 0x1f || head[1] != 0x8b) return false;     // not gzip
            fs.Position = 0;
            using var gz = new System.IO.Compression.GZipStream(
                fs, System.IO.Compression.CompressionMode.Decompress);
            Span<byte> magic = stackalloc byte[4];
            int n = gz.Read(magic);
            return n == 4 && magic[0] == 'G' && magic[1] == 'C'
                          && magic[2] == 'T' && magic[3] == 'C';
        }
        catch
        {
            return false;
        }
    }

    static void PrintSummary(Report r)
    {
        var t = new Table().Border(TableBorder.Rounded)
            .AddColumn("metric")
            .AddColumn(new TableColumn("value").RightAligned());
        t.AddRow("engine",   Markup.Escape(r.EngineId));
        t.AddRow("duration", $"{r.DurationSeconds:F2}s");
        t.AddRow("cases",    r.Totals.Cases.ToString());
        t.AddRow("[green]passed[/]",  r.Totals.Passed.ToString());
        t.AddRow(r.Totals.Failed  > 0 ? "[red]failed[/]"  : "failed",  r.Totals.Failed.ToString());
        t.AddRow(r.Totals.Timeout > 0 ? "[red]timeout[/]" : "timeout", r.Totals.Timeout.ToString());
        t.AddRow(r.Totals.Error   > 0 ? "[red]error[/]"   : "error",   r.Totals.Error.ToString());
        AnsiConsole.Write(t);

        if (r.Failures.Count > 0)
        {
            int show = Math.Min(r.Failures.Count, 10);
            AnsiConsole.MarkupLine($"\n[red]Showing {show} of {r.Failures.Count} failure(s):[/]");
            foreach (var f in r.Failures.Take(show))
            {
                var lines = new List<string>
                {
                    $"[grey]source[/] {Markup.Escape(f.Source)}",
                    $"[grey]fen[/]    {Markup.Escape(f.Fen)}",
                    $"[grey]depth[/]  {f.Depth}   [grey]kind[/] {f.Kind}",
                };
                if (f.Kind == "mismatch")
                    lines.Add($"[red]expected[/] {f.Expected:N0}   [red]actual[/] {f.Actual:N0}   [red]diff[/] {f.Diff}");
                if (!string.IsNullOrEmpty(f.Message))
                    lines.Add($"[red]message[/] {Markup.Escape(f.Message)}");
                if (f.Tags is { Count: > 0 })
                    lines.Add($"[grey]tags[/]   {Markup.Escape(string.Join(", ", f.Tags))}");
                if (f.SourceUrls is { Count: > 0 })
                {
                    int n = Math.Min(f.SourceUrls.Count, 3);
                    var slice = string.Join("\n           ",
                        f.SourceUrls.Take(n).Select(Markup.Escape));
                    string suffix = f.SourceUrls.Count > n
                        ? $"\n           [grey](+{f.SourceUrls.Count - n} more)[/]"
                        : "";
                    lines.Add($"[grey]links[/]  {slice}{suffix}");
                }
                if (f.DrillDown is { } dd)
                {
                    lines.Add($"[grey]→ drill[/] bug at d={dd.BugDepth}"
                              + (dd.MoveSequence.Count > 0
                                  ? $"  via {Markup.Escape(string.Join(' ', dd.MoveSequence))}"
                                  : ""));
                    lines.Add($"[grey]  leaf[/] {Markup.Escape(dd.LeafFen)}");
                    if (dd.MissingMoves.Count > 0)
                        lines.Add($"[red]  missing[/]  {Markup.Escape(string.Join(", ", dd.MissingMoves))}");
                    if (dd.ExtraMoves.Count > 0)
                        lines.Add($"[red]  extra[/]    {Markup.Escape(string.Join(", ", dd.ExtraMoves))}");
                    if (dd.WrongCount.Count > 0)
                    {
                        var s = string.Join(", ",
                            dd.WrongCount.Take(5).Select(kv =>
                                $"{kv.Key}:{kv.Value.Actual}≠{kv.Value.Expected}"));
                        lines.Add($"[red]  wrong[/]    {Markup.Escape(s)}");
                    }
                    if (!string.IsNullOrEmpty(dd.Note))
                        lines.Add($"[grey]  note[/]     {Markup.Escape(dd.Note!)}");
                }
                AnsiConsole.Write(new Panel(string.Join("\n", lines)).Border(BoxBorder.Rounded));
            }
        }

        PrintTagAggregation(r);
        PrintDrillDownSummary(r);
    }

    static void PrintDrillDownSummary(Report r)
    {
        var drilled = r.Failures.Where(f => f.DrillDown is not null).ToList();
        if (drilled.Count == 0) return;

        var byBugDepth = drilled.GroupBy(f => f.DrillDown!.BugDepth)
                                .OrderBy(g => g.Key)
                                .ToList();
        var depthTable = new Table().Border(TableBorder.Rounded)
            .AddColumn("bug depth")
            .AddColumn(new TableColumn("failures").RightAligned());
        foreach (var g in byBugDepth)
            depthTable.AddRow(g.Key.ToString(), g.Count().ToString());

        AnsiConsole.MarkupLine(
            $"\n[grey]Drill-down across the {drilled.Count} drilled failure(s):[/]");
        AnsiConsole.Write(depthTable);
    }

    static void PrintTagAggregation(Report r)
    {
        var agg = r.FailureTags;
        if (agg.FailuresWithTags == 0) return;

        var sorted = agg.CountByTag
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        // Show only the top 20 to keep the summary readable; the JSON has all.
        int take = Math.Min(sorted.Count, 20);

        var t = new Table().Border(TableBorder.Rounded);
        t.AddColumn("tag");
        t.AddColumn(new TableColumn("failures").RightAligned());
        t.AddColumn(new TableColumn("fraction").RightAligned());

        foreach (var kv in sorted.Take(take))
        {
            double frac = agg.FractionByTag.TryGetValue(kv.Key, out var f) ? f : 0;
            string fracStr = $"{frac:P0}";
            // Escape the tag once for safe markup interpolation, then wrap
            // in [yellow]…[/] when the fraction is high enough to be a
            // strong signal. AddRow takes markup strings, so we don't want
            // to escape the result a second time.
            string safeTag = Markup.Escape(kv.Key);
            string display = frac >= 0.5 ? $"[yellow]{safeTag}[/]" : safeTag;
            t.AddRow(display, kv.Value.ToString(), fracStr);
        }

        AnsiConsole.MarkupLine(
            $"\n[grey]Tags across the {agg.FailuresWithTags} failure(s)[/] "
            + $"[grey](top {take} of {sorted.Count}, [yellow]≥50%[/] highlighted)[/]:");
        AnsiConsole.Write(t);
    }

    static void PrintReportLocation(string reportPath)
    {
        AnsiConsole.MarkupLine($"\nReport written to [blue]{Markup.Escape(Path.GetFullPath(reportPath))}[/]");
    }
}
