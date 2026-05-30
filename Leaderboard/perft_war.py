#!/usr/bin/env python3
"""Perft-War benchmark harness for the TGCT leaderboard.

See README.md for the design. Stdlib-only by design — this is meant to run
on a fresh baremetal Ubuntu host without extra dependencies.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import queue
import re
import statistics
import subprocess
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

MODES = ("single-no-cache", "single-with-cache", "multi-no-cache", "multi-with-cache")

# Optimism factor for the iterative-deepening early-skip predictor.
# After each depth completes we estimate the next depth's runtime from
# (expected_nodes / (observed_nps * NEXT_DEPTH_OPTIMISM)). Keeping this
# at 1.0 means the predictor uses the raw observed NPS — no assumption
# that the engine will speed up at deeper depths. The slack instead
# lives in BUDGET_OVERRUN_FACTOR below: we let a single call overrun
# the strict budget up to that multiplier before either letting it
# finish or killing it.
NEXT_DEPTH_OPTIMISM = 1.0

# Hard cap on per-(mode, position) wall time, as a multiple of the user's
# `--budget-sec`. A depth that the conservative predictor thinks will fit
# in this extended window is allowed to run; only when the prediction
# blows even this extension does the runner skip and move on. The actual
# call also gets killed if it exceeds this window mid-flight.
BUDGET_OVERRUN_FACTOR = 1.5

# Predictor confidence threshold: skip-prediction is only reliable once the
# last completed depth took long enough that fixed overhead (subprocess
# startup, hash-table allocation, JIT warmup) isn't the dominant cost.
# Below this, the observed NPS is meaningless for extrapolation — just try
# the next depth and accept the worst case (a few seconds of wasted budget).
# This is what unblocks engines whose wrapper re-spawns the underlying
# binary per call: e.g. mperft with --hash 4096 takes ~1.6s to allocate a
# 4 GB TT regardless of depth, so d1 reads as 12 nps but d7 is multi-G/s.
PREDICTOR_MIN_ELAPSED_SEC = 1.0

# (name, fen, {depth: expected_nodes})
# Authoritative node counts from results/perft_p{0,1,2}_results.json.
# The runner walks depths in order (d1, d2, …) under a per-position time budget
# and stops when the budget is exhausted; the deepest completed depth is what
# the leaderboard reports for that position.
POSITIONS = [
    (
        "startpos",
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        {
            1: 20,
            2: 400,
            3: 8_902,
            4: 197_281,
            5: 4_865_609,
            6: 119_060_324,
            7: 3_195_901_860,
            8: 84_998_978_956,
            9: 2_439_530_234_167,
            10: 69_352_859_712_417,
            11: 2_097_651_003_696_806,
            12: 62_854_969_236_701_747,
        },
    ),
    (
        "kiwipete",
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        {
            1: 48,
            2: 2_039,
            3: 97_862,
            4: 4_085_603,
            5: 193_690_690,
            6: 8_031_647_685,
            7: 374_190_009_323,
            8: 15_493_944_087_984,
            9: 708_027_759_953_502,
        },
    ),
    (
        "sje",
        "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
        {
            1: 46,
            2: 2_079,
            3: 89_890,
            4: 3_894_594,
            5: 164_075_551,
            6: 6_923_051_137,
            7: 287_188_994_746,
            8: 11_923_589_843_526,
            9: 490_154_852_788_714,
        },
    ),
]


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_descriptor(path: Path) -> dict:
    desc = json.loads(path.read_text())
    for key in ("name", "version", "modes"):
        if key not in desc:
            raise ValueError(f"descriptor {path} missing required key: {key}")
    for mode_name, mode_def in desc["modes"].items():
        if mode_name not in MODES:
            raise ValueError(f"descriptor {path} declares unknown mode: {mode_name}")
        if not isinstance(mode_def, dict):
            raise ValueError(f"mode {mode_name} must be an object")
        for req in ("launch", "case", "end_re"):
            if req not in mode_def:
                raise ValueError(f"mode {mode_name} missing required key: {req}")
    return desc


def substitute(template: str, fen: str, depth: int, threads: int) -> str:
    return (template
            .replace("{fen}", fen)
            .replace("{depth}", str(depth))
            .replace("{threads}", str(threads)))


def host_threads() -> int:
    return os.cpu_count() or 1


def _run_cmd(args: list[str], timeout: float = 10.0) -> str | None:
    """Best-effort subprocess capture for host probes. Returns stdout on
    success, None on any failure (missing binary, non-zero exit, timeout).
    Used for sysctl / system_profiler / dmidecode lookups — every one of
    them may be absent or restricted, so we always tolerate failure."""
    try:
        r = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    except (FileNotFoundError, OSError, subprocess.SubprocessError):
        return None
    if r.returncode != 0:
        return None
    return r.stdout


def _sysctl_int(key: str) -> int | None:
    out = _run_cmd(["sysctl", "-n", key])
    if out is None:
        return None
    try:
        return int(out.strip())
    except ValueError:
        return None


def _linux_mem_speed_mts() -> int | None:
    """Parse Configured/Speed lines from `dmidecode -t memory`. Needs root
    on most distros; returns None silently when unavailable."""
    out = _run_cmd(["dmidecode", "-t", "memory"])
    if not out:
        return None
    # Prefer the actually-configured speed; some BIOSes only emit "Speed:".
    configured = re.findall(r"Configured (?:Memory|Clock) Speed:\s+(\d+)\s*MT/s", out)
    speeds = configured or re.findall(r"^\s*Speed:\s+(\d+)\s*MT/s", out, re.M)
    if not speeds:
        return None
    return max(int(x) for x in speeds)


def _darwin_mem_speed_mts() -> int | None:
    """macOS system_profiler reports DDR data rates as `Speed: NNNN MHz`
    (which equals MT/s for DDR). On Apple Silicon, no speed line is
    typically emitted — unified memory makes it moot — so returns None."""
    out = _run_cmd(["system_profiler", "SPMemoryDataType"], timeout=15.0)
    if not out:
        return None
    speeds = re.findall(r"Speed:\s+(\d+)\s*MHz", out)
    if not speeds:
        return None
    return max(int(x) for x in speeds)


def collect_host_info() -> dict:
    """Cross-platform best-effort description of the machine that ran the
    benchmark. Every field can independently be None — callers should treat
    the dict as informational, not a contract.

    Stored verbatim under `versions[v].host` in each per-engine result, and
    surfaced as `hosts[<engine>].host` in the aggregated leaderboard."""
    sysname = platform.system()
    info: dict = {
        "platform": platform.platform(),
        "system": sysname,
        "machine": platform.machine(),
        "python_version": platform.python_version(),
        "cpu_model": None,
        "cpu_physical_cores": None,
        "cpu_logical_cores": os.cpu_count(),
        "ram_total_bytes": None,
        "mem_speed_mts": None,
    }

    if sysname == "Linux":
        try:
            with open("/proc/cpuinfo") as f:
                cpuinfo = f.read()
            m = re.search(r"^model name\s*:\s*(.+)$", cpuinfo, re.M)
            if m:
                info["cpu_model"] = m.group(1).strip()
            sockets: set[str] = set()
            cores_per_socket: int | None = None
            for line in cpuinfo.splitlines():
                if line.startswith("physical id"):
                    sockets.add(line.split(":", 1)[1].strip())
                elif line.startswith("cpu cores") and cores_per_socket is None:
                    try:
                        cores_per_socket = int(line.split(":", 1)[1].strip())
                    except ValueError:
                        pass
            if cores_per_socket and sockets:
                info["cpu_physical_cores"] = cores_per_socket * len(sockets)
            elif cores_per_socket:
                info["cpu_physical_cores"] = cores_per_socket
        except OSError:
            pass
        try:
            with open("/proc/meminfo") as f:
                m = re.search(r"^MemTotal:\s+(\d+)\s+kB", f.read(), re.M)
                if m:
                    info["ram_total_bytes"] = int(m.group(1)) * 1024
        except OSError:
            pass
        info["mem_speed_mts"] = _linux_mem_speed_mts()
    elif sysname == "Darwin":
        brand = _run_cmd(["sysctl", "-n", "machdep.cpu.brand_string"])
        if brand:
            info["cpu_model"] = brand.strip() or None
        info["cpu_physical_cores"] = _sysctl_int("hw.physicalcpu")
        logical = _sysctl_int("hw.logicalcpu")
        if logical is not None:
            info["cpu_logical_cores"] = logical
        info["ram_total_bytes"] = _sysctl_int("hw.memsize")
        info["mem_speed_mts"] = _darwin_mem_speed_mts()

    return info


def verify_nodes(stdout: str, expected: int) -> bool:
    # Strip thousands separators (commas, underscores) before matching, so
    # engines like StockDory that print "Nodes searched: 197,281" still
    # validate against the bare-int expected value.
    cleaned = stdout.replace(",", "").replace("_", "")
    return re.search(rf"\b{expected}\b", cleaned) is not None


def stdout_tail(stdout: str, lines: int = 20) -> str:
    return "\n".join(stdout.splitlines()[-lines:])


class Disqualified(Exception):
    def __init__(self, mode: str, case: dict, stdout: str):
        self.mode = mode
        self.case = case
        self.stdout = stdout


def _start_reader(proc: subprocess.Popen, q: "queue.Queue[str | None]") -> threading.Thread:
    def reader() -> None:
        try:
            assert proc.stdout is not None
            for line in proc.stdout:
                q.put(line)
        finally:
            q.put(None)
    t = threading.Thread(target=reader, daemon=True)
    t.start()
    return t


def _send_lines(proc: subprocess.Popen, text: str) -> None:
    assert proc.stdin is not None
    for line in text.split("\n"):
        if not line:
            continue
        proc.stdin.write(line + "\n")
    proc.stdin.flush()


def _wait_for_end(
    q: "queue.Queue[str | None]",
    end_re: re.Pattern,
    timeout: float,
) -> tuple[str, bool, bool]:
    """Read lines until end_re matches or timeout. Returns (output, timed_out, eof)."""
    deadline = time.monotonic() + timeout
    lines: list[str] = []
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return "\n".join(lines), True, False
        try:
            line = q.get(timeout=remaining)
        except queue.Empty:
            return "\n".join(lines), True, False
        if line is None:
            return "\n".join(lines), False, True
        stripped = line.rstrip("\n")
        lines.append(stripped)
        if end_re.search(stripped):
            return "\n".join(lines), False, False


_IS_LINUX = platform.system() == "Linux"
try:
    _CLK_TCK = os.sysconf("SC_CLK_TCK")
except (ValueError, AttributeError, OSError):
    _CLK_TCK = 100
try:
    _PAGE_KB = os.sysconf("SC_PAGE_SIZE") // 1024
except (ValueError, AttributeError, OSError):
    _PAGE_KB = 4


def _parse_cputime(s: str) -> float | None:
    """Parse the time column from BSD `ps -o time=` (macOS) into seconds.

    Format: '[H:]MM:SS.dd' (e.g. '0:01.23', '1:23:45.67'). Linux GNU `ps`
    has only whole-second precision so we read /proc/<pid>/stat there
    instead; this parser only needs to handle the macOS form.
    """
    try:
        s = s.strip()
        if "-" in s:
            days_str, rest = s.split("-", 1)
            days_sec = int(days_str) * 86_400
        else:
            days_sec = 0
            rest = s
        sec = 0.0
        for p in rest.split(":"):
            sec = sec * 60 + float(p)
        return days_sec + sec
    except (ValueError, AttributeError):
        return None


def _proc_snapshot_linux(pid: int) -> dict | None:
    """RSS + cputime via /proc on Linux. Cputime resolves to one clock tick
    (typically 10ms with HZ=100, 4ms with HZ=250) — fine enough for our
    sub-second perft calls, where GNU `ps -o time=` would round to 0."""
    try:
        with open(f"/proc/{pid}/stat", "r") as f:
            stat_line = f.read()
        with open(f"/proc/{pid}/statm", "r") as f:
            statm_parts = f.read().split()
    except OSError:
        return None
    # The comm field is in parens and may itself contain spaces or parens.
    # Find the last ')' and parse fields after it.
    rparen = stat_line.rfind(")")
    if rparen < 0:
        return None
    after = stat_line[rparen + 2:].split()
    # Indexes after comm in /proc/<pid>/stat:
    #   0=state, 1=ppid, 2=pgrp, 3=session, 4=tty_nr, 5=tpgid, 6=flags,
    #   7=minflt, 8=cminflt, 9=majflt, 10=cmajflt, 11=utime, 12=stime
    try:
        utime = int(after[11])
        stime = int(after[12])
        rss_pages = int(statm_parts[1])
    except (IndexError, ValueError):
        return None
    return {
        "rss_kb": rss_pages * _PAGE_KB,
        "cputime_sec": (utime + stime) / _CLK_TCK,
    }


def _ps_snapshot(pid: int) -> dict | None:
    """One-shot {rss_kb, cputime_sec} read. Linux uses /proc directly for
    sub-second cputime precision; other systems fall back to `ps`."""
    if _IS_LINUX:
        return _proc_snapshot_linux(pid)
    try:
        result = subprocess.run(
            ["ps", "-p", str(pid), "-o", "rss=,time="],
            capture_output=True, text=True, timeout=2,
        )
    except (FileNotFoundError, subprocess.TimeoutExpired, OSError):
        return None
    if result.returncode != 0:
        return None
    line = result.stdout.strip()
    if not line:
        return None
    parts = line.split(None, 1)
    if len(parts) < 2:
        return None
    try:
        rss_kb = int(parts[0])
    except ValueError:
        return None
    cputime_sec = _parse_cputime(parts[1])
    if cputime_sec is None:
        return None
    return {"rss_kb": rss_kb, "cputime_sec": cputime_sec}


class ProcStatsSampler:
    """Background-thread RSS sampler plus synchronous start/end snapshots.

    av_cpu_pct is derived from cputime_delta / wall_elapsed * 100, so it can
    exceed 100% on multi-threaded workloads (e.g. 14 cores pinned = ~1400%).
    """

    def __init__(self, pid: int, interval_sec: float = 0.1):
        self.pid = pid
        self.interval = interval_sec
        self._lock = threading.Lock()
        self._samples: list[int] = []
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self.available = _ps_snapshot(pid) is not None

    def start(self) -> None:
        if self.available:
            self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread.is_alive():
            self._thread.join(timeout=2)

    def _run(self) -> None:
        while not self._stop.is_set():
            snap = _ps_snapshot(self.pid)
            if snap:
                with self._lock:
                    self._samples.append(snap["rss_kb"])
            if self._stop.wait(self.interval):
                break

    def begin_call(self) -> tuple[dict | None, int]:
        if not self.available:
            return None, 0
        snap = _ps_snapshot(self.pid)
        with self._lock:
            start_idx = len(self._samples)
        return snap, start_idx

    def end_call(self, marker: tuple[dict | None, int], elapsed_sec: float) -> dict:
        if not self.available:
            return {}
        start_snap, start_idx = marker
        end_snap = _ps_snapshot(self.pid)
        with self._lock:
            samples = list(self._samples[start_idx:])

        result: dict = {}
        if start_snap and end_snap and elapsed_sec > 0:
            cpu_delta = end_snap["cputime_sec"] - start_snap["cputime_sec"]
            result["av_cpu_pct"] = round(cpu_delta / elapsed_sec * 100, 1)

        rss_pool = list(samples)
        if start_snap:
            rss_pool.append(start_snap["rss_kb"])
        if end_snap:
            rss_pool.append(end_snap["rss_kb"])
        if rss_pool:
            result["av_rss_mb"] = round(sum(rss_pool) / len(rss_pool) / 1024, 1)
            result["peak_rss_mb"] = round(max(rss_pool) / 1024, 1)

        return result


def _shutdown_proc(proc: subprocess.Popen, quit_cmd: str | None) -> None:
    try:
        if quit_cmd and proc.stdin and not proc.stdin.closed:
            try:
                proc.stdin.write(quit_cmd + "\n")
                proc.stdin.flush()
            except (BrokenPipeError, OSError):
                pass
        if proc.stdin:
            try:
                proc.stdin.close()
            except (BrokenPipeError, OSError):
                pass
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
    except Exception:
        pass


def run_position_iterative(
    mode: str,
    mode_def: dict,
    name: str,
    fen: str,
    depths_map: dict[int, int],
    budget_sec: float,
    threads: int,
    min_depth: int = 1,
) -> dict:
    """Iterative deepening on one position under a hard wall-clock budget.

    Launches a fresh engine subprocess, sends d{min_depth}, d{min_depth+1}, …,
    recording the time each completed depth took. When `budget_sec` elapses
    or the current call times out, the subprocess is killed and the deepest
    completed depth is used as the position's result.

    `min_depth` lets descriptors skip shallow depths that crash a particular
    engine (e.g. Horsie segfaults on `perft 1`)."""
    launch = substitute(mode_def["launch"], "", 0, threads)
    setup_lines = [substitute(s, "", 0, threads) for s in mode_def.get("setup", [])]
    case_template = mode_def["case"]
    end_re = re.compile(mode_def["end_re"])
    quit_cmd = mode_def.get("quit", "quit")

    # `exec` so the shell replaces itself with the engine; proc.pid then refers
    # to the engine itself, which the stats sampler needs for accurate readings.
    proc = subprocess.Popen(
        f"exec {launch}",
        shell=True,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )
    q: "queue.Queue[str | None]" = queue.Queue()
    _start_reader(proc, q)
    for line in setup_lines:
        _send_lines(proc, line)

    sampler = ProcStatsSampler(proc.pid)
    sampler.start()

    completed: list[dict] = []
    note: str | None = None
    wrong_count_case: dict | None = None
    budget_start = time.monotonic()

    try:
        for depth in sorted(depths_map):
            if depth < min_depth:
                continue
            elapsed_total = time.monotonic() - budget_start
            # Hard wall-clock cap = budget_sec * BUDGET_OVERRUN_FACTOR.
            # The "budget" the user set is the *target*; we let calls run up
            # to BUDGET_OVERRUN_FACTOR× past that before killing them so a
            # depth that's barely over budget can still complete.
            hard_cap = budget_sec * BUDGET_OVERRUN_FACTOR
            remaining = hard_cap - elapsed_total
            if remaining <= 0:
                note = "budget exhausted"
                break

            expected = depths_map[depth]

            # Predictive early skip: if even an extension to BUDGET_OVERRUN_FACTOR×
            # the budget can't fit the next depth (using last completed depth's
            # NPS at 1.0× — no optimism), stop here rather than burn time on a
            # call we know will time out.
            if completed:
                last = completed[-1]
                last_elapsed = last["elapsed_sec"]
                last_nodes = last["nodes"]
                # Only trust the predictor once the last measurement is big
                # enough that fixed overhead doesn't dominate.
                if (last_elapsed >= PREDICTOR_MIN_ELAPSED_SEC
                        and last_nodes > 0):
                    observed_nps = last_nodes / last_elapsed
                    projected_nps = observed_nps * NEXT_DEPTH_OPTIMISM
                    estimated_sec = expected / projected_nps
                    if estimated_sec > remaining:
                        print(
                            f"    d{depth:<2} SKIPPED "
                            f"(est. {estimated_sec:>7.1f}s at "
                            f"{NEXT_DEPTH_OPTIMISM:.1f}x last NPS > "
                            f"{remaining:.1f}s remaining "
                            f"[cap {hard_cap:.0f}s])",
                            flush=True,
                        )
                        note = (
                            f"skipped d{depth} "
                            f"(estimated {estimated_sec:.1f}s > "
                            f"{remaining:.1f}s remaining at "
                            f"{BUDGET_OVERRUN_FACTOR:.1f}x budget cap)"
                        )
                        break

            cmd = substitute(case_template, fen, depth, threads)
            stats_marker = sampler.begin_call()
            call_start = time.monotonic()
            _send_lines(proc, cmd)
            # Per-call timeout = how much of the hard-capped window remains.
            # If the call exceeds this, the engine subprocess gets killed.
            output, timed_out, eof = _wait_for_end(q, end_re, remaining)
            call_elapsed = time.monotonic() - call_start
            stats = sampler.end_call(stats_marker, call_elapsed)

            if timed_out:
                print(f"    d{depth:<2} TIMEOUT after {call_elapsed:.1f}s (budget exhausted)", flush=True)
                note = f"timeout at d{depth}"
                break
            if eof:
                print(f"    d{depth:<2} ENGINE DIED after {call_elapsed:.1f}s", flush=True)
                note = f"engine died at d{depth}"
                break

            if not verify_nodes(output, expected):
                print(f"    d{depth:<2} WRONG NODE COUNT", flush=True)
                wrong_count_case = {
                    "name": name, "fen": fen, "depth": depth,
                    "expected_nodes": expected,
                }
                note = f"wrong node count at d{depth}"
                # Bubble up via exception after cleanup.
                raise Disqualified(mode, wrong_count_case, output)

            nps = int(expected / call_elapsed) if call_elapsed > 0 else 0
            entry = {
                "depth": depth,
                "nodes": expected,
                "elapsed_sec": round(call_elapsed, 6),
                "nps": nps,
                **stats,
            }
            completed.append(entry)
            cpu_str = f"{stats['av_cpu_pct']:>6.0f}%" if "av_cpu_pct" in stats else "    -- "
            rss_str = f"{stats['peak_rss_mb']:>7.0f}MB" if "peak_rss_mb" in stats else "      --"
            print(f"    d{depth:<2} {call_elapsed:>8.3f}s  {expected:>18,} nodes  {nps:>14,} nps  cpu={cpu_str}  rss={rss_str}", flush=True)
    finally:
        sampler.stop()
        _shutdown_proc(proc, quit_cmd)

    result: dict = {"name": name, "fen": fen, "depths": completed}
    if note:
        result["note"] = note
    if completed:
        deepest = completed[-1]
        result["best_depth"] = deepest["depth"]
        result["best_nodes"] = deepest["nodes"]
        result["best_elapsed_sec"] = deepest["elapsed_sec"]
        result["best_nps"] = deepest["nps"]
        for k in ("av_cpu_pct", "av_rss_mb", "peak_rss_mb"):
            if k in deepest:
                result[f"best_{k}"] = deepest[k]
    return result


def run_mode(
    mode: str,
    mode_def: dict,
    positions: list[tuple[str, str, dict[int, int]]],
    budget_sec: float,
    threads: int,
    min_depth: int = 1,
) -> dict:
    position_results: list[dict] = []
    best_nps_values: list[float] = []
    for name, fen, depths_map in positions:
        print(f"  -- {name} (budget {budget_sec:.0f}s) --", flush=True)
        result = run_position_iterative(mode, mode_def, name, fen, depths_map, budget_sec, threads, min_depth=min_depth)
        position_results.append(result)
        if "best_nps" in result and result["best_nps"] is not None:
            best_nps_values.append(result["best_nps"])

    mean_nps = int(statistics.mean(best_nps_values)) if best_nps_values else None
    return {"positions": position_results, "mean_nps": mean_nps}


def write_engine_result(results_dir: Path, descriptor: dict, version_block: dict) -> Path:
    results_dir.mkdir(parents=True, exist_ok=True)
    path = results_dir / f"{descriptor['name']}.json"

    if path.exists():
        existing = json.loads(path.read_text())
    else:
        existing = {
            "name": descriptor["name"],
            "owner": descriptor.get("owner"),
            "repo": descriptor.get("repo"),
            "language": descriptor.get("language"),
            "versions": {},
        }

    existing["owner"] = descriptor.get("owner", existing.get("owner"))
    existing["repo"] = descriptor.get("repo", existing.get("repo"))
    if descriptor.get("language") is not None:
        existing["language"] = descriptor["language"]
    existing.setdefault("versions", {})
    existing["versions"][descriptor["version"]] = version_block

    path.write_text(json.dumps(existing, indent=2) + "\n")
    return path


def cmd_run(args: argparse.Namespace) -> int:
    descriptor = load_descriptor(Path(args.descriptor))

    requested_modes = args.modes.split(",") if args.modes else list(descriptor["modes"].keys())
    for mode in requested_modes:
        if mode not in MODES:
            print(f"unknown mode: {mode}", file=sys.stderr)
            return 2
        if mode not in descriptor["modes"]:
            print(f"engine {descriptor['name']} does not declare mode {mode}; skipping",
                  file=sys.stderr)

    threads = host_threads()
    budget = float(args.budget_sec)
    host = collect_host_info()

    if args.positions:
        wanted = set(args.positions.split(","))
        positions = [p for p in POSITIONS if p[0] in wanted]
        unknown = wanted - {p[0] for p in POSITIONS}
        if unknown:
            print(f"unknown positions: {sorted(unknown)}", file=sys.stderr)
            return 2
    else:
        positions = list(POSITIONS)

    # Per-engine min_depth: lets descriptors skip shallow depths that crash
    # the engine (e.g. Horsie segfaults on `perft 1`).
    min_depth = int(descriptor.get("min_depth", 1))

    mode_results: dict[str, dict] = {}
    try:
        for mode in requested_modes:
            if mode not in descriptor["modes"]:
                continue
            print(f"[{descriptor['name']} {descriptor['version']}] {mode} (threads={threads})", flush=True)
            mode_results[mode] = run_mode(
                mode=mode,
                mode_def=descriptor["modes"][mode],
                positions=positions,
                budget_sec=budget,
                threads=threads,
                min_depth=min_depth,
            )
    except Disqualified as dq:
        version_block = {
            "ran_at": utc_now_iso(),
            "host": host,
            "disqualified": True,
            "reason": "wrong node count",
            "failed_case": {
                "mode": dq.mode,
                "name": dq.case["name"],
                "fen": dq.case["fen"],
                "depth": dq.case["depth"],
                "expected_nodes": dq.case["expected_nodes"],
                "captured_stdout": stdout_tail(dq.stdout, 20),
            },
        }
        path = write_engine_result(Path(args.results_dir), descriptor, version_block)
        print(f"DISQUALIFIED: {dq.mode} {dq.case['name']} d{dq.case['depth']}",
              file=sys.stderr)
        print(f"wrote {path}", file=sys.stderr)
        return 1

    version_block = {
        "ran_at": utc_now_iso(),
        "host": host,
        "disqualified": False,
        "budget_sec": budget,
        "modes": mode_results,
    }
    path = write_engine_result(Path(args.results_dir), descriptor, version_block)
    print(f"wrote {path}")
    return 0


def cmd_aggregate(args: argparse.Namespace) -> int:
    results_dir = Path(args.results_dir)
    rows: list[dict] = []
    hosts: dict[str, dict] = {}
    for engine_file in sorted(results_dir.glob("*.json")):
        if engine_file.name == "leaderboard.json":
            continue
        data = json.loads(engine_file.read_text())
        versions = data.get("versions", {})
        valid = [
            (v, block) for v, block in versions.items()
            if not block.get("disqualified", False) and "modes" in block
        ]
        if not valid:
            continue
        valid.sort(key=lambda vb: vb[1].get("ran_at", ""), reverse=True)
        version, block = valid[0]
        if "host" in block:
            hosts[data["name"]] = {"version": version, "host": block["host"]}
        for mode, mode_data in block["modes"].items():
            row_positions = []
            for pos in mode_data.get("positions", []):
                row_positions.append({
                    "name": pos["name"],
                    "best_depth": pos.get("best_depth"),
                    "best_nodes": pos.get("best_nodes"),
                    "best_elapsed_sec": pos.get("best_elapsed_sec"),
                    "best_nps": pos.get("best_nps"),
                    "best_av_cpu_pct": pos.get("best_av_cpu_pct"),
                    "best_av_rss_mb": pos.get("best_av_rss_mb"),
                    "best_peak_rss_mb": pos.get("best_peak_rss_mb"),
                    "depths": [
                        {
                            "depth": d["depth"],
                            "elapsed_sec": d["elapsed_sec"],
                            "nps": d.get("nps"),
                            "av_cpu_pct": d.get("av_cpu_pct"),
                            "av_rss_mb": d.get("av_rss_mb"),
                            "peak_rss_mb": d.get("peak_rss_mb"),
                        }
                        for d in pos.get("depths", [])
                    ],
                })
            rows.append({
                "engine": data["name"],
                "version": version,
                "language": data.get("language"),
                "repo": data.get("repo"),
                "mode": mode,
                "mean_nps": mode_data.get("mean_nps"),
                "positions": row_positions,
            })

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps({
        "generated_at": utc_now_iso(),
        "hosts": hosts,
        "rows": rows,
    }, indent=2) + "\n")
    print(f"wrote {out_path} ({len(rows)} rows, {len(hosts)} hosts)")
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="perft_war.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("run", help="benchmark one engine")
    r.add_argument("descriptor", help="path to engine descriptor JSON")
    r.add_argument("--budget-sec", type=float, default=60.0,
                   help="wall-clock budget per (mode, position) pair in seconds")
    r.add_argument("--modes", default=None,
                   help="comma-separated subset of modes; default = all declared")
    r.add_argument("--positions", default=None,
                   help="comma-separated subset of positions; default = all in POSITIONS")
    r.add_argument("--results-dir", default="results")
    r.set_defaults(func=cmd_run)

    a = sub.add_parser("aggregate", help="build the combined leaderboard")
    a.add_argument("--results-dir", default="results")
    a.add_argument("--out", default="results/leaderboard.json")
    a.set_defaults(func=cmd_aggregate)

    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
