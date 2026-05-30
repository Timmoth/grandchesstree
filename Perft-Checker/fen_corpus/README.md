# FEN Corpus

A pipeline for building a corpus of "problematic" chess FENs harvested from
the open web, paired with a TGCT-generated perft EPD up to depth 7.

The goal: a single authoritative test suite of positions that have historically
exposed bugs in move generators — castling-through-check edge cases, pinned
en-passant captures, promotion-with-check, etc. — with cross-verified node
counts so it can be used as a regression set for any new movegen.

**Status:** all 8 stages implemented and running. ~7700 unique positions in
`corpus.csv`; `static_analysis.jsonl` populated with feature tags + perft
node counts d1…d5. Seed queries and the stage-6 snippet prompt still want
tuning.

---

## Pipeline

Eight stages (plus 01b), each a standalone script, each idempotent and
resumable. State flows through the filesystem so you can rerun any stage in
isolation.

```
queries.txt                            Leaderboard/engines/*.json
    │                                          │
    ▼                                          ▼
[01_search.py]   web search          [01b_github_issues.py]   GitHub REST API
    │            DDG/Brave/Tavily    │                        (issues + comments)
    ▼                                ▼
sources/queue.jsonl              sources/pages/<hash>.json    synthetic pages
    │
    ▼
[02_fetch.py]    BFS crawler: pops queue, fetches, extracts outbound links,
    │            appends "promising" new links back to the queue
    ▼
sources/pages/<hash>.json        cached page content + outbound links
    │
    ▼
[03_extract.py]  pure-regex FEN extraction over each page's text
    │
    ▼
extracted/<hash>.jsonl           {fen, source_url}, one per FEN found
    │
    ▼
[04_corpus.py]   validate, dedupe across all pages (key = first 4 FEN fields)
    │
    ▼
corpus.csv                       deduped corpus  ─┐
    │                                             │
    ▼                                             │
[05_validate_epd.py]  Stockfish perft 1..3 per    │
    │                FEN; splits standard vs      │
    │                Chess960; drops crashers     │
    ▼                                             │
epd/standard.epd, epd/chess960.epd                │
                                                  │
                            ┌─────────────────────┘
                            ▼
[06_snippets.py]  LLM-extract one verbatim debug snippet per FEN occurrence
    │             from each contributing page
    ▼
snippets/<hash>.json
    │
    ▼
[07_describe.py]  deterministic static analysis from the FEN string —
    │             piece counts, phase, castling/EP, boolean feature tags
    ▼
static_analysis.jsonl            one JSON line per FEN
    │
    ▼
[08_perft.py]    pool of TGCT subprocesses fills d1…dN node counts
    │            in static_analysis.jsonl. Resumable via perft_results.jsonl.
    ▼
perft_results.jsonl              append-only log; merged back into
                                 static_analysis.jsonl on completion
```

Each stage only writes to its own output and only reads from the prior
stage's output, so partial runs are safe.

## Crawler behavior

`02_fetch.py` is a BFS crawler. Each pass pops URLs from `queue.jsonl`,
skips anything already in `pages/`, fetches the rest with per-host
politeness sleep, and saves `pages/<hash>.json` with the stripped text plus
outbound links classified as **promising** (chess-domain allowlist or
chess-keyword in URL/anchor — enqueued) or **skipped** (wiki noise, edit
URLs, binaries, etc.).

Cap each run with `--max-pages` (default 200). Defaults
`--sleep-per-host 2` and `--per-host-quota 150` keep things polite — URLs
that hit the quota stay in the queue for a later run.

## GitHub issues source

`01b_github_issues.py` walks every engine descriptor under
`Leaderboard/engines/*.json` and searches each repo's issues (state=all) for
mentions of "FEN" or "perft". Body + comments are written as a synthetic
`pages/<hash>.json` so stage 3 picks them up identically.

Set `GITHUB_TOKEN` to raise the rate limit (60/hr → 5000/hr REST,
10/min → 30/min Search):

```sh
GITHUB_TOKEN=ghp_… python3 scripts/01b_github_issues.py
```

## Layout

```
Perft-Checker/fen_corpus/
├── README.md             # this file
├── config.py             # endpoints, paths, env-var overrides
├── queries.txt           # seed search queries, one per line
├── chess_repos.txt       # extra GitHub repos for 01b
├── .gitignore            # see "what's checked in" below
├── scripts/
│   ├── 01_search.py             # search → queue.jsonl
│   ├── 01b_github_issues.py     # GitHub issue bodies+comments → pages/
│   ├── 02_fetch.py              # BFS crawler
│   ├── 03_extract.py            # regex FEN extraction
│   ├── 04_corpus.py             # dedupe → corpus.csv
│   ├── 05_validate_epd.py       # Stockfish perft → standard/chess960 EPDs
│   ├── 06_snippets.py           # per-FEN LLM debug snippets
│   ├── 07_describe.py           # deterministic static analysis
│   └── 08_perft.py              # TGCT pool fills d1…dN in static_analysis
├── sources/
│   ├── queue.jsonl       # append-only URL queue          [gitignored]
│   └── pages/<hash>.json # fetched page cache             [gitignored]
├── extracted/<hash>.jsonl                                 [gitignored]
├── corpus.csv            # deduped corpus                 [gitignored]
├── corpus_hm.csv         # human-marked intermediates     [gitignored]
├── corpus_invalid.csv    # FENs rejected by stage 5       [gitignored]
├── epd/                  # standard.epd, chess960.epd     [gitignored]
├── snippets/<hash>.json  # stage 6 LLM output             [gitignored]
├── static_analysis.jsonl # stage 7 output (large)         [gitignored]
└── perft_results.jsonl   # stage 8 append-only log (large)[gitignored]
```

**What's checked in:** the scripts, `config.py`, `queries.txt`, and
`chess_repos.txt`. Everything else is regenerable. A vestigial
`descriptions/` directory may exist locally — leftovers from an earlier
static-analysis iteration, not produced by any current script.

## Configuration

Env vars (all optional; defaults in `config.py`):

| Var               | Default                       | Notes |
|-------------------|-------------------------------|-------|
| `LLM_BASE_URL`    | `http://10.0.50.4:8080/v1`    | OpenAI-compatible chat-completions endpoint. |
| `LLM_MODEL`       | `local`                       | Many local servers ignore this; set if yours doesn't. |
| `LLM_API_KEY`     | `none`                        | Sent as `Authorization: Bearer`; many local servers ignore. |
| `SEARCH_BACKEND`  | `ddg`                         | `ddg`, `brave`, or `tavily`. |
| `BRAVE_API_KEY`   | —                             | Required for `SEARCH_BACKEND=brave`. |
| `TAVILY_API_KEY`  | —                             | Required for `SEARCH_BACKEND=tavily`. |
| `USER_AGENT`      | benign desktop string         | Sent on all HTTP fetches. |

## Running

```sh
cd Perft-Checker/fen_corpus

# 1. Search — appends to sources/queue.jsonl (deduped by URL).
python3 scripts/01_search.py --max-per-query 20

# 1b. Optional: harvest GitHub issues/comments from every engine repo.
GITHUB_TOKEN=ghp_… python3 scripts/01b_github_issues.py

# 2. Fetch every URL that isn't yet cached.
python3 scripts/02_fetch.py

# 3. Regex-extract FENs from each page.
python3 scripts/03_extract.py

# 4. Build the deduped corpus CSV.
python3 scripts/04_corpus.py

# 5. Validate with Stockfish; split into standard.epd / chess960.epd.
python3 scripts/05_validate_epd.py

# 6. LLM extracts one verbatim debug snippet per FEN occurrence.
python3 scripts/06_snippets.py

# 7. Deterministic static analysis → static_analysis.jsonl.
python3 scripts/07_describe.py

# 8. Fill in d1…dN node counts via TGCT engine pool.
python3 scripts/08_perft.py --max-depth 5
```

## Dedup key

Dedup uses the first **four** FEN fields (board, side-to-move, castling,
en-passant). Halfmove/fullmove clocks are dropped — they don't affect
movegen. The original FEN of the first occurrence is kept; alternates and
source URLs merge into the row.
