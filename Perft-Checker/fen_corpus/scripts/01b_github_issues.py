#!/usr/bin/env python3
"""Stage 1b: fetch GitHub issues + comments from chess engine repos.

For every engine descriptor under Leaderboard/engines/ that points at a GitHub
repository, this script searches the repo's issues (open + closed) for ones
that mention FEN or perft, fetches the issue body and all comments, and saves
the combined text as a synthetic "page" under sources/pages/. Stage 03
(regex extract) then picks them up like any other page.

Auth: set GITHUB_TOKEN to raise the API rate limit from 60→5000/hr for the
REST endpoints and 10→30/min for /search. Without a token this still works
but is slow.

Stdlib only.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


API = "https://api.github.com"
SEARCH_TERMS = ("fen", "perft")


def _headers() -> dict[str, str]:
    h = {
        "User-Agent": config.USER_AGENT,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    if config.GITHUB_TOKEN:
        h["Authorization"] = f"Bearer {config.GITHUB_TOKEN}"
    return h


def gh_get(url: str, *, attempt: int = 1) -> tuple[int, dict, dict | list | None]:
    """GET a GitHub API URL, return (status, headers, json). Sleeps on 403
    secondary rate limits and primary-rate-limit exhaustion."""
    req = urllib.request.Request(url, headers=_headers())
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            body = r.read()
            return r.status, dict(r.headers.items()), json.loads(body) if body else None
    except urllib.error.HTTPError as e:
        hdr = dict(e.headers.items()) if e.headers else {}
        # Secondary rate limit: Retry-After header.
        if e.code in (403, 429) and attempt <= 3:
            wait = int(hdr.get("Retry-After", "30"))
            print(f"  rate-limited (HTTP {e.code}), sleeping {wait}s",
                  file=sys.stderr)
            time.sleep(wait + 1)
            return gh_get(url, attempt=attempt + 1)
        return e.code, hdr, None
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        if attempt <= 3:
            time.sleep(5 * attempt)
            return gh_get(url, attempt=attempt + 1)
        return 0, {}, None


def respect_rate_limit(headers: dict) -> None:
    """If we're nearly out of API budget, sleep until the limit resets."""
    rem = headers.get("X-RateLimit-Remaining") or headers.get("x-ratelimit-remaining")
    reset = headers.get("X-RateLimit-Reset") or headers.get("x-ratelimit-reset")
    if rem is None or reset is None:
        return
    try:
        rem_n = int(rem); reset_n = int(reset)
    except ValueError:
        return
    if rem_n <= 2:
        wait = max(reset_n - int(time.time()), 0) + 2
        print(f"  rate window exhausted; sleeping {wait}s for reset",
              file=sys.stderr)
        time.sleep(wait)


CHESS_REPOS_FILE = config.ROOT / "chess_repos.txt"


def load_repos() -> list[str]:
    """Union of (a) GitHub repos parsed out of Leaderboard/engines/*.json and
    (b) supplemental repos listed in fen_corpus/chess_repos.txt — typically
    chess libraries like python-chess / chess.js / scalachess that are
    high-signal for FEN bug reports but aren't full engines."""
    repos: set[str] = set()
    pat = re.compile(r"^https?://github\.com/([^/]+/[^/?#\s]+?)(?:\.git)?/?$")
    if config.ENGINES_DIR.exists():
        for p in sorted(config.ENGINES_DIR.glob("*.json")):
            try:
                d = json.loads(p.read_text())
            except json.JSONDecodeError:
                continue
            m = pat.match(d.get("repo", "").strip())
            if m:
                repos.add(m.group(1))
    if CHESS_REPOS_FILE.exists():
        for line in CHESS_REPOS_FILE.read_text().splitlines():
            line = line.split("#", 1)[0].strip()
            if line:
                repos.add(line)
    return sorted(repos)


def search_issues(repo: str, term: str, per_page: int = 100,
                  max_pages: int = 10) -> list[dict]:
    """Use the GitHub search API to find issues + PRs in a repo mentioning
    `term`. GitHub's /search/issues now requires `is:issue` or
    `is:pull-request`, so we run two searches and merge.

    Search results cap at 1000 total per query, so we never need more than 10
    pages of 100 results."""
    out: dict[int, dict] = {}
    for kind in ("issue", "pull-request"):
        for page in range(1, max_pages + 1):
            q = f"{term} repo:{repo} is:{kind}"
            url = (f"{API}/search/issues?"
                   f"q={urllib.parse.quote(q)}&per_page={per_page}&page={page}")
            status, hdr, data = gh_get(url)
            respect_rate_limit(hdr)
            if status != 200 or not isinstance(data, dict):
                if isinstance(data, dict) and data.get("message"):
                    print(f"  search error: {data['message']}", file=sys.stderr)
                break
            items = data.get("items") or []
            for it in items:
                out[it["number"]] = it
            if len(items) < per_page:
                break
    return list(out.values())


def get_comments(repo: str, number: int) -> list[dict]:
    """Fetch every comment on an issue (paginated)."""
    out: list[dict] = []
    page = 1
    while True:
        url = (f"{API}/repos/{repo}/issues/{number}/comments?"
               f"per_page=100&page={page}")
        status, hdr, data = gh_get(url)
        respect_rate_limit(hdr)
        if status != 200 or not isinstance(data, list):
            break
        out.extend(data)
        if len(data) < 100:
            break
        page += 1
    return out


def stitch_text(issue: dict, comments: list[dict]) -> str:
    parts = [
        f"# {issue.get('title') or ''}",
        f"State: {issue.get('state')}  Author: {(issue.get('user') or {}).get('login')}",
        "",
        issue.get("body") or "",
    ]
    for c in comments:
        author = (c.get("user") or {}).get("login")
        parts.append(f"\n--- comment by {author} ---\n")
        parts.append(c.get("body") or "")
    return "\n".join(parts)


def url_hash(url: str) -> str:
    return hashlib.sha1(url.encode()).hexdigest()[:16]


def save_page(url: str, text: str) -> bool:
    """Write the synthetic page entry. Return True if newly written."""
    out_path = config.PAGES_DIR / f"{url_hash(url)}.json"
    if out_path.exists():
        return False
    out_path.write_text(json.dumps({
        "url": url,
        "url_hash": url_hash(url),
        "fetched_at": datetime.now(timezone.utc).isoformat(),
        "status": 200,
        "content_type": "text/markdown; source=github-issue",
        "text": text,
        "outbound_promising_links": [],
    }, ensure_ascii=False))
    return True


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--repo", action="append", default=None,
                    help="restrict to specific owner/repo (repeatable). "
                         "Default: every repo in Leaderboard/engines/.")
    ap.add_argument("--max-issues-per-repo", type=int, default=200,
                    help="cap on issues fetched per repo")
    args = ap.parse_args()

    config.ensure_dirs()

    repos = args.repo or load_repos()
    if not repos:
        print("No repos to query. Pass --repo owner/name or check "
              f"{config.ENGINES_DIR}", file=sys.stderr)
        return 1

    if not config.GITHUB_TOKEN:
        print("WARNING: GITHUB_TOKEN not set — rate limit is 60 req/hr "
              "for REST and 10 req/min for /search. Expect long sleeps.",
              file=sys.stderr)

    total_saved = total_skipped = 0
    for repo in repos:
        print(f"\n=== {repo} ===")
        # Union of issues matching each search term (some issues will hit on
        # both "fen" and "perft" — deduplicate by number).
        by_number: dict[int, dict] = {}
        for term in SEARCH_TERMS:
            hits = search_issues(repo, term)
            for h in hits:
                by_number[h["number"]] = h
        issues = list(by_number.values())[:args.max_issues_per_repo]
        print(f"  matched {len(issues)} issue(s)")

        for issue in issues:
            html_url = issue["html_url"]
            if (config.PAGES_DIR / f"{url_hash(html_url)}.json").exists():
                total_skipped += 1
                continue
            comments = []
            if issue.get("comments", 0) > 0:
                comments = get_comments(repo, issue["number"])
            text = stitch_text(issue, comments)
            if save_page(html_url, text):
                total_saved += 1
                print(f"    + #{issue['number']} ({len(comments)} comments) "
                      f"{html_url}")

    print(f"\nSaved {total_saved} new issues, skipped {total_skipped} already-cached")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
