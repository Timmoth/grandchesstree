# Move Generation — article series + reference implementation

A seven-part walkthrough of building a chess move generator in C#, plus the working reference engine that the articles assemble piece by piece, plus the research notes the prose was distilled from.

The end-state engine lives next to the articles in [`MoveGen/`](MoveGen/). It passes perft on the six standard test positions up to depth 6 from the initial position and depth 5 from Kiwipete — 119 / 119 xUnit tests green.

The articles are also **published as HTML** on the project site at
[grandchesstree.com/move-generator/](https://grandchesstree.com/move-generator/).
The publishing pipeline is [`build-articles.js`](#publishing-to-the-site)
in this directory — re-run it whenever any markdown file or figure changes.

## The articles

Plain markdown. No site-specific frontmatter — they're meant to be hand-converted to static HTML on the site. Each article builds on the previous one, ending with something runnable.

| #   | File                              | Builds                                                                     |
|-----|-----------------------------------|----------------------------------------------------------------------------|
| 1   | `01-introduction.md`              | Perft skeleton + FEN→ASCII renderer                                        |
| 2   | `02-representing-the-board.md`    | `Position` (12 bitboards + 8×8 mailbox), full FEN round-trip               |
| 3   | `03-encoding-a-move.md`           | 16-bit packed `Move` struct, UCI string conversion                         |
| 4   | `04-pseudo-legal-moves.md`        | Per-piece pseudo-legal generator, classical ray-scan sliders, `IsAttackedBy` |
| 5   | `05-making-a-move.md`             | `MakeMove`/`UnmakeMove`, undo stack, perft via "make then test king attacked" |
| 6   | `06-magic-bitboards.md`           | Magic-bitboard slider replacement; same perft numbers, faster              |
| 7   | `07-legal-move-generation.md`     | Checkers/pins/king-danger trinity, en-passant pin filter; perft matches reference up to d6 |

`SERIES-OUTLINE.md` is the planning doc — settled decisions (audience, language, voice, diagrams) and the per-article runnable-outcome table. Keep it in sync if the article scope shifts.

`js-feedback.md` captures feedback from porting the series to vanilla JavaScript (`../assets/js/movegen.js`) — bugs in the article prose, friction points, suggestions. Includes a "Responses" section listing what was accepted vs declined and where the fix landed.

## Reference implementation — `MoveGen/`

The engine lives next to the articles in [`MoveGen/`](MoveGen/). Every code
snippet in the prose mirrors a file in `MoveGen.App/`. Other consumers
(the site's WASM build, Perft-Checker, the perft-test tool) reference it from
this location.

```
MoveGen/
├── MoveGen.sln
├── MoveGen.App/       the engine — one .cs file per concept the articles introduce
├── MoveGen.Tests/     xUnit, one test file per article (Part1Tests.cs … Part7Tests.cs + ValidateTests.cs)
└── MoveGen.Wasm/      browser-WASM build of MoveGen.App — see its own README
```

The `MoveGen.App/` files are 1:1 with the articles. `Bitboards.cs` and `Attacks.cs` are introduced in Part 4, `PositionMake.cs` in Part 5, `Magic.cs` in Part 6, `Legality.cs` + `LegalMoveGenerator.cs` in Part 7. The Part-1 stub types in `Move.cs` / `MoveGenerator.cs` are kept around so the Part-1 perft compiles; they're functionally dead from Part 4 onward.

`MoveGen.App` also speaks UCI — invoke without `--demo` and it reads `position fen … / go perft N` from stdin and writes `Nodes searched: N` to stdout. That's what `../../../Perft-Checker/` validates against. A tiny `movegen-engine.sh` wrapper invokes the dll via `dotnet` so the macOS code-signing dance on the single-file build is avoided during dev.

Build and test:

```sh
cd MoveGen
dotnet test -c Release            # 119/119
dotnet run --project MoveGen.App  # UCI mode (default)
dotnet run --project MoveGen.App -- --demo   # perft demo
```

Targets `net10.0`.

## Article images — `img/`

Eleven board PNGs used as figures in the articles. All are rendered by `render-board.js`, which is a Node script that:

1. Loads `../assets/js/movegen.js` for FEN parsing.
2. Builds an SVG using the piece PNGs from `../assets/img/pieces/`.
3. Pipes that through `rsvg-convert` to PNG.

Same geometry, colours, and pixel-art rules as the FEN visualizer's Save-PNG button (`../assets/js/board-export.js`), so figures match the in-browser visualiser exactly.

To regenerate or add an image:

```sh
./render-board.js "<FEN>" img/<name>.png             # default 856 px wide
./render-board.js "<FEN>" img/<name>.png --width 600 # custom width
./render-board.js "<FEN>" img/<name>.png --flip      # black at the bottom
```

Requires `node` and `rsvg-convert` (`brew install librsvg` / `apt install librsvg2-bin`).

## Publishing to the site

[`build-articles.js`](build-articles.js) is a standalone Node script that
converts every `0N-*.md` in this directory into a styled HTML page under
[`../../dist/move-generator/`](../../dist/move-generator/). No npm
dependencies — the markdown parser is hand-rolled for exactly the features
the articles use (ATX headings, paragraphs, bold/italic, inline + fenced
code, lists, pipe tables, blockquotes, horizontal rules, links, images).

It's normally invoked indirectly by `Site/build.js`, but can also be run
standalone:

```sh
node build-articles.js          # writes to ../../dist/move-generator/ by default
MG_OUT_DIR=/tmp/foo node build-articles.js   # override the output dir
```

It produces:

- `move-generator/index.html` — hub page with one card per article
- `move-generator/01-introduction.html` … `07-legal-moves.html`
- `move-generator/img/` — every PNG from `img/` copied over

Each generated page includes the site's standard header/footer/nav (active
on "Move generator"), a breadcrumb, the article body with anchor-linked
headings, dark-themed code blocks with language pills, **Prism.js
syntax highlighting** loaded from a CDN (csharp + any other language
detected at runtime via the autoloader), and Prev/Next cards at the
bottom.

Don't hand-edit the generated files — re-run the build instead. To change
template chrome (header, breadcrumb format, prev/next styling), edit
`../templates/header.html` and `../templates/footer.html`; for everything
else (article structure, code block styling, Prism wiring) edit the
`pageHtml` / `hubHtml` functions in `build-articles.js`.

## Research — `research/`

45 markdown files extracted from chessprogramming.org during the research pass. Two flavours:

- **Per-page source extracts** — one file per wiki page consulted, with `source:` URL and `retrieved:` date frontmatter. Naming: `<topic>-<slug>.md` (`board-bitboards.md`, `movegen-en-passant.md`, `magic-looking-for-magics.md`, …).
- **Topic syntheses** — five `_*.md` files (`_perft-overview.md`, `_board-overview.md`, `_movegen-overview.md`, `_magic-overview.md`, `_move-overview.md`) that fuse the relevant per-page material into one coherent narrative aimed at engine implementers.

Unused-but-kept extracts: `magic-bmi2-pext.md`, `magic-hyperbola-quintessence.md`, `magic-kindergarten.md`, `move-copy-make.md`, `board-0x88.md`, `board-mailbox.md`. Out of scope for the current series, useful if the scope ever expands.

## Status

- **Series complete** — all seven articles written, peer-reviewed via the JS port.
- **Reference engine green** — 119/119 xUnit tests, perft matches CPW reference up to d6 initial / d5 Kiwipete.
- **Article images** — all eleven rendered and embedded.

When making changes:
- A code change → update the matching article *and* `MoveGen/`. Don't let them drift.
- A new figure → drop the FEN into `render-board.js`'s normal invocation rather than hand-drawing.
- A new chessprogramming.org reference → fetch it into `research/` with proper frontmatter, then thread it into the relevant `_*-overview.md` synthesis if it warrants.
