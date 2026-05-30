#!/usr/bin/env python3
"""Standalone perft correctness checker for PerftWar engines.

Reads the same `engines/*.json` descriptors as `perft_war.py` and walks each
engine through a corpus of known-correct perft cases (~105 k by default).
Reports go to `results/perftcheck/<engine>.json` plus a roll-up
`results/perftcheck/_summary.json`. Never touches the leaderboard.

Two subcommands:

    perft_verify.py verify <engine.json> [--depth-cap N] [--epd-dir DIR]
    perft_verify.py verify-all [--depth-cap N] [--workers N] [--engines a,b,c]

Reuses perft_war's subprocess driver primitives (spawn / setup / case /
end_re / quit) so any engine that PerftWar can run, this can verify —
including the wrapper-respawn engines (mperft, gigantua, chessbit, etc.).

Stdlib-only by design — meant to run on a fresh baremetal host.
"""

from __future__ import annotations

import argparse
import json
import queue
import re
import subprocess
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

# Reuse the low-level engine-driver primitives from perft_war. This is the
# only coupling — perft_verify has its own report shape, EPD corpus loader,
# scheduler, and CLI. The shared helpers are subprocess-control plumbing
# that doesn't belong duplicated.
from perft_war import (
    _send_lines,
    _shutdown_proc,
    _start_reader,
    _wait_for_end,
    host_threads,
    load_descriptor,
    stdout_tail,
    substitute,
    utc_now_iso,
    verify_nodes,
)

# Engines that respawn their underlying binary per perft call (via bash
# wrappers around perft-only CLI tools). Their per-case overhead dominates
# wall-clock, so verify-all schedules them first to overlap with the
# faster persistent-UCI engines instead of leaving them as a tail.
_SLOW_FIRST_SCHEDULE = (
    "perft_cpu_2026",
    "mperft",
    "gigantua",
    "chessbit",
    "quanticade",
    "surge",
    "juddperft",
)


# ─────────────────────────── EPD corpus ────────────────────────────────────

def parse_epd_file(path: Path) -> list[tuple[str, dict[int, int]]]:
    """Read an EPD file in `<FEN> ; D1 <count> ; D2 <count> ; …` format.
    Returns a list of (fen, {depth: expected_nodes})."""
    cases: list[tuple[str, dict[int, int]]] = []
    with path.open() as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split(";")
            fen = parts[0].strip()
            depths: dict[int, int] = {}
            for p in parts[1:]:
                m = re.match(r"\s*D(\d+)\s+(\d+)", p)
                if m:
                    depths[int(m.group(1))] = int(m.group(2))
            if fen and depths:
                cases.append((fen, depths))
    return cases


def load_epd_corpus(
    epd_dir: Path, depth_cap: int
) -> tuple[list[tuple[str, int, int]], list[str]]:
    """Load every .epd under `epd_dir`, expand into flat (fen, depth, expected)
    triples filtered to depth <= depth_cap. Returns also the source filenames
    so the report records what was tested against."""
    cases: list[tuple[str, int, int]] = []
    files: list[str] = []
    for path in sorted(epd_dir.glob("*.epd")):
        files.append(path.name)
        for fen, depths in parse_epd_file(path):
            for d, count in depths.items():
                if d <= depth_cap:
                    cases.append((fen, d, count))
    return cases, files


# ─────────────────────────── engine driver ─────────────────────────────────

def pick_verify_mode(descriptor: dict) -> str:
    """Prefer single-no-cache; otherwise the first declared mode. Big TT
    allocation is the worst hit for wrapper-respawn engines and verify
    doesn't need a cache, so cache-off is mandatory when available."""
    modes = descriptor.get("modes", {})
    for preferred in ("single-no-cache", "single-with-cache",
                      "multi-no-cache", "multi-with-cache"):
        if preferred in modes:
            return preferred
    return next(iter(modes))


def verify_engine(
    descriptor: dict,
    cases: list[tuple[str, int, int]],
    epd_files: list[str],
    depth_cap: int,
    per_case_timeout: float = 5.0,
    fail_fast: bool = False,
    progress_every: int = 5000,
) -> dict:
    """Drive one engine through every EPD case, return a report dict.

    The engine subprocess is kept alive across all cases — engines that
    speak true UCI (or our wrappers) amortise startup once. Wrapper-respawn
    engines still pay the per-case spawn cost inside the wrapper; that's
    unavoidable without changing the wrapper design."""
    mode = pick_verify_mode(descriptor)
    mode_def = descriptor["modes"][mode]
    threads = host_threads()

    # Some engines crash on shallow depths (e.g. Horsie segfaults on
    # `perft 1`). Descriptors can opt out via `min_depth`; we filter the
    # case list to honour it instead of letting the engine die per-case.
    min_depth = int(descriptor.get("min_depth", 1))
    if min_depth > 1:
        skipped = sum(1 for _, d, _ in cases if d < min_depth)
        cases = [c for c in cases if c[1] >= min_depth]
        print(f"  [{descriptor['name']}] min_depth={min_depth}: "
              f"skipping {skipped:,} shallower cases", flush=True)

    launch = substitute(mode_def["launch"], "", 0, threads)
    setup_lines = [substitute(s, "", 0, threads)
                   for s in mode_def.get("setup", [])]
    case_template = mode_def["case"]
    end_re = re.compile(mode_def["end_re"])
    quit_cmd = mode_def.get("quit", "quit")

    started = time.monotonic()
    failures: list[dict] = []
    counts = {"cases": len(cases), "passed": 0,
              "failed": 0, "timeout": 0, "error": 0, "skipped_min_depth": 0}
    if min_depth > 1:
        counts["skipped_min_depth"] = skipped

    def open_engine() -> tuple[subprocess.Popen, queue.Queue]:
        p = subprocess.Popen(
            f"exec {launch}",
            shell=True,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
        )
        qq: "queue.Queue[str | None]" = queue.Queue()
        _start_reader(p, qq)
        for line in setup_lines:
            _send_lines(p, line)
        return p, qq

    def restart() -> tuple[subprocess.Popen, queue.Queue]:
        try:
            _shutdown_proc(proc, quit_cmd)
        except Exception:
            pass
        return open_engine()

    proc, q = open_engine()

    try:
        for i, (fen, depth, expected) in enumerate(cases, 1):
            cmd = substitute(case_template, fen, depth, threads)
            try:
                _send_lines(proc, cmd)
            except (BrokenPipeError, OSError):
                counts["error"] += 1
                failures.append({
                    "fen": fen, "depth": depth, "expected": expected,
                    "got": None, "mode": mode, "kind": "engine_died",
                    "captured_stdout_tail": "",
                })
                if fail_fast:
                    break
                proc, q = restart()
                continue

            output, timed_out, eof = _wait_for_end(q, end_re, per_case_timeout)

            if timed_out:
                counts["timeout"] += 1
                failures.append({
                    "fen": fen, "depth": depth, "expected": expected,
                    "got": None, "mode": mode, "kind": "timeout",
                    "captured_stdout_tail": stdout_tail(output, 8),
                })
                if fail_fast:
                    break
                # Engine's protocol state is now ambiguous — restart.
                proc, q = restart()
                continue

            if eof:
                counts["error"] += 1
                failures.append({
                    "fen": fen, "depth": depth, "expected": expected,
                    "got": None, "mode": mode, "kind": "engine_died",
                    "captured_stdout_tail": stdout_tail(output, 8),
                })
                if fail_fast:
                    break
                proc, q = open_engine()
                continue

            if verify_nodes(output, expected):
                counts["passed"] += 1
            else:
                counts["failed"] += 1
                # Best-effort extraction of whatever integer the engine
                # printed, for the failure record.
                cleaned = output.replace(",", "").replace("_", "")
                ints = re.findall(r"\b\d{2,}\b", cleaned)
                got = ints[-1] if ints else None
                failures.append({
                    "fen": fen, "depth": depth, "expected": expected,
                    "got": got, "mode": mode, "kind": "wrong_count",
                    "captured_stdout_tail": stdout_tail(output, 8),
                })
                if fail_fast:
                    break

            if progress_every and i % progress_every == 0:
                print(f"  [{descriptor['name']}] {i:,}/{len(cases):,}  "
                      f"pass={counts['passed']:,} fail={counts['failed']} "
                      f"timeout={counts['timeout']} err={counts['error']}",
                      flush=True)
    finally:
        try:
            _shutdown_proc(proc, quit_cmd)
        except Exception:
            pass

    duration = time.monotonic() - started
    return {
        "engine": descriptor["name"],
        "version": descriptor.get("version"),
        "language": descriptor.get("language"),
        "mode_used": mode,
        "ran_at": utc_now_iso(),
        "duration_sec": round(duration, 3),
        "options": {
            "depth_cap": depth_cap,
            "epd_files": epd_files,
            "per_case_timeout_sec": per_case_timeout,
            "fail_fast": fail_fast,
        },
        "totals": counts,
        "failures": failures,
    }


# ─────────────────────────── scheduler ─────────────────────────────────────

def _verify_shard_worker(args: tuple) -> tuple[str, int, dict | None, str | None]:
    """Process-pool entry point for ONE shard of ONE engine. Returns
    (engine_name, shard_idx, shard_report, err_str). The shard report has
    the same shape as a full report; the parent merges all shards of the
    same engine before writing the final file."""
    descriptor_path, shard_idx, shard_cases, epd_files, depth_cap, per_case_timeout, fail_fast = args
    try:
        descriptor = load_descriptor(Path(descriptor_path))
        # Per-shard progress prefix so logs from parallel shards stay readable.
        # Progress is muted (5000 → 0) when sharding because the parent prints
        # per-shard completion lines and a roll-up at the end anyway.
        report = verify_engine(
            descriptor, shard_cases, epd_files,
            depth_cap=depth_cap,
            per_case_timeout=per_case_timeout,
            fail_fast=fail_fast,
            progress_every=0,
        )
        return descriptor["name"], shard_idx, report, None
    except Exception as e:
        return Path(descriptor_path).stem, shard_idx, None, f"{type(e).__name__}: {e}"


def _shard_cases(
    cases: list[tuple[str, int, int]], n_shards: int
) -> list[list[tuple[str, int, int]]]:
    """Round-robin split (better than contiguous because adjacent EPD
    entries are often correlated — sequences of similar positions)."""
    n_shards = max(1, n_shards)
    shards: list[list[tuple[str, int, int]]] = [[] for _ in range(n_shards)]
    for i, c in enumerate(cases):
        shards[i % n_shards].append(c)
    return shards


def _merge_shard_reports(reports: list[dict], engine_name: str) -> dict:
    """Combine N shard reports for one engine into a single report. Counts
    are summed; failures are concatenated; duration is the wall-clock max
    (shards ran in parallel, so the engine's effective duration is its
    slowest shard, not the sum)."""
    merged_totals = {"cases": 0, "passed": 0, "failed": 0, "timeout": 0, "error": 0, "skipped_min_depth": 0}
    merged_failures: list[dict] = []
    max_duration = 0.0
    first = reports[0]
    for r in reports:
        for k in merged_totals:
            merged_totals[k] += r["totals"].get(k, 0)
        merged_failures.extend(r.get("failures", []))
        max_duration = max(max_duration, r.get("duration_sec", 0.0))
    return {
        "engine": engine_name,
        "version": first.get("version"),
        "language": first.get("language"),
        "mode_used": first.get("mode_used"),
        "ran_at": utc_now_iso(),
        "duration_sec": round(max_duration, 3),
        "shards": len(reports),
        "options": first.get("options", {}),
        "totals": merged_totals,
        "failures": merged_failures,
    }


# ─────────────────────────── subcommands ───────────────────────────────────

def cmd_verify(args: argparse.Namespace) -> int:
    descriptor = load_descriptor(Path(args.descriptor))
    cases, epd_files = load_epd_corpus(Path(args.epd_dir), args.depth_cap)
    print(f"[{descriptor['name']}] verifying {len(cases):,} cases "
          f"(depth_cap={args.depth_cap}, "
          f"from {len(epd_files)} EPD file{'s' if len(epd_files)!=1 else ''})",
          flush=True)
    report = verify_engine(
        descriptor, cases, epd_files,
        depth_cap=args.depth_cap,
        per_case_timeout=args.per_case_timeout,
        fail_fast=args.fail_fast,
    )
    out_path = Path(args.results_dir) / "perftcheck" / f"{descriptor['name']}.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2) + "\n")
    t = report["totals"]
    print(f"[{descriptor['name']}] done in {report['duration_sec']:.1f}s: "
          f"pass={t['passed']:,} fail={t['failed']} "
          f"timeout={t['timeout']} err={t['error']}",
          flush=True)
    print(f"wrote {out_path}")
    return 0 if (t['failed'] == 0 and t['error'] == 0 and t['timeout'] == 0) else 1


def cmd_verify_all(args: argparse.Namespace) -> int:
    import os

    engine_paths: list[Path] = []
    if args.engines:
        for n in args.engines.split(","):
            f = Path(f"engines/{n.strip()}.json")
            if not f.exists():
                print(f"unknown engine: {n} ({f})", file=sys.stderr)
                return 2
            engine_paths.append(f)
    else:
        for f in sorted(Path("engines").glob("*.json")):
            if f.name.startswith("example-"):
                continue
            engine_paths.append(f)

    # Pool size = the host's logical cores. We submit ALL shards across ALL
    # engines as separate futures; ProcessPoolExecutor handles the work-
    # stealing automatically — fast engines' completed shards free up slots
    # that get filled by pending shards from slower engines.
    workers = args.workers if args.workers > 0 else (os.cpu_count() or 4)
    shards_per_engine = max(1, args.shards_per_engine)

    # Load the EPD corpus once in the parent and pass shards to workers,
    # rather than having each worker re-read it.
    cases, epd_files = load_epd_corpus(Path(args.epd_dir), args.depth_cap)

    # Build (engine, shard_cases) submission list.
    work: list[tuple] = []  # (descriptor_path, shard_idx, shard_cases, ...)
    for ep in engine_paths:
        descriptor = load_descriptor(ep)
        min_depth = int(descriptor.get("min_depth", 1))
        engine_cases = [c for c in cases if c[1] >= min_depth]
        for i, shard in enumerate(_shard_cases(engine_cases, shards_per_engine)):
            work.append((
                str(ep), i, shard, epd_files,
                args.depth_cap, args.per_case_timeout, args.fail_fast,
            ))

    print(f"verifying {len(engine_paths)} engines × {shards_per_engine} shards "
          f"= {len(work)} chunks across {workers} workers "
          f"(depth_cap={args.depth_cap}, {len(cases):,} cases per engine)",
          flush=True)

    started = time.monotonic()
    # Per-engine shard reports buffered in the parent until we have them all.
    pending: dict[str, list[dict]] = {p.stem: [] for p in engine_paths}
    pending_count: dict[str, int] = {p.stem: shards_per_engine for p in engine_paths}
    engine_started_at: dict[str, float] = {p.stem: started for p in engine_paths}
    engine_first_finish: dict[str, float] = {}
    errors: dict[str, str] = {}

    with ProcessPoolExecutor(max_workers=workers) as pool:
        futures = {pool.submit(_verify_shard_worker, wa): (wa[0], wa[1]) for wa in work}
        for fut in as_completed(futures):
            engine_path, shard_idx = futures[fut]
            try:
                name, idx, report, err = fut.result()
            except Exception as e:
                name, err = Path(engine_path).stem, f"{type(e).__name__}: {e}"
                report = None
            if err is not None:
                # Whole shard failed; record the error and decrement remaining.
                errors[name] = err
                pending_count[name] -= 1
                print(f"[{name} shard {shard_idx}] FAILED: {err}", file=sys.stderr)
            else:
                pending[name].append(report)
                pending_count[name] -= 1

            if pending_count[name] == 0:
                # All shards of this engine are in; merge and write.
                if pending[name]:
                    merged = _merge_shard_reports(pending[name], name)
                    out_path = Path(args.results_dir) / "perftcheck" / f"{name}.json"
                    out_path.parent.mkdir(parents=True, exist_ok=True)
                    out_path.write_text(json.dumps(merged, indent=2) + "\n")
                    t = merged["totals"]
                    wall = time.monotonic() - started
                    print(f"[{name}] done t+{wall:.0f}s: "
                          f"pass={t['passed']:,} fail={t['failed']} "
                          f"timeout={t['timeout']} err={t['error']} "
                          f"({merged['shards']} shards, "
                          f"slowest_shard={merged['duration_sec']:.0f}s)",
                          flush=True)
                else:
                    print(f"[{name}] all shards failed; no report written",
                          file=sys.stderr)

    # Build summary roll-up.
    summary_rows: list[dict] = []
    for ep in engine_paths:
        name = ep.stem
        report_path = Path(args.results_dir) / "perftcheck" / f"{name}.json"
        if report_path.exists():
            r = json.loads(report_path.read_text())
            summary_rows.append({
                "engine": r["engine"],
                "version": r.get("version"),
                "language": r.get("language"),
                "mode_used": r.get("mode_used"),
                "duration_sec": r.get("duration_sec"),
                "shards": r.get("shards", 1),
                "totals": r["totals"],
            })
        else:
            summary_rows.append({
                "engine": name,
                "error": errors.get(name, "no report produced"),
            })

    summary_rows.sort(key=lambda r: r.get("engine", ""))
    summary = {
        "generated_at": utc_now_iso(),
        "options": {
            "depth_cap": args.depth_cap,
            "per_case_timeout_sec": args.per_case_timeout,
            "workers": workers,
            "shards_per_engine": shards_per_engine,
            "epd_dir": args.epd_dir,
        },
        "total_duration_sec": round(time.monotonic() - started, 3),
        "rows": summary_rows,
    }
    out = Path(args.results_dir) / "perftcheck" / "_summary.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(summary, indent=2) + "\n")
    print(f"wrote {out} ({len(summary_rows)} engines, "
          f"total_wall_clock={summary['total_duration_sec']:.0f}s)")

    bad = sum(
        1 for r in summary_rows
        if "error" in r
        or (r.get("totals") and (r["totals"]["failed"]
                                  or r["totals"]["error"]
                                  or r["totals"]["timeout"]))
    )
    return 0 if bad == 0 else 1


# ─────────────────────────── CLI ───────────────────────────────────────────

def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="perft_verify.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    v = sub.add_parser("verify",
                       help="run perft-correctness check on one engine")
    v.add_argument("descriptor",
                   help="path to engine descriptor JSON")
    v.add_argument("--depth-cap", type=int, default=4,
                   help="skip EPD cases deeper than this [default 4]")
    v.add_argument("--epd-dir", default="../Perft-Checker/data",
                   help="directory of .epd files [default ../Perft-Checker/data]")
    v.add_argument("--per-case-timeout", type=float, default=5.0,
                   help="per-case timeout in seconds [default 5]")
    v.add_argument("--fail-fast", action="store_true",
                   help="stop on the first failure")
    v.add_argument("--results-dir", default="results")
    v.set_defaults(func=cmd_verify)

    va = sub.add_parser("verify-all",
                        help="run perft-correctness check on every engine in parallel")
    va.add_argument("--depth-cap", type=int, default=4)
    va.add_argument("--epd-dir", default="../Perft-Checker/data")
    va.add_argument("--per-case-timeout", type=float, default=5.0)
    va.add_argument("--fail-fast", action="store_true")
    va.add_argument("--workers", type=int, default=0,
                    help="size of the global process pool; "
                         "default = cpu_count(). All shards from all engines "
                         "share this pool — work-stealing happens naturally "
                         "as fast engines' shards finish and free slots.")
    va.add_argument("--shards-per-engine", type=int, default=4,
                    help="split each engine's EPD case list into this many "
                         "shards [default 4]. Each shard runs as its own "
                         "engine subprocess, in parallel via the global pool. "
                         "Higher = better load balance but more startup cost.")
    va.add_argument("--engines", default=None,
                    help="comma-separated subset of engine names; "
                         "default = every engines/*.json")
    va.add_argument("--results-dir", default="results")
    va.set_defaults(func=cmd_verify_all)

    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
