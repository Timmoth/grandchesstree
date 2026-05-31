#!/usr/bin/env python3
"""Stage 8: compute perft d1…dN for every FEN in static_analysis.jsonl
using TGCT engine instances in parallel.

For each (FEN, depth) pair where the d_<N> field is still 0, queue a
single-threaded TGCT perft. A pool of worker threads each owns one
long-lived TGCT subprocess and pulls tasks off a shared queue. Results
stream to `perft_results.jsonl` as they're computed; on completion (or
on ctrl-C) the results are merged back into `static_analysis.jsonl`
in place.

Resumable: `perft_results.jsonl` is append-only and indexed by
(fen_key, depth); on restart we read it and skip work that's already
been done.

Crash-resilient: a TGCT instance that segfaults / hangs is killed and
replaced; the offending (fen, depth) task is recorded with nodes=null
and the worker resumes.

Stdlib only.
"""

from __future__ import annotations

import argparse
import json
import os
import queue
import re
import signal
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import config  # noqa: E402


STATIC_ANALYSIS  = config.ROOT / "static_analysis.jsonl"
RESULTS_FILE     = config.ROOT / "perft_results.jsonl"
DEFAULT_TGCT     = (config.ROOT.parent / "bin"
                    / "tgct_engine_local" / "GrandChessTree.Engine")


# TGCT `divide:<depth>:<mb_hash>:<fen>` output is line-based:
#   e2e4 12345
#   e2e3 11111
#   …                                  (one per root move, uci_move + count)
#   -----results-----
#   nodes: 197281                      (sum across moves)
#   nps: …
#   time: 1ms
#   hash: …
#   fen: …
#   -----------------
_END_RE    = re.compile(r"^-{15,}$")
_NODES_RE  = re.compile(r"^nodes:\s*(\d+)\s*$", re.IGNORECASE)
_DIVIDE_RE = re.compile(r"^([a-h][1-8][a-h][1-8][rnbq]?)\s+(\d+)\s*$")


# -------------- worker --------------

class TgctWorker:
    """One TGCT subprocess + a background line-reader thread.

    The reader thread is essential: Python's text-mode pipe buffers above
    the OS pipe boundary, so a `select` on the underlying fd misses lines
    that are already in the Python buffer. Draining into a Queue with
    blocking readline() and consuming with Queue.get(timeout=…) is
    reliable.
    """

    def __init__(self, binary: Path, worker_id: int, per_task_timeout: float):
        self.binary = binary
        self.worker_id = worker_id
        self.timeout = per_task_timeout
        self.proc: subprocess.Popen | None = None
        self.q: queue.Queue[str | None] = queue.Queue()
        self.reader: threading.Thread | None = None
        self.restarts = 0
        self._start()

    def _start(self) -> None:
        self._kill_proc()
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

    @staticmethod
    def _drain(proc: subprocess.Popen, q: queue.Queue) -> None:
        try:
            assert proc.stdout is not None
            for line in proc.stdout:
                q.put(line)
        except Exception:
            pass
        q.put(None)

    def _kill_proc(self) -> None:
        if self.proc and self.proc.poll() is None:
            try: self.proc.kill()
            except Exception: pass
            try: self.proc.wait(timeout=2)
            except Exception: pass

    def _send(self, cmd: str) -> bool:
        try:
            assert self.proc and self.proc.stdin
            self.proc.stdin.write(cmd)
            self.proc.stdin.flush()
            return True
        except (BrokenPipeError, OSError):
            return False

    def perft(self, fen: str, depth: int,
              cache_mb: int = 0,
              mt_threads: int = 0
              ) -> tuple[int | None, dict[str, int], float, str | None]:
        """Run a single perft. Returns (nodes, divide, elapsed_sec, error_msg).
        `divide` is `{uci_move: child_node_count}` summed by TGCT to `nodes`.
        On timeout / EOF / crash the worker's subprocess is killed and a
        fresh one started; the caller then continues with the next task.

        `cache_mb` sets TGCT's transposition table size for this call.
        Each worker is its own TGCT instance, so each gets its own TT —
        total memory cost is `cache_mb × --threads`.

        When `mt_threads > 0`, sends `divide_mt:<d>:<mb>:<mt-threads>:<fen>`
        instead — one TGCT instance fans out across mt-threads CPU threads.
        Pair with `--threads 1` so a single MT instance gets the whole box,
        the obvious shape for crunching d7 long-tail positions."""
        cmd = (f"divide_mt:{depth}:{cache_mb}:{mt_threads}:{fen}\n"
               if mt_threads > 0
               else f"divide:{depth}:{cache_mb}:{fen}\n")
        if not self._send(cmd):
            self._start()
            return None, {}, 0.0, "broken_pipe"

        deadline = time.time() + self.timeout
        start    = time.time()
        nodes: int | None = None
        divide: dict[str, int] = {}

        while True:
            remaining = deadline - time.time()
            if remaining <= 0:
                self._kill_proc()
                self._start()
                return None, divide, time.time() - start, "timeout"
            try:
                line = self.q.get(timeout=remaining)
            except queue.Empty:
                self._kill_proc()
                self._start()
                return None, divide, time.time() - start, "timeout"
            if line is None:
                # EOF — subprocess died (probably a TGCT segfault on this
                # position). Restart for the next task.
                self._start()
                return (nodes if nodes is not None else None,
                        divide,
                        time.time() - start,
                        "eof")
            stripped = line.strip()
            if not stripped:
                continue
            md = _DIVIDE_RE.match(stripped)
            if md:
                divide[md.group(1)] = int(md.group(2))
                continue
            m = _NODES_RE.match(stripped)
            if m:
                nodes = int(m.group(1))
                continue
            if _END_RE.match(stripped):
                return nodes, divide, time.time() - start, None
            # Any other content (nps, time, hash, fen, banner lines) is ignored.

    def close(self) -> None:
        try:
            if self._send("quit\n"):
                time.sleep(0.05)
        finally:
            self._kill_proc()


# -------------- driver --------------

def load_existing_results() -> dict[str, dict[int, int]]:
    """fen_key → {depth: nodes} of already-computed entries."""
    out: dict[str, dict[int, int]] = {}
    if not RESULTS_FILE.exists():
        return out
    with RESULTS_FILE.open() as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            n = r.get("nodes")
            if n is None:
                continue
            out.setdefault(r["fen_key"], {})[int(r["d"])] = int(n)
    return out


def build_task_list(max_depth: int,
                    quality_filter: set[str],
                    limit: int,
                    existing: dict[str, dict[int, int]],
                    no_mate_skip: bool = False,
                    ) -> list[tuple[str, str, int]]:
    """Return (fen_key, fen, depth) tasks needing computation. Skips
    depths already present in either static_analysis (non-zero) or the
    perft_results sidecar. Folds those existing values into `existing`
    so the final merge sees them.

    The default mate-skip optimisation looks at the static_analysis row
    and skips d_n if d_{n-1} = 0 (because mate/stalemate at shallower
    depth means d_n is also zero). Pass `no_mate_skip=True` to disable
    that — useful for a fresh full regen where d_n fields have all been
    reset to zero and the filter would incorrectly drop everything but
    d=1."""
    tasks: list[tuple[str, str, int]] = []
    seen_fen_keys = 0
    with STATIC_ANALYSIS.open() as f:
        for line in f:
            if not line.strip():
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            if row.get("context_quality") not in quality_filter:
                continue
            key = row["fen_key"]
            fen = row["fen"]
            for d in range(1, max_depth + 1):
                # `in` (not `> 0`): any prior result counts as "tried" —
                # otherwise mate/stalemate positions that legitimately
                # returned nodes=0 last run get re-queued forever.
                if d in existing.get(key, {}):
                    continue
                v = row.get(f"d{d}", 0)
                if v and v > 0:
                    # Already populated in static_analysis itself.
                    existing.setdefault(key, {})[d] = int(v)
                    continue
                if not no_mate_skip:
                    # Skip mate/stalemate / unparseable positions: if d_{d-1}
                    # has already been computed and came out 0 there are no
                    # legal moves at any deeper depth either. Trying d_n is
                    # guaranteed-zero work.
                    if d > 1 and row.get(f"d{d-1}", 0) == 0:
                        continue
                tasks.append((key, fen, d))
            seen_fen_keys += 1
            if limit and seen_fen_keys >= limit:
                break
    return tasks


def merge_into_static_analysis(max_depth: int) -> int:
    """Re-read perft_results.jsonl (the canonical source of computed
    values), then walk static_analysis.jsonl row-by-row and overwrite
    d1…d<max_depth> fields with the computed values. Atomic via
    temp-file + rename. Returns the number of d-cells updated."""
    if not RESULTS_FILE.exists():
        return 0

    # by_key[fen_key][depth] = {"nodes": int, "divide": {move: count}}
    by_key: dict[str, dict[int, dict]] = {}
    with RESULTS_FILE.open() as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            n = r.get("nodes")
            if n is None:
                continue
            entry = {"nodes": int(n)}
            div = r.get("divide")
            if isinstance(div, dict) and div:
                # JSON keys are strings already; values may be ints or strs.
                entry["divide"] = {str(k): int(v) for k, v in div.items()}
            by_key.setdefault(r["fen_key"], {})[int(r["d"])] = entry

    src = STATIC_ANALYSIS
    tmp = src.with_suffix(".jsonl.tmp")
    updated_cells = 0

    with src.open() as fin, tmp.open("w") as fout:
        for line in fin:
            if not line.strip():
                fout.write(line)
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                fout.write(line)
                continue
            key = row.get("fen_key")
            if key and key in by_key:
                for d, entry in by_key[key].items():
                    if not (1 <= d <= max_depth):
                        continue
                    nodes = entry["nodes"]
                    if row.get(f"d{d}", 0) != nodes:
                        row[f"d{d}"] = nodes
                        updated_cells += 1
                    div = entry.get("divide")
                    if div and row.get(f"divide_d{d}") != div:
                        row[f"divide_d{d}"] = div
                        updated_cells += 1
            fout.write(json.dumps(row, ensure_ascii=False) + "\n")

    os.replace(tmp, src)
    return updated_cells


def worker_loop(worker: TgctWorker,
                task_q: queue.Queue,
                result_q: queue.Queue,
                stop: threading.Event,
                cache_mb: int,
                mt_threads: int) -> None:
    while not stop.is_set():
        try:
            task = task_q.get(timeout=0.5)
        except queue.Empty:
            continue
        if task is None:
            task_q.task_done()
            return
        fen_key, fen, depth = task
        nodes, divide, elapsed, err = worker.perft(fen, depth, cache_mb, mt_threads)
        result_q.put({
            "fen_key":     fen_key,
            "d":           depth,
            "nodes":       nodes,
            "divide":      divide,
            "elapsed_sec": round(elapsed, 4),
            "engine":      "tgct",
            "worker":      worker.worker_id,
            "error":       err,
        })
        task_q.task_done()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--max-depth", type=int, default=4, choices=range(1, 8),
                    help="compute perft from d1 up to this depth (default 4, "
                         "max 7)")
    ap.add_argument("--threads", type=int, default=os.cpu_count() or 4,
                    help="number of parallel TGCT instances "
                         "(default = CPU count)")
    ap.add_argument("--tgct", default=str(DEFAULT_TGCT),
                    help="path to TGCT engine binary")
    ap.add_argument("--timeout", type=float, default=120.0,
                    help="per-(fen, depth) timeout in seconds")
    ap.add_argument("--cache-mb", type=int, default=0,
                    help="TGCT transposition-table size in MB per worker "
                         "(0 = no cache; e.g. 4096 for a 4 GB TT). Total "
                         "RAM cost = cache-mb × --threads.")
    ap.add_argument("--mt-threads", type=int, default=0,
                    help="if >0, sends divide_mt:<d>:<cache>:<mt-threads>:<fen> "
                         "so each TGCT instance fans out across that many "
                         "threads. Use with `--threads 1` to let one MT TGCT "
                         "own the whole machine — the right shape for the d7 "
                         "long-tail. (default 0 = single-threaded divide.)")
    ap.add_argument("--quality", default="all",
                    choices=("high", "medium", "both", "all"),
                    help="restrict to this context-quality tier "
                         "(default: all)")
    ap.add_argument("--limit", type=int, default=0,
                    help="cap on FENs considered this run (0 = no cap)")
    ap.add_argument("--no-merge", action="store_true",
                    help="skip the final merge into static_analysis.jsonl")
    ap.add_argument("--no-mate-skip", action="store_true",
                    help="don't skip d_n when d_{n-1}=0 in static_analysis. "
                         "Use this on a fresh regen where d_n has been "
                         "reset to zero across the board — otherwise the "
                         "mate-detection filter would drop every depth >1.")
    args = ap.parse_args()

    if not STATIC_ANALYSIS.exists():
        print(f"No static_analysis at {STATIC_ANALYSIS} — run 07_describe.py "
              f"first.", file=sys.stderr)
        return 1

    tgct = Path(args.tgct)
    if not tgct.exists():
        print(f"TGCT binary not found at {tgct}", file=sys.stderr)
        return 1

    quality_filter = ({"high", "medium"} if args.quality == "both"
                      else {"high", "medium", "low"} if args.quality == "all"
                      else {args.quality})

    existing = load_existing_results()
    n_existing_cells = sum(len(v) for v in existing.values())
    print(f"Loaded {n_existing_cells} previously-computed cells from "
          f"{RESULTS_FILE.name}")

    tasks = build_task_list(args.max_depth, quality_filter,
                            args.limit, existing,
                            no_mate_skip=args.no_mate_skip)
    print(f"Tasks to compute: {len(tasks)} "
          f"(quality={args.quality}, max_depth={args.max_depth})")
    if not tasks:
        if not args.no_merge:
            updated = merge_into_static_analysis(args.max_depth)
            print(f"Merged {updated} cells into static_analysis.jsonl")
        return 0

    task_q:   queue.Queue = queue.Queue()
    result_q: queue.Queue = queue.Queue()
    stop_evt = threading.Event()

    for t in tasks:
        task_q.put(t)
    for _ in range(args.threads):
        task_q.put(None)  # poison pills

    print(f"Starting {args.threads} TGCT worker(s)…", flush=True)
    workers: list[TgctWorker] = []
    threads: list[threading.Thread] = []
    for i in range(args.threads):
        try:
            w = TgctWorker(tgct, i, args.timeout)
        except Exception as e:
            print(f"  worker {i}: failed to start ({e})", file=sys.stderr)
            continue
        t = threading.Thread(
            target=worker_loop,
            args=(w, task_q, result_q, stop_evt, args.cache_mb, args.mt_threads),
            daemon=True, name=f"tgct-{i}")
        t.start()
        workers.append(w)
        threads.append(t)
    if not workers:
        print("No workers running — aborting.", file=sys.stderr)
        return 1

    def shutdown(signum, frame):
        print("\nShutdown requested — finishing in-flight tasks then merging.",
              flush=True, file=sys.stderr)
        stop_evt.set()

    signal.signal(signal.SIGINT,  shutdown)
    signal.signal(signal.SIGTERM, shutdown)

    n_total   = len(tasks)
    n_done    = 0
    n_failed  = 0
    started   = time.time()
    last_log  = started
    log_every = 5.0

    with RESULTS_FILE.open("a") as out:
        while n_done + n_failed < n_total:
            if stop_evt.is_set() and task_q.empty():
                # All workers will exit once their in-flight task finishes;
                # we still drain the result queue until they're done.
                pass
            try:
                rec = result_q.get(timeout=0.5)
            except queue.Empty:
                if all(not t.is_alive() for t in threads):
                    break
                continue
            out.write(json.dumps(rec, ensure_ascii=False) + "\n")
            out.flush()
            if rec.get("nodes") is None:
                n_failed += 1
            else:
                n_done += 1
            now = time.time()
            if now - last_log >= log_every:
                rate = (n_done + n_failed) / max(0.001, now - started)
                eta  = (n_total - n_done - n_failed) / max(0.001, rate)
                restarts = sum(w.restarts - 1 for w in workers)
                print(f"  [{n_done + n_failed:>6}/{n_total}]  "
                      f"done={n_done}  failed={n_failed}  "
                      f"rate={rate:>5.1f}/s  eta={eta:>5.0f}s  "
                      f"restarts={restarts}",
                      flush=True)
                last_log = now

    # Drain workers (they exit when they pop their poison pill or the
    # task queue is empty).
    for t in threads:
        t.join(timeout=10)
    for w in workers:
        w.close()

    print(f"\nFinished: {n_done} computed, {n_failed} failed, "
          f"{sum(w.restarts - 1 for w in workers)} engine restarts, "
          f"{time.time() - started:.1f}s elapsed.")

    if not args.no_merge:
        print("Merging into static_analysis.jsonl…")
        updated = merge_into_static_analysis(args.max_depth)
        print(f"Merged {updated} cells.")
    else:
        print("--no-merge passed; static_analysis.jsonl untouched. "
              f"Run again without that flag to merge {RESULTS_FILE.name}.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
