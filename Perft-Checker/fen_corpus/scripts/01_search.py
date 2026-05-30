#!/usr/bin/env python3
"""Stage 1: search the web for pages that likely contain problematic FENs.

Reads queries from queries.txt, hits the configured search backend, and
appends new URLs to sources/urls.jsonl. Re-running is safe — URLs already in
the file are not duplicated.

Backends:
- ddg     DuckDuckGo HTML endpoint, no API key (default).
- brave   Brave Search API. Needs BRAVE_API_KEY.
- tavily  Tavily Search API. Needs TAVILY_API_KEY.

Stdlib only.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from html.parser import HTMLParser
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


def _http(url: str, *, data: bytes | None = None, headers: dict[str, str] | None = None,
          method: str = "GET", timeout: float = 20.0) -> tuple[int, bytes]:
    req = urllib.request.Request(url, data=data, method=method,
                                 headers={"User-Agent": config.USER_AGENT, **(headers or {})})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read() if e.fp else b""


class _DDGParser(HTMLParser):
    """Pull result URLs out of the DDG HTML endpoint's response.

    DDG wraps each result in a redirect link of the form
        //duckduckgo.com/l/?uddg=<percent-encoded-url>&rut=...
    or, more rarely, a direct https:// href. We grab both forms from any
    anchor carrying class="result__a".
    """

    def __init__(self) -> None:
        super().__init__()
        self.hits: list[tuple[str, str]] = []  # (url, snippet-placeholder)
        self._capture = False
        self._buf: list[str] = []
        self._href = ""

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag != "a":
            return
        d = dict(attrs)
        if "result__a" not in (d.get("class") or ""):
            return
        href = d.get("href") or ""
        if href.startswith("//"):
            href = "https:" + href
        if "uddg=" in href:
            q = urllib.parse.urlparse(href).query
            for k, v in urllib.parse.parse_qsl(q):
                if k == "uddg":
                    href = v
                    break
        self._href = href
        self._capture = True
        self._buf = []

    def handle_endtag(self, tag: str) -> None:
        if tag == "a" and self._capture:
            self.hits.append((self._href, "".join(self._buf).strip()))
            self._capture = False
            self._href = ""

    def handle_data(self, data: str) -> None:
        if self._capture:
            self._buf.append(data)


def search_ddg(query: str, n: int) -> list[dict]:
    body = urllib.parse.urlencode({"q": query}).encode()
    status, raw = _http(
        "https://html.duckduckgo.com/html/",
        data=body, method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )
    if status != 200:
        print(f"  ddg: HTTP {status}", file=sys.stderr)
        return []
    p = _DDGParser()
    p.feed(raw.decode("utf-8", errors="replace"))
    out: list[dict] = []
    for url, snippet in p.hits[:n]:
        if url.startswith(("http://", "https://")):
            out.append({"url": url, "snippet": snippet})
    return out


def search_brave(query: str, n: int) -> list[dict]:
    if not config.BRAVE_API_KEY:
        print("  brave: BRAVE_API_KEY not set", file=sys.stderr)
        return []
    url = "https://api.search.brave.com/res/v1/web/search?" + urllib.parse.urlencode(
        {"q": query, "count": min(n, 20)})
    status, raw = _http(url, headers={
        "X-Subscription-Token": config.BRAVE_API_KEY,
        "Accept": "application/json",
    })
    if status != 200:
        print(f"  brave: HTTP {status}", file=sys.stderr)
        return []
    data = json.loads(raw)
    return [{"url": r["url"], "snippet": r.get("description", "")}
            for r in data.get("web", {}).get("results", [])[:n]]


def search_tavily(query: str, n: int) -> list[dict]:
    if not config.TAVILY_API_KEY:
        print("  tavily: TAVILY_API_KEY not set", file=sys.stderr)
        return []
    body = json.dumps({
        "api_key": config.TAVILY_API_KEY,
        "query": query,
        "max_results": n,
    }).encode()
    status, raw = _http(
        "https://api.tavily.com/search",
        data=body, method="POST",
        headers={"Content-Type": "application/json"},
    )
    if status != 200:
        print(f"  tavily: HTTP {status}", file=sys.stderr)
        return []
    data = json.loads(raw)
    return [{"url": r["url"], "snippet": r.get("content", "")}
            for r in data.get("results", [])[:n]]


SEARCHERS = {"ddg": search_ddg, "brave": search_brave, "tavily": search_tavily}


def load_queries() -> list[str]:
    out: list[str] = []
    for line in config.QUERIES_FILE.read_text().splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            out.append(line)
    return out


def load_known_urls() -> set[str]:
    if not config.QUEUE_FILE.exists():
        return set()
    seen: set[str] = set()
    for line in config.QUEUE_FILE.read_text().splitlines():
        if not line.strip():
            continue
        try:
            seen.add(json.loads(line)["url"])
        except (json.JSONDecodeError, KeyError):
            continue
    return seen


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--max-per-query", type=int, default=20)
    ap.add_argument("--sleep", type=float, default=2.0,
                    help="seconds between queries to be polite")
    ap.add_argument("--backend", default=config.SEARCH_BACKEND,
                    choices=sorted(SEARCHERS.keys()))
    args = ap.parse_args()

    config.ensure_dirs()
    searcher = SEARCHERS[args.backend]
    queries = load_queries()
    known = load_known_urls()
    now = lambda: datetime.now(timezone.utc).isoformat()

    appended = 0
    with config.QUEUE_FILE.open("a") as f:
        for q in queries:
            print(f"[{args.backend}] {q}")
            results = searcher(q, args.max_per_query)
            for r in results:
                if r["url"] in known:
                    continue
                known.add(r["url"])
                f.write(json.dumps({
                    "url": r["url"],
                    "query": q,
                    "snippet": r["snippet"],
                    "discovered_at": now(),
                    "backend": args.backend,
                }) + "\n")
                appended += 1
            time.sleep(args.sleep)

    print(f"Appended {appended} new URLs → {config.QUEUE_FILE}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
