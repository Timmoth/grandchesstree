#!/usr/bin/env python3
"""Stage 5: validate every FEN in corpus.csv via Stockfish perft and emit
two EPDs (standard, chess960).

Per FEN we run perft 1, 2, 3. Any FEN that crashes Stockfish or stops it
responding is marked invalid and dropped. The runner restarts Stockfish on
crash so a single bad position doesn't kill the whole pass.

Variant detection: if the castling-rights field uses file letters (A–H, a–h)
it's X-FEN / Chess960 and goes into `epd/chess960.epd`; otherwise it goes
into `epd/standard.epd`. Stockfish runs persistently with
`UCI_Chess960=true`, which handles both notations.

Output line format (one per FEN):

  <fen> ;D1 <n1> ;D2 <n2> ;D3 <n3> ;c0 "<sources>";

Resumable — FENs already present in either output EPD are skipped.
"""

from __future__ import annotations

import argparse
import csv
import os
import queue
import re
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


# Resolution order for the Stockfish binary:
#   1. --stockfish CLI flag
#   2. $STOCKFISH_BIN env var
#   3. PerftWar/bin/stockfish/stockfish (the bundled mac build, dev laptop)
#   4. `stockfish` on PATH (Ubuntu `apt install stockfish` puts it at
#      /usr/games/stockfish which the package adds to PATH)
STOCKFISH_BUNDLED = config.ROOT.parent / "bin" / "stockfish" / "stockfish"


def resolve_stockfish(cli_path: str | None) -> Path:
    candidates: list[str | None] = [
        cli_path,
        os.environ.get("STOCKFISH_BIN"),
        str(STOCKFISH_BUNDLED) if STOCKFISH_BUNDLED.exists() else None,
        shutil.which("stockfish"),
    ]
    for c in candidates:
        if c and Path(c).exists():
            return Path(c)
    raise SystemExit(
        "Stockfish not found. Set STOCKFISH_BIN env var, pass --stockfish, "
        "or install stockfish on PATH (`apt install stockfish` on Ubuntu)."
    )

NODE_RE = re.compile(r"^Nodes searched:\s*(\d+)")

EPD_STD_FILE  = config.EPD_DIR / "standard.epd"
EPD_960_FILE  = config.EPD_DIR / "chess960.epd"
INVALID_CSV   = config.ROOT / "corpus_invalid.csv"


def is_chess960(fen: str) -> bool:
    """X-FEN castling uses file letters (A–H, a–h) — those are Chess960."""
    parts = fen.split()
    if len(parts) < 3:
        return False
    return any(c in parts[2] for c in "ABCDEFGHabcdefgh")


def dedup_key(fen: str) -> str:
    return " ".join(fen.split()[:4])


class Stockfish:
    """Persistent Stockfish subprocess with crash recovery.

    Reads stdout in a background thread because Python's text-mode pipe
    buffers data above the OS pipe, so `select` on the underlying fd
    misses lines that are already inside Python's buffer. The reader
    thread drains lines into a Queue from which we can pop with a
    proper wall-clock timeout.
    """

    def __init__(self, binary: Path) -> None:
        self.binary = binary
        self.proc: subprocess.Popen | None = None
        self.q: queue.Queue[str | None] = queue.Queue()
        self.reader: threading.Thread | None = None
        self.restarts = 0
        self._start()

    def _start(self) -> None:
        self._kill_proc()
        # Fresh queue per process — old lines from a killed SF must not
        # leak into the new one's read stream.
        self.q = queue.Queue()
        self.proc = subprocess.Popen(
            [str(self.binary)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, text=True, bufsize=1,
        )
        self.reader = threading.Thread(
            target=self._drain, args=(self.proc, self.q), daemon=True)
        self.reader.start()
        self.restarts += 1
        self._send("uci")
        if not self._wait_for("uciok", timeout=5.0):
            raise RuntimeError("stockfish failed to handshake uciok")
        self._send("setoption name UCI_Chess960 value true")
        self._send("setoption name Threads value 1")
        self._send("setoption name Hash value 16")
        if not self.ready(5.0):
            raise RuntimeError("stockfish failed to acknowledge initial isready")

    @staticmethod
    def _drain(proc: subprocess.Popen, q: queue.Queue) -> None:
        """Continuously read lines until SF exits, push to queue."""
        try:
            for line in proc.stdout:  # type: ignore[union-attr]
                q.put(line)
        except Exception:
            pass
        q.put(None)  # sentinel for EOF

    def _kill_proc(self) -> None:
        if self.proc and self.proc.poll() is None:
            try: self.proc.kill()
            except Exception: pass
            try: self.proc.wait(timeout=2)
            except Exception: pass

    def _send(self, cmd: str) -> None:
        assert self.proc and self.proc.stdin
        self.proc.stdin.write(cmd + "\n")
        self.proc.stdin.flush()

    def _readline_with_timeout(self, deadline: float) -> str | None:
        """Return the next line from SF stdout, "" on EOF, or None on
        timeout. Does not block past `deadline`."""
        remaining = deadline - time.time()
        if remaining <= 0:
            return None
        try:
            line = self.q.get(timeout=remaining)
        except queue.Empty:
            return None
        return line if line is not None else ""

    def _wait_for(self, marker: str, timeout: float) -> bool:
        deadline = time.time() + timeout
        while True:
            line = self._readline_with_timeout(deadline)
            if line is None or line == "":
                return False
            if line.strip() == marker:
                return True

    def ready(self, timeout: float = 3.0) -> bool:
        try:
            self._send("isready")
        except (BrokenPipeError, OSError):
            return False
        return self._wait_for("readyok", timeout)

    def perft_all(self, fen: str, depths: list[int],
                  timeout_per_depth: float = 60.0
                  ) -> tuple[dict[int, int], int | None]:
        """Run perft for each requested depth against `fen` and return
        ({d: nodes}, crashed_at). Stockfish stays loaded on the same position
        between depths, so this is ~3× faster than re-sending `position fen`.

        If SF stops responding mid-pass, the returned dict contains whatever
        depths completed before the crash; `crashed_at` is the depth that
        failed (or None if all completed)."""
        out: dict[int, int] = {}
        try:
            self._send(f"position fen {fen}")
            if not self.ready(3.0):
                return out, depths[0]
            for d in depths:
                self._send(f"go perft {d}")
                deadline = time.time() + timeout_per_depth
                nodes: int | None = None
                while True:
                    line = self._readline_with_timeout(deadline)
                    if line is None:
                        # Per-depth timeout. Stockfish may still be churning
                        # — kill it so the caller can restart cleanly.
                        self._kill_proc()
                        return out, d
                    if not line:            # EOF
                        return out, d
                    m = NODE_RE.match(line.strip())
                    if m:
                        nodes = int(m.group(1))
                        break
                if nodes is None:
                    return out, d
                out[d] = nodes
            return out, None
        except (BrokenPipeError, OSError):
            return out, depths[len(out)] if len(out) < len(depths) else None

    def restart(self) -> None:
        self._start()

    def quit(self) -> None:
        try: self._send("quit")
        except Exception: pass
        try: self.proc.wait(timeout=2)  # type: ignore[union-attr]
        except Exception:
            try: self.proc.kill()  # type: ignore[union-attr]
            except Exception: pass


def already_done(epd_paths: list[Path]) -> set[str]:
    done: set[str] = set()
    for p in epd_paths:
        if not p.exists():
            continue
        for line in p.read_text().splitlines():
            if not line.strip() or line.startswith("#"):
                continue
            fields = line.split(";", 1)[0].strip().split()
            if len(fields) >= 4:
                done.add(" ".join(fields[:4]))
    return done


def fmt_epd_line(fen: str, depths: dict[int, int], sources: str,
                 crashed_at: int | None = None) -> str:
    parts = [fen]
    for d in sorted(depths):
        parts.append(f";D{d} {depths[d]}")
    if sources:
        safe = sources.replace('"', "'").replace("\n", " ")[:500]
        parts.append(f';c0 "{safe}"')
    if crashed_at is not None:
        parts.append(f';c2 "stockfish-crashed-at-d{crashed_at}"')
    return " ".join(parts) + ";"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--max-depth", type=int, default=3,
                    help="deepest perft to run per FEN (default: 3)")
    ap.add_argument("--timeout-per-depth", type=float, default=60.0,
                    help="seconds to wait for a single perft to finish")
    ap.add_argument("--limit", type=int, default=0,
                    help="process at most N positions this run (0 = no cap)")
    ap.add_argument("--corpus", default=str(config.CORPUS_CSV),
                    help="path to corpus CSV (default: corpus.csv)")
    ap.add_argument("--stockfish", default=None,
                    help="override Stockfish binary path (else use "
                         "$STOCKFISH_BIN, then bundled, then PATH)")
    args = ap.parse_args()

    config.ensure_dirs()

    corpus_path = Path(args.corpus)
    if not corpus_path.exists():
        print(f"No corpus at {corpus_path} — run 04_corpus.py first.",
              file=sys.stderr)
        return 1

    sf_path = resolve_stockfish(args.stockfish)
    print(f"Using stockfish at {sf_path}")

    done = already_done([EPD_STD_FILE, EPD_960_FILE])
    print(f"Already validated: {len(done)} positions")

    sf = Stockfish(sf_path)

    counts = {"std_ok": 0, "960_ok": 0, "invalid": 0, "skipped": 0,
              "sf_crash_partial": 0}
    invalid_rows: list[tuple[str, str, str]] = []

    # Append to whichever EPD matches the variant; truncate-then-append on
    # invalid.csv each run since the set may change as Stockfish improves.
    std_out = EPD_STD_FILE.open("a")
    nf_out  = EPD_960_FILE.open("a")

    processed = 0
    try:
        with corpus_path.open() as f:
            for row in csv.DictReader(f):
                fen = row["fen"]
                key = dedup_key(fen)
                if key in done:
                    counts["skipped"] += 1
                    continue

                variant = "960" if is_chess960(fen) else "std"

                target_depths = list(range(1, args.max_depth + 1))
                depths, crashed_at = sf.perft_all(
                    fen, target_depths, args.timeout_per_depth)
                if crashed_at is not None:
                    # Restart Stockfish so the next FEN gets a clean state.
                    if sf.proc and sf.proc.poll() is not None:
                        sf.restart()
                    elif not sf.ready(2.0):
                        sf.restart()

                if not depths:
                    # Even D1 failed — Stockfish refused to parse the FEN at
                    # all. Treat as invalid and surface it.
                    counts["invalid"] += 1
                    invalid_rows.append((fen, variant, row.get("source_urls", "")))
                    print(f"  INVALID ({variant}) {fen}")
                else:
                    sources = row.get("source_urls", "")
                    line = fmt_epd_line(fen, depths, sources, crashed_at)
                    if variant == "960":
                        nf_out.write(line + "\n"); nf_out.flush()
                        counts["960_ok"] += 1
                    else:
                        std_out.write(line + "\n"); std_out.flush()
                        counts["std_ok"] += 1
                    done.add(key)
                    if crashed_at is not None:
                        counts["sf_crash_partial"] += 1
                        deepest = max(depths)
                        print(f"  SF CRASHED at d{crashed_at} ({variant}, "
                              f"kept D1..D{deepest}) {fen}")

                processed += 1
                if processed % 50 == 0:
                    print(f"  [{processed}] std={counts['std_ok']} "
                          f"960={counts['960_ok']} invalid={counts['invalid']} "
                          f"crash={counts['sf_crash_partial']} "
                          f"restarts={sf.restarts - 1}", flush=True)
                if args.limit and processed >= args.limit:
                    break
    finally:
        std_out.close(); nf_out.close()
        sf.quit()

    # Write invalid list (truncate each run).
    if invalid_rows:
        with INVALID_CSV.open("w", newline="") as f:
            w = csv.writer(f)
            w.writerow(["fen", "variant", "source_urls"])
            w.writerows(invalid_rows)

    print(f"\nstandard EPD:        {counts['std_ok']}  → {EPD_STD_FILE}")
    print(f"chess960 EPD:        {counts['960_ok']}  → {EPD_960_FILE}")
    print(f"   (of which partial — SF crashed mid-depth: "
          f"{counts['sf_crash_partial']})")
    print(f"invalid (D1 failed): {counts['invalid']}  → {INVALID_CSV}")
    print(f"skipped (already done): {counts['skipped']}")
    print(f"stockfish restarts:  {sf.restarts - 1}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
