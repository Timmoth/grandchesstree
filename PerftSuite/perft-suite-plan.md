# Cross-Platform Perft Validation CLI — Plan (revised)

A single .NET CLI app that, given a path to a chess-engine executable, validates the engine's perft output against the bundled known-correct EPD corpora. JSON report out, exit code reflects success.

## Goal

```
perftcheck --engine /path/to/engine --depth-cap 4 --report out.json
```

- Engine is run as a subprocess (UCI protocol).
- Tool walks each `(FEN, depth, expected)` case from bundled EPDs.
- Tool sends `position fen … / go perft N` and parses `Nodes searched:`.
- Compares actual vs expected.
- Writes a JSON report with per-case failure details, exits 0 if all passed.

## Existing assets we reuse

The user's `GrandChessTree.Client.Tests/` already has:

| File                  | Source                         | Lines |
|-----------------------|--------------------------------|------:|
| `perft.epd`           | Chris Whittington collection   | 173   |
| `perft-marcel.epd`    | Marcel van Kervinck via CW     | 6,837 |

Identical format across both (`<FEN> ; D1 N ; D2 N ; …`), each carrying D1..D6 expected counts. The `data/*.epd` glob in the csproj auto-embeds any additional EPDs dropped into the directory locally, so contributors can add private corpora without code changes.

The new CLI bundles these as embedded resources — no external data files to ship.

## Project layout — single project, no extra splits

```
PerftSuite/
├── PerftSuite.csproj                 net8.0, PublishAot, single-file
├── Program.cs                        entry point + arg parsing
├── Epd/
│   ├── EpdCase.cs                    record (Fen, Depth, Expected)
│   └── EpdReader.cs                  parses bundled .epd files
├── Engines/
│   └── UciEngineDriver.cs            subprocess + stdin/stdout pipes
├── Runner/
│   ├── PerftRunner.cs                walks cases, calls driver, aggregates results
│   └── CaseResult.cs                 Pass / Fail / Timeout / EngineError
├── Reporting/
│   ├── ReportModel.cs                DTOs serialized to JSON
│   └── ReportJsonContext.cs          [JsonSerializable] for AOT
├── data/
│   ├── perft.epd                     <EmbeddedResource via data/*.epd glob>
│   └── perft-marcel.epd
└── build/
    ├── publish-all.sh                produces linux-x64, osx-arm64, osx-x64, win-x64
    └── publish-all.cmd
```

One project, no test project (we'll exercise the runner against `stockfish` or your own engine as the smoke test). If we want unit tests later we'll add a sibling test project then; the goal here is the shippable CLI.

## CLI surface

```
perftcheck [options]

  --engine <path>          (required) path to UCI engine executable
  --epd    <file>          additional EPD file (can be passed multiple times)
                           default: all three bundled corpora
  --depth-cap <N>          max depth to test         default: 4
  --depth-min <N>          min depth to test         default: 1
  --timeout   <seconds>    per-case timeout          default: 30
  --filter    <substr>     only run cases whose FEN contains substr
  --limit     <N>          test only the first N matching cases
  --report    <path>       write JSON report here    default: perft-report.json
  --fail-fast              stop on first failure
  --quiet                  no stdout progress (only final summary line)
  --help / -h
  --version
```

Exit codes:
- `0` — every selected case passed
- `1` — one or more cases failed (mismatch, timeout, or engine error)
- `2` — bad arguments / engine not found / EPD parse error

## UCI protocol

Standard "go perft" exchange:

```
→ uci
←   id name …
←   uciok
→ isready
←   readyok
→ position fen <fen>
→ go perft <depth>
←   a2a3: 380          (zero or more divide lines)
←   …
←   Nodes searched: <total>
→ isready
←   readyok            (used as a sync barrier between cases)
…
→ quit
```

The driver:
- Spawns the engine once and keeps it warm between cases (sends `position fen` + `go perft` repeatedly).
- Parses `Nodes searched: (\d+)` from stdout (also accepts `Total nodes: N` and `Total: N` as fallbacks for non-Stockfish engines).
- After each case, syncs with `isready` / `readyok`.
- On timeout: kills the process, marks the case `Timeout`, spawns a fresh process for the next case.
- On unexpected EOF or unparseable output: marks `EngineError`.

Only UCI for MVP. Custom-command protocol can come later if needed; every modern engine speaks UCI.

## JSON report

The whole point of this tool is a machine-readable artifact. Single JSON document, ~human-readable:

```json
{
  "tool":      "perftcheck",
  "version":   "0.1.0",
  "engine":    "/Users/.../engine",
  "engineId":  "Stockfish 17",
  "startedUtc": "2026-05-23T15:42:11Z",
  "durationSeconds": 194.31,
  "options": {
    "depthMin": 1,
    "depthCap": 4,
    "timeout":  30,
    "epdFiles": ["perft.epd", "perft-marcel.epd"]
  },
  "totals": {
    "cases":   20983,
    "passed":  20981,
    "failed":      2,
    "timeout":     0,
    "error":       0
  },
  "failures": [
    {
      "kind":     "mismatch",
      "fen":      "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
      "depth":    4,
      "expected": 197281,
      "actual":   197280,
      "diff":     -1,
      "elapsedSeconds": 0.014,
      "source":   "perft.epd:1"
    },
    {
      "kind":     "timeout",
      "fen":      "…",
      "depth":    4,
      "timeoutSeconds": 30,
      "source":   "perft-marcel.epd:4421"
    }
  ]
}
```

Failure entries also include the **engine's raw last output** (truncated to ~512 chars) when the kind is `error` — invaluable when an engine misformats its perft response.

`source` is the path + line number into the EPD file the case came from, so divide-style bisection is a copy-paste away.

For success-only runs the `failures` array is empty. JSON is always written, even on early exit.

## Cross-platform build

Single project + four publish targets:

```sh
# build/publish-all.sh
dotnet publish -c Release -r linux-x64    -o dist/linux-x64    --self-contained true /p:PublishSingleFile=true
dotnet publish -c Release -r osx-x64      -o dist/osx-x64      --self-contained true /p:PublishSingleFile=true
dotnet publish -c Release -r osx-arm64    -o dist/osx-arm64    --self-contained true /p:PublishSingleFile=true
dotnet publish -c Release -r win-x64      -o dist/win-x64      --self-contained true /p:PublishSingleFile=true
```

Each invocation produces one self-contained executable (`perftcheck` / `perftcheck.exe`) with no external runtime dependency. Roughly 12–20 MB per platform thanks to `PublishTrimmed`. AOT (`PublishAot`) would shrink that further and improve startup, but constrains reflection; happy to enable if you want.

The repo can stop there or add a `.tar.gz` / `.zip` step for releases.

## What I'm explicitly cutting from the original plan

- **No multi-project split.** Everything in one `PerftSuite/` folder.
- **No `dotnet tool` packaging.** Just plain `dotnet publish` to a self-contained exe.
- **No text reporter / no JUnit XML.** JSON is the only output format.
- **No `divide` command.** The JSON failure entries point at the offending position; users can run divide against their engine manually (or we add this in a later round if it turns out useful).
- **No parallel runner.** Sequential — one engine subprocess, processed cases serially. Adding parallelism is one PR away if the runtime is too slow, but defaults are aimed at "correctness check that runs in 1–2 minutes".
- **No reference engine.** Expected values come from the EPDs only.

## Decisions still needed before I start

1. **Target framework** — `net8.0` (broader compatibility, smaller AOT footprint) or `net10.0` (matches our new MoveGen)? Suggest `net8.0`.

2. **Arg-parser** — three options:
   - `System.CommandLine` (Microsoft, official, in-box-ish, AOT-friendly) — most polished.
   - Hand-rolled (~50 lines, zero deps) — simplest.
   - Spectre.Console.Cli — overkill if there's no progress bar / colour output.
   
   Suggest hand-rolled given the surface is small (10 flags).

3. **AOT or self-contained-trimmed?** AOT gives a ~5 MB binary with sub-second startup; trimmed gives ~15 MB with ~1 s startup. For a CLI you run from CI either is fine. Suggest **self-contained-trimmed** to start (simpler, no AOT-incompatible-API land mines) and switch to AOT later if startup matters.

4. **Tool / binary name** — `perftcheck`? Something more brand-aligned like `gct-perft`?

5. **Project location** — `/Users/timmoth/code/grandchesstree/PerftSuite/`?

6. **Stockfish-style line format only**, or should we also handle the alternative `Total nodes: N` and `Total: N` formats I mentioned? Costs almost nothing; suggest including them.

Answer those six and I'll generate the project. First commit will be a working tool that runs against your existing engine.
