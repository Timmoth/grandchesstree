#!/usr/bin/env python3
"""Stage 2: priority-queue web crawler over sources/queue.jsonl.

Every URL gets a numeric score from `SCORE_KEYWORDS` (strong signals like
`perft`/`epd`/`kiwipete`/`/issues/` weighted high, generic `chess`/`position`
weighted zero), plus a small domain bonus and a big file-extension bonus
for `.epd`/`.fen`. The crawler pops URLs highest-score-first and drops
anything below `--min-score`.

When a page is fetched, its outbound links inherit a fraction
(`--inherit-factor`, default 0.3) of the page's own score on top of the
link's own base score. So a high-yield page like `/Perft_Results` boosts
the URLs it points at, and an old leftover URL in the queue gets promoted
if it's re-linked from a fresh high-yield discovery.

Resumable — pages already cached are skipped; the queue is rebuilt from
queue.jsonl on each session.
"""

from __future__ import annotations

import argparse
import hashlib
import heapq
import html
import itertools
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from html.parser import HTMLParser
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


# Weighted scoring instead of a flat allowlist. Each keyword that appears in
# the URL path / query / anchor text contributes its weight to the URL's
# score. The crawler processes URLs highest-score first and drops anything
# below --min-score outright.
#
# The previous flat keyword set let in author bio pages and engine
# descriptions on chessprogramming.org because they all contained generic
# tokens like "chess". Strong signals like `perft` / `epd` / `kiwipete` /
# `/issues/` deserve much more weight than `chess` (which is now ~0).
SCORE_KEYWORDS: dict[str, float] = {
    # very strong — pages that almost certainly hold FENs
    "perft":         10.0,
    "epd":           10.0,
    "kiwipete":      10.0,
    "fischer":        8.0,
    "perft_results":  6.0,
    "test_positions": 6.0,
    "testpositions":  6.0,
    "bratko":         6.0,
    "wac":            6.0,
    "ecm":            6.0,
    # github issue / PR pages — bug reports very often include FENs
    "/issues/":       5.0,
    "/pull/":         3.0,
    # strong — discussions or test suites likely to contain positions
    "movegen":        5.0,
    "move_gen":       5.0,
    "fen":            5.0,
    "test_suite":     4.0,
    "test-suite":     4.0,
    "testsuite":      4.0,
    "viewtopic":      3.0,   # talkchess thread permalink
    # moderate
    "960":            3.0,
    "chess960":       4.0,
    "castl":          2.0,
    "endgame":        2.0,
    "tactic":         2.0,
    "puzzle":         2.0,
    "stockfish":      2.0,
    "debug":          2.0,
    "draft":          2.0,
    # very weak — these are too generic to be reliable signals on their
    # own. Keeping them at 0 means they don't lift a URL above the
    # min-score threshold unless paired with something stronger.
    "chess":          0.0,
    "position":       0.0,
    "opening":        0.0,
    "test":           0.0,
    "suite":          0.0,
    "engine":         0.0,
}

# Hostname bonuses — sites where even a generic-looking URL is more likely
# than usual to actually contain FENs.
DOMAIN_BONUS: dict[str, float] = {
    "talkchess.com":            2.0,
    "chess.stackexchange.com":  2.0,
    "lichess.org":              1.0,
    "rocechess.ch":             4.0,
    "gist.github.com":          2.0,
    "raw.githubusercontent.com": 3.0,
}

# Bonus by URL file extension — `.epd` files are pure FEN test suites.
FILE_EXT_BONUS: dict[str, float] = {
    ".epd": 12.0,
    ".fen":  8.0,
    ".pgn":  1.0,
    ".txt":  1.0,
}

# Fraction of the parent page's score that propagates into a newly-discovered
# outbound link. Lets a high-yield page (say chessprogramming.org/Perft) bump
# every link it points at above older FIFO leftovers.
DEFAULT_INHERIT_FACTOR = 0.3
DEFAULT_MIN_SCORE = 2.0

# URL patterns we never follow.
SKIP_RE = re.compile(
    r"""
    /(Special|File|Help|Category|Template|Talk|User):     # wiki noise (path)
    | title=(Special|File|Help|Category|Template|Talk|User):  # wiki noise (query)
    | [?&]action=(edit|history|info|raw|delete|protect|email|watch|unwatch)
    | [?&]oldid=
    | [?&]diff=
    | [?&]printable=
    | [?&]mobileaction=
    | [?&]returnto=
    | [?&]redlink=
    | /(login|signin|signup|register|password|account|settings|preferences)
    | \.(pdf|zip|gz|tar|tgz|7z|rar|png|jpg|jpeg|gif|svg|webp|css|js|mp4|mp3|wav|woff2?|ttf|eot|ico|bmp)([?\#]|$)
    | /(images|assets|static|favicon)/
    | github\.com/[^/]+/[^/]+/commits([/?]|$)  # GitHub commit-list pages, never have FENs
    | github\.com/[^/]+/[^/]+/commit/[0-9a-f]+   # individual commit diffs
    | github\.com/[^/]+/[^/]+/(blob|raw|tree)/[0-9a-f]{40}  # commit-pinned views
    | github\.com/search                # GitHub search pages — combinatorial trap
    | github\.com/[^/]+/[^/]+/(stargazers|watchers|forks|network|graphs|pulse|community|projects|security|tags|releases|activity|labels|milestones|compare|branches/(active|stale|all))
    | github\.com/[^/]+/[^/]+/(issues|pulls)\?  # filtered issue/PR listings (use API instead)
    | github\.com/[^/]+\?(tab|repo_name)=  # GitHub user profile tab links
    | github\.com/users/[^/]+/packages
    | github\.com/contact/report-content
    """,
    re.IGNORECASE | re.VERBOSE,
)

# Query parameters that don't change the page content. Stripped during URL
# canonicalization so different session IDs / tracking tokens collapse to the
# same cache entry instead of being re-fetched forever.
_STRIP_PARAMS = {
    "sid", "session", "sessid", "phpsessid", "s",   # forum session ids
    "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term",
    "fbclid", "gclid", "ref", "ref_src", "ref_url",
}

_SKIP_TAGS = {"script", "style", "noscript", "svg"}


class _PageParser(HTMLParser):
    """Extract both visible text (paragraph-respecting) and outbound links
    with their anchor text in one pass."""

    def __init__(self) -> None:
        super().__init__()
        self.text_parts: list[str] = []
        self.links: list[tuple[str, str]] = []  # (href, anchor_text)
        self._skip_depth = 0
        self._in_a = False
        self._a_buf: list[str] = []
        self._a_href = ""

    def handle_starttag(self, tag: str, attrs):  # noqa: ANN001
        if tag in _SKIP_TAGS:
            self._skip_depth += 1
            return
        if tag == "a" and self._skip_depth == 0:
            href = ""
            for k, v in attrs:
                if k == "href" and v:
                    href = v
                    break
            if href:
                self._in_a = True
                self._a_href = href
                self._a_buf = []
                return
        if tag in ("p", "br", "li", "tr", "div", "h1", "h2", "h3", "h4", "h5", "h6"):
            self.text_parts.append("\n")

    def handle_endtag(self, tag: str) -> None:
        if tag in _SKIP_TAGS and self._skip_depth > 0:
            self._skip_depth -= 1
            return
        if tag == "a" and self._in_a:
            anchor = " ".join("".join(self._a_buf).split())
            self.links.append((self._a_href, anchor))
            self._in_a = False
            self._a_href = ""
            self._a_buf = []
        if tag in ("p", "li", "tr", "div", "h1", "h2", "h3", "h4", "h5", "h6"):
            self.text_parts.append("\n")

    def handle_data(self, data: str) -> None:
        if self._skip_depth:
            return
        if self._in_a:
            self._a_buf.append(data)
        self.text_parts.append(data)

    def text(self) -> str:
        raw = "".join(self.text_parts)
        lines = [" ".join(l.split()) for l in raw.splitlines()]
        return "\n".join(l for l in lines if l)


def url_hash(url: str) -> str:
    return hashlib.sha1(url.encode()).hexdigest()[:16]


def _rewrite(url: str) -> str:
    p = urllib.parse.urlparse(url)
    if p.netloc in ("www.reddit.com", "reddit.com", "new.reddit.com"):
        return urllib.parse.urlunparse(p._replace(netloc="old.reddit.com"))
    return url


def fetch(url: str, timeout: float) -> tuple[int, str, str, list[tuple[str, str]]]:
    """GET url, return (status, content_type, text, outbound_links)."""
    req = urllib.request.Request(_rewrite(url), headers={
        "User-Agent": config.USER_AGENT,
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "en-US,en;q=0.5",
    })
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            body = r.read()
            ct = r.headers.get("Content-Type", "")
            status = r.status
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get("Content-Type", "") if e.headers else "", "", []
    except (urllib.error.URLError, TimeoutError, ConnectionError, OSError) as e:
        return 0, "", f"<fetch error: {type(e).__name__}: {e}>", []
    enc = "utf-8"
    if "charset=" in ct:
        enc = ct.split("charset=", 1)[1].split(";")[0].strip() or "utf-8"
    try:
        raw = body.decode(enc, errors="replace")
    except LookupError:
        raw = body.decode("utf-8", errors="replace")
    if "html" in ct.lower() or raw.lstrip().lower().startswith(("<html", "<!doctype")):
        p = _PageParser()
        try:
            p.feed(raw)
        except Exception:
            pass
        return status, ct, p.text(), p.links
    # Plain-text response (raw .epd files, gist raw, etc.) — no links to follow.
    return status, ct, raw, []


def url_base_score(url: str, anchor: str = "") -> float:
    """Score this URL from its own properties — keywords in path/query/anchor,
    domain bonus, file extension. Returns 0 for URLs that match SKIP_RE so
    they're filtered out completely regardless of context."""
    if SKIP_RE.search(url):
        return 0.0
    p = urllib.parse.urlparse(url)
    blob = (p.path + " " + p.query + " " + (anchor or "")).lower()
    score = 0.0
    for kw, w in SCORE_KEYWORDS.items():
        if kw in blob:
            score += w
    host = p.netloc.lower().removeprefix("www.")
    score += DOMAIN_BONUS.get(host, 0.0)
    path_lower = p.path.lower()
    for ext, b in FILE_EXT_BONUS.items():
        if path_lower.endswith(ext):
            score += b
            break
    return score


def normalize(url: str, base: str) -> str | None:
    """Resolve a relative URL against `base` and canonicalize it.

    Canonicalization drops fragments, strips session-id / tracking query
    parameters (so the same page with a fresh `sid=` doesn't get re-fetched),
    and rewrites a few github-specific patterns to their canonical form
    (commit-pinned `/blob/<sha>/X` → `/blob/master/X`) so we don't fetch
    100 copies of the same .epd file."""
    try:
        # Decode HTML entities first (`&amp;` → `&`). Python's html.parser
        # leaves entities in attribute values, so hrefs scraped from <a href>
        # often arrive entity-encoded.
        raw = html.unescape(url.strip())
        abs_url = urllib.parse.urljoin(base, raw)
        abs_url = urllib.parse.urldefrag(abs_url).url
    except Exception:
        return None
    p = urllib.parse.urlparse(abs_url)
    if p.scheme not in ("http", "https"):
        return None
    if not p.netloc:
        return None

    # Strip noise query params (session IDs, tracking).
    if p.query:
        pairs = urllib.parse.parse_qsl(p.query, keep_blank_values=True)
        pairs = [(k, v) for k, v in pairs if k.lower() not in _STRIP_PARAMS]
        new_query = urllib.parse.urlencode(pairs)
        p = p._replace(query=new_query)

    # Canonicalize github URLs: blob/<sha>/X → blob/master/X,
    # raw/refs/heads/master/X → raw/master/X, raw.githubusercontent.com → blob.
    host = p.netloc.lower().removeprefix("www.")
    if "talkchess.com" in host:
        # talkchess serves identical content at www, bare host, http, https.
        # Canonicalize all to https://www.talkchess.com so the same thread
        # doesn't get fetched four times under different cache keys.
        p = p._replace(scheme="https", netloc="www.talkchess.com")

    # phpBB forums (talkchess, open-chess.org, hiarcs.net, open-aurec.com…)
    # share the same viewtopic.php URL scheme. A thread can be reached as:
    #   ?t=THREAD                          — the thread, canonical
    #   ?t=T&p=POST                        — thread anchored at a post
    #   ?p=POST                            — post permalink (redirects)
    #   ?topic_view=threads&t=T            — alternate flat layout
    #   ?t=T&f=FORUM                       — same thread, decorated
    # All render the same thread content. Collapse to `?t=T`.
    if "viewtopic.php" in p.path:
        pairs = urllib.parse.parse_qsl(p.query, keep_blank_values=True)
        keys = {k for k, _ in pairs}
        if "t" in keys:
            pairs = [(k, v) for k, v in pairs
                     if k not in ("p", "topic_view", "f")]
            p = p._replace(query=urllib.parse.urlencode(pairs))
        elif "p" in keys:
            # post-permalink with no thread — redirects to thread, which
            # we'll see via `?t=` anyway. Drop.
            return None

    if host == "github.com":
        # Strip 40-char commit hashes from blob/raw paths and re-point at
        # master, then collapse the HTML "/blob/" view to the equivalent
        # "/raw/" URL so the same file isn't fetched twice (once as HTML,
        # once as text). The .epd content is identical either way and the
        # regex extractor doesn't care.
        path = re.sub(
            r"^/([^/]+)/([^/]+)/(blob|raw)/[0-9a-f]{40}/",
            r"/\1/\2/\3/master/", p.path,
        )
        path = re.sub(
            r"^/([^/]+)/([^/]+)/raw/refs/heads/",
            r"/\1/\2/raw/", path,
        )
        path = re.sub(
            r"^/([^/]+)/([^/]+)/blob/",
            r"/\1/\2/raw/", path,
        )
        p = p._replace(path=path)

    return urllib.parse.urlunparse(p)


class PriorityQueue:
    """Best-score-first URL queue with stale-entry skipping.

    Each URL has at most one *current* score (in `score_of`). The heap may
    hold older entries with worse scores from earlier discoveries; those are
    detected and discarded on pop.

    Pushing a URL with a higher score than the existing record updates the
    record and pushes a new heap entry. That's how a URL already in the
    queue gets promoted when a high-yield page links to it.
    """

    def __init__(self, min_score: float) -> None:
        self.min_score = min_score
        self.score_of: dict[str, float] = {}
        self.heap: list[tuple[float, int, str]] = []
        self._counter = itertools.count()

    def push(self, url: str, score: float) -> bool:
        if score < self.min_score:
            return False
        old = self.score_of.get(url)
        if old is not None and old >= score:
            return False
        self.score_of[url] = score
        # heapq is min-heap; negate so highest scores come out first.
        heapq.heappush(self.heap, (-score, next(self._counter), url))
        return True

    def pop(self) -> tuple[str, float] | None:
        while self.heap:
            neg, _, url = heapq.heappop(self.heap)
            current = self.score_of.get(url)
            if current is None or current != -neg:
                continue  # stale — superseded by a higher score
            return url, current
        return None

    def __len__(self) -> int:
        # Approximation: the score_of dict tracks URLs still pending an
        # outcome; popping removes via stale-skip rather than from this dict.
        # Good enough for progress logging.
        return len(self.score_of)


def load_initial_queue(min_score: float
                       ) -> tuple[PriorityQueue, set[str], int]:
    """Build the priority queue from queue.jsonl.

    Existing queue entries are scored from URL + stored anchor/snippet/query
    only — no parent-score propagation, since we don't know what page first
    surfaced them. New discoveries during the current crawl DO get
    propagation, which is what lets fresh high-yield finds outrank older
    leftovers.
    """
    pq = PriorityQueue(min_score=min_score)
    known: set[str] = set()
    filtered = 0
    if not config.QUEUE_FILE.exists():
        return pq, known, filtered
    for line in config.QUEUE_FILE.read_text().splitlines():
        if not line.strip():
            continue
        try:
            rec = json.loads(line)
        except json.JSONDecodeError:
            continue
        u = rec.get("url")
        if not u:
            continue
        # Re-normalize old entries so URLs that differ only in tracking
        # params / commit hashes / blob-vs-raw collapse to one queue slot.
        u_norm = normalize(u, u)
        if not u_norm:
            continue
        u = u_norm
        known.add(u)
        if (config.PAGES_DIR / f"{url_hash(u)}.json").exists():
            continue
        ctx = (rec.get("anchor") or rec.get("snippet")
               or rec.get("query") or "")
        score = url_base_score(u, ctx)
        # If the same URL appears multiple times in queue.jsonl, the highest
        # base score wins (pq.push is monotonic).
        if not pq.push(u, score):
            if u not in pq.score_of:
                filtered += 1
    return pq, known, filtered


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--max-pages", type=int, default=200,
                    help="cap on URLs fetched this run (0 = no cap)")
    ap.add_argument("--timeout", type=float, default=20.0)
    ap.add_argument("--sleep-per-host", type=float, default=2.0,
                    help="minimum seconds between requests to the same host")
    ap.add_argument("--per-host-quota", type=int, default=150,
                    help="cap on requests to any one host per session "
                         "(URLs left in the queue for a future run)")
    ap.add_argument("--min-score", type=float, default=DEFAULT_MIN_SCORE,
                    help="drop URLs whose computed score is below this")
    ap.add_argument("--inherit-factor", type=float,
                    default=DEFAULT_INHERIT_FACTOR,
                    help="fraction of parent page's score that propagates "
                         "to each outbound link discovered there")
    args = ap.parse_args()

    config.ensure_dirs()

    pq, known, filtered = load_initial_queue(min_score=args.min_score)
    print(f"Queue: {len(pq)} pending (score ≥ {args.min_score}), "
          f"{filtered} below threshold, {len(known)} known total")

    last_hit: dict[str, float] = {}
    host_count: dict[str, int] = {}
    fetched = errors = discovered = quota_skips = 0

    with config.QUEUE_FILE.open("a") as qf:
        while args.max_pages == 0 or fetched < args.max_pages:
            item = pq.pop()
            if item is None:
                break
            url, current_score = item
            page_path = config.PAGES_DIR / f"{url_hash(url)}.json"
            if page_path.exists():
                continue

            host = urllib.parse.urlparse(url).netloc
            if host_count.get(host, 0) >= args.per_host_quota:
                # Don't burn out a single source — leave the URL in the queue
                # (no page-cache file written) so a later run can pick it up.
                # We DO NOT re-push to pq: this URL is now off the heap and
                # will be re-loaded from queue.jsonl on the next session.
                quota_skips += 1
                continue
            wait = args.sleep_per_host - (time.time() - last_hit.get(host, 0))
            if wait > 0:
                time.sleep(wait)
            last_hit[host] = time.time()
            host_count[host] = host_count.get(host, 0) + 1

            print(f"GET [{current_score:.1f}] {url}", flush=True)
            status, ct, text, links = fetch(url, args.timeout)

            new_here = 0
            kept_links: list[dict] = []
            for href, anchor in links:
                abs_url = normalize(href, url)
                if not abs_url:
                    continue
                if (config.PAGES_DIR / f"{url_hash(abs_url)}.json").exists():
                    continue
                base = url_base_score(abs_url, anchor)
                if base <= 0:
                    continue
                score = base + current_score * args.inherit_factor
                old_score = pq.score_of.get(abs_url)
                if not pq.push(abs_url, score):
                    continue
                # Only count "newly known" URLs once; re-pushed (promoted)
                # ones are tracked as improvements, not new discoveries.
                if abs_url not in known:
                    known.add(abs_url)
                    new_here += 1
                qf.write(json.dumps({
                    "url": abs_url,
                    "source": "link",
                    "from": url,
                    "anchor": anchor[:120],
                    "score": round(score, 2),
                    "discovered_at": datetime.now(timezone.utc).isoformat(),
                }) + "\n")
                kept_links.append({
                    "url": abs_url,
                    "anchor": anchor[:120],
                    "score": round(score, 2),
                })
            qf.flush()

            page_path.write_text(json.dumps({
                "url": url,
                "url_hash": url_hash(url),
                "fetched_at": datetime.now(timezone.utc).isoformat(),
                "status": status,
                "content_type": ct,
                "text": text,
                "outbound_promising_links": kept_links,
            }, ensure_ascii=False))

            fetched += 1
            discovered += new_here
            if status != 200:
                errors += 1
                print(f"  → status {status}")
            elif new_here:
                print(f"  → +{new_here} new links (queue now ~{len(pq)})")

    print(f"\nFetched {fetched}, errors {errors}, "
          f"discovered {discovered} new URLs, "
          f"queue remaining ~{len(pq)}, "
          f"per-host-quota skips {quota_skips}")
    if host_count:
        print("Per-host counts this run:")
        for h, n in sorted(host_count.items(), key=lambda x: -x[1])[:10]:
            print(f"  {n:>4}  {h}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
