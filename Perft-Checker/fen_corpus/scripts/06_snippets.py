#!/usr/bin/env python3
"""Stage 6: per-page LLM extraction of verbatim FEN-context snippets.

For each cached page that contributes at least one HIGH (and optionally
MEDIUM) context FEN to corpus.csv, send the page text to the local LLM and
ask for one verbatim snippet per FEN occurrence. Results are stored in
`snippets/<url-hash>.json` so stage 7 can consume them without re-reading
or re-prompting the full page.

Idempotent: if `snippets/<url-hash>.json` already exists, the page is
skipped unless --force is passed (use --force after changing the model or
prompt).

Stdlib only.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


SNIPPETS_DIR = config.ROOT / "snippets"


SYSTEM_PROMPT = """\
You are processing a chess webpage to extract snippets of HUMAN COMMENTARY
about specific positions, keyed by FEN. The downstream goal is to help a
chess-engine developer debug their own engine — so we only want snippets
that would actually be useful to such a developer.

A FEN is a six-field position string of the form:
  <board> <side> <castling> <en_passant> <halfmove> <fullmove>
Example: rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1

For each FEN that appears on the page, judge whether the surrounding text
is genuinely useful for debugging an engine on that position.

INCLUDE the FEN occurrence if the surrounding text does any of:
  - Explains what is notable about the position
  - Reports a bug, perft mismatch, or unexpected engine behaviour
    involving this specific position
  - Compares expected vs actual node counts / move counts
  - Discusses how one or more engines (mis)handle the position
  - Provides historical, research, or design context for the position
  - States what edge case the position is meant to probe (e.g.
    "tests en-passant pin", "tests castling through check")

SKIP the FEN occurrence (do NOT include it) if the surrounding text is any
of these — having a FEN nearby is not enough by itself:
  - A command-line usage example demonstrating CLI syntax
    (e.g. `oliperft 6 "FEN"`, `engine bench --fen "FEN"`)
  - A bare entry in a list or table with no commentary
  - The FEN appears only as test input/output with no human discussion
  - Generic boilerplate (download instructions, build flags, licensing)
  - Documentation for tools / scripts that happen to use the FEN as a sample
  - The FEN is one of many in a bulk dump (EPD file, opening book) with
    no per-position discussion

If a page contains FENs but none of them have useful commentary, return
{"items": []}. It is correct and expected to drop FENs whose context is
just a CLI example or a bulk listing.

Return ONE JSON object, nothing else:

{
  "items": [
    {
      "fen":     "<verbatim 6-field FEN exactly as it appears on the page>",
      "snippet": "<verbatim multi-paragraph excerpt from the page that contains the useful commentary AROUND this FEN — copy text, do not paraphrase>"
    },
    ...
  ]
}

Rules:
- Snippet must be VERBATIM from the page — copy, do not paraphrase.
- Include the full surrounding commentary, not just the line that mentions the FEN.
- If the same FEN appears in two clearly distinct useful discussions, return two items.
- Skip board-only diagrams (FENs missing the trailing five fields).
- Return ONLY the JSON object — no markdown fences, no commentary.
"""


_JSON_OBJ_RE = re.compile(r"\{.*\}", re.DOTALL)


def url_hash(url: str) -> str:
    return hashlib.sha1(url.encode()).hexdigest()[:16]


def call_llm(url: str, page_text: str, max_chars: int, timeout: float) -> dict:
    """POST page text to the local /v1/chat/completions endpoint, return
    parsed JSON. Falls back to scooping the first {...} out of the response
    if the model wrapped its answer in prose despite the instructions."""
    if len(page_text) > max_chars:
        page_text = page_text[:max_chars] + "\n\n[…truncated…]"
    body = json.dumps({
        "model": config.LLM_MODEL,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": f"URL: {url}\n\n{page_text}"},
        ],
        "temperature": 0.0,
        "response_format": {"type": "json_object"},
    }).encode()
    req = urllib.request.Request(
        config.LLM_BASE_URL.rstrip("/") + "/chat/completions",
        data=body, method="POST",
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {config.LLM_API_KEY}",
        },
    )
    with urllib.request.urlopen(req, timeout=timeout) as r:
        resp = json.loads(r.read())
    content = resp["choices"][0]["message"]["content"]
    try:
        return json.loads(content)
    except json.JSONDecodeError:
        m = _JSON_OBJ_RE.search(content)
        if m:
            return json.loads(m.group(0))
        raise RuntimeError(f"LLM did not return JSON: {content[:200]!r}")


def collect_target_urls(quality_filter: set[str]) -> list[str]:
    """Return the set of source URLs that contribute at least one FEN at
    the requested context-quality level(s)."""
    urls: set[str] = set()
    with config.CORPUS_CSV.open() as f:
        for row in csv.DictReader(f):
            if row["context_quality"] not in quality_filter:
                continue
            for u in row["source_urls"].split(" | "):
                u = u.strip()
                if u:
                    urls.add(u)
    return sorted(urls)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--quality", default="high",
                    choices=("high", "medium", "both"),
                    help="which context-quality tier of FENs to extract "
                         "snippets for (default: high)")
    ap.add_argument("--limit", type=int, default=0,
                    help="cap on pages processed this run (0 = no cap)")
    ap.add_argument("--max-chars", type=int, default=80000,
                    help="truncate page text past this many chars before "
                         "sending to the LLM")
    ap.add_argument("--timeout", type=float, default=600.0)
    ap.add_argument("--force", action="store_true",
                    help="reprocess pages that already have a snippets file")
    args = ap.parse_args()

    config.ensure_dirs()
    SNIPPETS_DIR.mkdir(parents=True, exist_ok=True)

    if not config.CORPUS_CSV.exists():
        print(f"No corpus at {config.CORPUS_CSV} — run 04_corpus.py first.",
              file=sys.stderr)
        return 1

    quality_filter = ({"high", "medium"} if args.quality == "both"
                      else {args.quality})
    target_urls = collect_target_urls(quality_filter)
    print(f"Found {len(target_urls)} unique source URLs at "
          f"quality={args.quality}")

    processed = skipped = errors = empty = 0
    for url in target_urls:
        h = url_hash(url)
        page_path = config.PAGES_DIR / f"{h}.json"
        out_path = SNIPPETS_DIR / f"{h}.json"

        if not page_path.exists():
            continue
        if out_path.exists() and not args.force:
            skipped += 1
            continue

        try:
            page = json.loads(page_path.read_text())
        except json.JSONDecodeError:
            continue
        if page.get("status") != 200 or not (page.get("text") or "").strip():
            # Write an empty snippets file so we don't keep retrying.
            out_path.write_text(json.dumps({
                "url": url, "url_hash": h,
                "extracted_at": datetime.now(timezone.utc).isoformat(),
                "model": config.LLM_MODEL,
                "page_text_chars": len(page.get("text") or ""),
                "items": [],
            }))
            empty += 1
            continue

        print(f"LLM {url}", flush=True)
        try:
            data = call_llm(url, page["text"], args.max_chars, args.timeout)
        except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError,
                json.JSONDecodeError, KeyError, TimeoutError, OSError) as e:
            print(f"  → error: {type(e).__name__}: {e}", file=sys.stderr,
                  flush=True)
            errors += 1
            continue

        items = data.get("items") or []
        # Defensive: every item should have fen+snippet strings.
        items = [
            {"fen": (i.get("fen") or "").strip(),
             "snippet": (i.get("snippet") or "").strip()}
            for i in items
            if (i.get("fen") or "").strip() and (i.get("snippet") or "").strip()
        ]
        out_path.write_text(json.dumps({
            "url": url,
            "url_hash": h,
            "extracted_at": datetime.now(timezone.utc).isoformat(),
            "model": config.LLM_MODEL,
            "page_text_chars": len(page["text"]),
            "items": items,
        }, ensure_ascii=False, indent=2))
        print(f"  → {len(items)} snippet(s)", flush=True)
        processed += 1

        if args.limit and processed >= args.limit:
            break

    print(f"\nProcessed {processed}, skipped {skipped} (already cached), "
          f"empty {empty}, errors {errors}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
