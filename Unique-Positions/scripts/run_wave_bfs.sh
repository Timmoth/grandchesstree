#!/usr/bin/env bash
# Run the BFS unique-position pipeline from ply 0 to a target depth.
# Usage:  run_wave_bfs.sh [depth] [fen]    (defaults: depth=9, fen=start)
#
# Env overrides:
#   WORK=/path           work dir (default /tmp/wave_bfs)
#   K=64                 bucket count, power of 2. Use 256+ for ply 11+ so
#                        per-bucket merge fits in RAM
#   THREADS=32           worker threads in wave_expand
#   LOG2_MEMTABLE=30     memtable size in log2(slots) — 0 disables.
#                        Each slot is 16 B (128-bit hash). E.g. 30 = 16 GB,
#                        31 = 32 GB, 32 = 64 GB. Suppresses local duplicates
#                        before they hit disk; for ply 11+ this can cut spill
#                        volume 40-50%. Default 30 (16 GB) is safe on any
#                        32 GB+ machine; set lower (or 0) on small RAM.
#   COUNT_PLY=0          if >0, run only that depth in count-mode (16-byte
#                        spill records, no wave output). Used for ply 12 count
#                        on top of an existing wave_(N-1). Implies that wave
#                        already exists in WORK/wave_$((COUNT_PLY-1))/.
#   WAVE_DIR=""          optional second tier for wave_N storage. When set,
#                        each wave_$N inside $WORK is a symlink to
#                        $WAVE_DIR/wave_$N — engine + merger see the same
#                        $WORK paths, but the actual bytes live on $WAVE_DIR.
#                        Use when spill (fast tier) and wave (bulk tier)
#                        should live on different filesystems. Both dirs are
#                        wiped at script start.
#   PASSES=1             number of expand passes per ply. >1 chunks the output
#                        bucket range so peak spill_N fits in disk. Each pass
#                        re-reads the full prior wave but only writes ~1/PASSES
#                        of the spill. Required for plies where total spill
#                        won't fit in scratch (ply 11+). Single merger pass
#                        runs at the end over the union of all passes' files.
#   COMPRESS=0           if 1, the merger writes wave_N bucket files compressed
#                        with `xz --delta=dist=34 --lzma2=preset=1` after a DFS
#                        sort-key re-sort. Engine reads transparently via magic
#                        bytes. Spill stays uncompressed (unsorted, won't compress
#                        well). Trades CPU for ~10-16× smaller wave_N on disk —
#                        decisive for ply 11+ where uncompressed wave doesn't fit.
#   RESUME=0             if 1, do NOT wipe $WORK / $WAVE_DIR at start and skip
#                        wave_init if wave_0 already exists. Per-ply resume
#                        relies on engine .done markers + merger .done markers;
#                        completed plies are detected by presence of all bucket
#                        files in $WORK/wave_$ply. Use this to survive crashes
#                        or accidental script reruns during multi-day runs.
#   K_PLY_<N>            per-ply K override. E.g. K_PLY_10=256 K_PLY_11=4096.
#                        Engine handles changing K between plies natively
#                        (reads any number of input files, routes to its own
#                        K output files). Useful for ply 11+ where bigger K
#                        is required for parallel merge.
#   PASSES_PLY_<N>       per-ply PASSES override. Same pattern as K_PLY_<N>.
#                        Most plies need PASSES=1; ply 11+ need multi-pass.
#   LOG2_MEMTABLE_PLY_<N>  per-ply LOG2_MEMTABLE override. Same pattern.
#                          Set =0 from ply 10 onward in production runs —
#                          memtable hit rate drops to <1 % once unique count
#                          exceeds capacity, and the saturated TryAdd's DRAM
#                          probes start costing more than the few suppressed
#                          duplicates save. d8 and d9 should keep the
#                          memtable on (it's net beneficial there).
#   SPILL_OVERFLOW_DIR=""    when set, a fraction of each pass's spill bucket
#                            files are pre-created as symlinks pointing here
#                            instead of $WORK/spill_$ply. Use to spread spill
#                            across a fast (NVMe) primary and a bulk (HDD)
#                            overflow tier — required for ply 12 where total
#                            spill exceeds either tier alone.
#   SPILL_OVERFLOW_PCT=50    percentage of each pass's bucket range routed to
#                            $SPILL_OVERFLOW_DIR (only when that var is set).
#                            E.g. 50 → second half of pass's bucket range goes
#                            to overflow tier, first half stays on $WORK.
#
# Live system stats: run scripts/watch_wave_bfs.sh in another terminal.

set -euo pipefail

DEPTH=${1:-9}
FEN=${2:-start}
WORK=${WORK:-/tmp/wave_bfs}
K=${K:-64}
THREADS=${THREADS:-32}
LOG2_MEMTABLE=${LOG2_MEMTABLE:-30}
COUNT_PLY=${COUNT_PLY:-0}
WAVE_DIR=${WAVE_DIR:-}
PASSES=${PASSES:-1}
if [[ "$PASSES" -lt 1 ]]; then echo "PASSES must be >= 1"; exit 1; fi

# Per-ply overrides for K and PASSES. Look up K_PLY_<N> / PASSES_PLY_<N>
# env vars; fall back to the global K / PASSES if not set.
ply_K()      { local v="K_PLY_$1";      echo "${!v:-$K}"; }
ply_PASSES() { local v="PASSES_PLY_$1"; echo "${!v:-$PASSES}"; }
# LOG2_MEMTABLE_PLY_<N> lets us disable the in-RAM dedup memtable for plies
# where it's saturated and pure overhead. Set =0 from d10+ in production —
# at that scale the memtable hit rate is <1 % and TryAdd's DRAM probes cost
# more than they save. d8 and d9 should keep the memtable on (LOG2_MEMTABLE=30).
ply_L2M()    { local v="LOG2_MEMTABLE_PLY_$1"; echo "${!v:-$LOG2_MEMTABLE}"; }
COMPRESS=${COMPRESS:-0}
# Pass compression intent down to the Rust merger via env var.
if [[ "$COMPRESS" == "1" ]]; then
    export WAVE_MERGE_COMPRESS=1
else
    unset WAVE_MERGE_COMPRESS
fi
RESUME=${RESUME:-0}
SPILL_OVERFLOW_DIR=${SPILL_OVERFLOW_DIR:-}
SPILL_OVERFLOW_PCT=${SPILL_OVERFLOW_PCT:-50}

# Symlink helpers for SPILL_OVERFLOW_DIR. When enabled, each pass pre-creates
# symlinks for the second SPILL_OVERFLOW_PCT% of its bucket range pointing at
# files under $SPILL_OVERFLOW_DIR. Engine writes to the symlinks; bytes land
# on the overflow tier. Merger reads via the symlinks (slower if overflow is
# on HDD, but lets total spill exceed any single tier's capacity).
#
# Two warts to be aware of:
#   - The merger's fs::remove_file deletes the SYMLINK, leaving the overflow
#     target orphaned. We sweep $SPILL_OVERFLOW_DIR after each pass's merge.
#   - The engine truncates existing files at FileMode.Create — fine for our
#     symlinks (truncates the target via the link).
setup_spill_overflow() {
    local ply=$1
    local bucket_lo=$2
    local bucket_hi=$3
    [[ -z "$SPILL_OVERFLOW_DIR" ]] && return
    local range=$((bucket_hi - bucket_lo))
    local overflow_count=$(( range * SPILL_OVERFLOW_PCT / 100 ))
    [[ "$overflow_count" -le 0 ]] && return
    local overflow_start=$(( bucket_hi - overflow_count ))
    mkdir -p "$WORK/spill_$ply" "$SPILL_OVERFLOW_DIR"
    local i
    for (( i = overflow_start; i < bucket_hi; i++ )); do
        local name; printf -v name 'bucket_%04d.bin' "$i"
        rm -f "$WORK/spill_$ply/$name"
        : > "$SPILL_OVERFLOW_DIR/$name"
        ln -s "$SPILL_OVERFLOW_DIR/$name" "$WORK/spill_$ply/$name"
    done
}
cleanup_spill_overflow() {
    [[ -z "$SPILL_OVERFLOW_DIR" ]] && return
    # Merger unlinks the symlinks but the overflow targets remain. Sweep.
    rm -f "$SPILL_OVERFLOW_DIR"/bucket_*.bin 2>/dev/null || true
}

REPO=$(cd "$(dirname "$0")/../.." && pwd)
ENGINE="dotnet $REPO/Engine/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll"
WAVE_MERGER=$REPO/Unique-Positions/target/release/wave_merge
WAVE_GLOBAL_MERGER=$REPO/Unique-Positions/target/release/wave_global_merge
WAVE_GLOBAL_TO_BUCKETS=$REPO/Unique-Positions/target/release/global_to_buckets
COUNT_MERGER=$REPO/Unique-Positions/target/release/merge

# DFS-sort-compression experiment hook. When DFS_GLOBAL_MERGE=1, run
# `wave_global_merge` after each ply's wave_$ply is complete to produce
# wave_${ply}_global.xz alongside the bucketed wave_$ply files. The bucketed
# files are kept (next ply's engine still reads them); the global file is
# an additional artefact whose compression ratio is what the experiment is
# measuring.
DFS_GLOBAL_MERGE=${DFS_GLOBAL_MERGE:-0}

# Promote-global-to-primary mode. When DFS_GLOBAL_PRIMARY=1 (implies
# DFS_GLOBAL_MERGE=1):
#   - After each ply's wave_global_merge succeeds and `xz -t` passes, the
#     bucketed wave_$ply is deleted. wave_${ply}_global.xz becomes the
#     only persistent representation of that ply.
#   - At the top of each ply's iteration, if the previous ply's bucketed
#     wave_(prev)/ is missing but wave_(prev)_global.xz exists, re-derive
#     bucketed via `global_to_buckets` so the engine's existing K-parallel
#     reader can consume it.
# Net: long-term disk footprint per ply drops to the global xz size only.
DFS_GLOBAL_PRIMARY=${DFS_GLOBAL_PRIMARY:-0}
# DFS_GLOBAL_PRIMARY requires DFS_GLOBAL_MERGE — except in COUNT_PLY mode,
# where primary-mode is only used for the re-derive input path (count-only
# expansion doesn't produce a wave that needs globalizing).
if [[ "$DFS_GLOBAL_PRIMARY" == "1" && "$DFS_GLOBAL_MERGE" != "1" && "$COUNT_PLY" -eq 0 ]]; then
    echo "DFS_GLOBAL_PRIMARY=1 requires DFS_GLOBAL_MERGE=1 (except in COUNT_PLY mode)"; exit 1
fi

# Sanity checks
[[ -x "$WAVE_MERGER" ]] || { echo "missing $WAVE_MERGER — build with: (cd $REPO/Unique-Positions && cargo build --release --bin wave_merge)"; exit 1; }
[[ -f "$REPO/Engine/GrandChessTree.Engine/bin/Release/net10.0/GrandChessTree.Engine.dll" ]] || { echo "missing engine dll — build with: dotnet build -c Release $REPO/Engine/GrandChessTree.Engine/GrandChessTree.Engine.csproj"; exit 1; }
if [[ "$DFS_GLOBAL_MERGE" == "1" ]]; then
    [[ -x "$WAVE_GLOBAL_MERGER" ]] || { echo "DFS_GLOBAL_MERGE=1 but missing $WAVE_GLOBAL_MERGER — build with: (cd $REPO/Unique-Positions && cargo build --release --bin wave_global_merge)"; exit 1; }
fi
if [[ "$DFS_GLOBAL_PRIMARY" == "1" ]]; then
    [[ -x "$WAVE_GLOBAL_TO_BUCKETS" ]] || { echo "DFS_GLOBAL_PRIMARY=1 but missing $WAVE_GLOBAL_TO_BUCKETS — build with: (cd $REPO/Unique-Positions && cargo build --release --bin global_to_buckets)"; exit 1; }
fi

# Wismuth/Labelle reference for the start position.
declare -A WISMUTH=( [1]=20 [2]=400 [3]=5362 [4]=72078 [5]=822518 [6]=9417681 [7]=96400068 [8]=988187354 [9]=9183421888 [10]=85375278064 [11]=726155461002 )

# ---- COUNT_PLY mode: multi-pass count-only expansion ----
# For ply 12 counting: feed wave_(N-1) -> spill_N (16-byte) -> count via `merge`.
# When PASSES > 1, the work is chunked across disjoint output bucket ranges so
# spill_N peak fits on disk. Each pass: expand its slice -> count -> wipe its
# spill -> next pass. Counts accumulate to a grand total.
if [[ "$COUNT_PLY" -gt 0 ]]; then
    if [[ ! -x "$COUNT_MERGER" ]]; then
        echo "missing $COUNT_MERGER — build with: (cd $REPO/Unique-Positions && cargo build --release --bin merge)"
        exit 1
    fi
    prev=$((COUNT_PLY - 1))
    # Composes with DFS_GLOBAL_PRIMARY: if bucketed wave_$prev was deleted
    # after promote-to-global, but wave_${prev}_global.xz still exists,
    # re-derive bucketed before count-expand. (Engine still wants K parallel
    # input files; the global xz isn't directly engine-readable.)
    if [[ "$DFS_GLOBAL_PRIMARY" == "1" && -e "$WORK/wave_${prev}_global.xz" && ! -d "$WORK/wave_$prev" ]]; then
        prev_K=$(ply_K "$prev")
        echo "Re-derive: wave_${prev}_global.xz -> wave_$prev/ (K=$prev_K) before count-expand"
        mkdir -p "$WORK/wave_$prev"
        "$WAVE_GLOBAL_TO_BUCKETS" "$WORK/wave_${prev}_global.xz" "$WORK/wave_$prev" "$prev_K" 2>&1 | grep -E "^(input xz|total records|output buckets|elapsed)" | sed 's/^/  /' || true
    fi
    [[ -d "$WORK/wave_$prev" ]] || { echo "need WORK/wave_$prev to exist (or wave_${prev}_global.xz with DFS_GLOBAL_PRIMARY=1); got nothing at $WORK/wave_$prev"; exit 1; }

    count_K=$(ply_K "$COUNT_PLY")
    count_PASSES=$(ply_PASSES "$COUNT_PLY")
    count_L2M=$(ply_L2M "$COUNT_PLY")
    if (( count_PASSES > count_K )); then
        echo "PASSES ($count_PASSES) must be <= K ($count_K) for ply $COUNT_PLY"; exit 1
    fi
    echo "Count-mode: ply $COUNT_PLY from $WORK/wave_$prev"
    echo "  K=$count_K PASSES=$count_PASSES threads=$THREADS memtable=2^$count_L2M"

    spill_dir="$WORK/spill_$COUNT_PLY"
    grand_seen=0
    grand_unique=0
    count_t0=$(date +%s)
    [[ -n "$SPILL_OVERFLOW_DIR" ]] && echo "  spill overflow: ${SPILL_OVERFLOW_PCT}% per pass routed to $SPILL_OVERFLOW_DIR"
    for p in $(seq 0 $((count_PASSES - 1))); do
        bucket_lo=$(( count_K * p / count_PASSES ))
        bucket_hi=$(( count_K * (p + 1) / count_PASSES ))
        echo ""
        echo "  ===== pass $((p+1))/$count_PASSES: output buckets [$bucket_lo, $bucket_hi) ====="
        # Fresh spill dir for each pass — we account for and delete it after counting.
        rm -rf "$spill_dir"
        setup_spill_overflow "$COUNT_PLY" "$bucket_lo" "$bucket_hi"
        echo "wave_expand:$WORK/wave_$prev:$spill_dir:$count_K:$THREADS:$count_L2M:count:$bucket_lo:$bucket_hi
quit" | $ENGINE 2>&1 | grep -E "^(input positions|child records|mode|memtable|elapsed|buckets \[)" | sed 's/^/    /'
        # Run the count merger on this pass's bucket files only. Capture output
        # to extract the per-pass totals before printing.
        pass_out=$("$COUNT_MERGER" "$spill_dir" 2>&1)
        echo "$pass_out" | tail -6 | sed 's/^/    /'
        cleanup_spill_overflow
        pass_seen=$(echo "$pass_out" | awk '/^total records seen/ {print $NF}')
        pass_unique=$(echo "$pass_out" | awk '/^total unique positions/ {print $NF}')
        grand_seen=$(( grand_seen + ${pass_seen:-0} ))
        grand_unique=$(( grand_unique + ${pass_unique:-0} ))
        echo "    pass $((p+1)) → +$pass_unique unique  (running total: $grand_unique)"
    done

    # Final cleanup of the per-pass spill scratch.
    rm -rf "$spill_dir"

    # DFS_GLOBAL_PRIMARY: if wave_$prev was re-derived from its global xz at
    # the top of this run, drop the bucketed scratch now. (The global xz
    # is the canonical representation; we can re-derive on demand.) At d12
    # production this reclaims ~24.6 TB of wave_11 bucketed scratch.
    if [[ "$DFS_GLOBAL_PRIMARY" == "1" && -e "$WORK/wave_${prev}_global.xz" && -d "$WORK/wave_$prev" ]]; then
        echo "Cleaning re-derived bucketed wave_$prev (global xz remains canonical)"
        rm -rf "$WORK/wave_$prev"
    fi
    count_t1=$(date +%s)
    echo ""
    echo "===== ply $COUNT_PLY count complete ====="
    expected_count=${WISMUTH[$COUNT_PLY]:-unknown}
    verdict_count="(no reference for this fen)"
    if [[ "$FEN" == "start" && "$expected_count" != "unknown" ]]; then
        if [[ "$grand_unique" == "$expected_count" ]]; then
            verdict_count="\033[32m✓ matches Wismuth ($expected_count)\033[0m"
        else
            verdict_count="\033[31m✗ MISMATCH (expected $expected_count)\033[0m"
        fi
    fi
    echo "total records seen:    $grand_seen"
    echo "total unique positions: $grand_unique"
    echo -e "verdict:               $verdict_count"
    echo "elapsed:                $((count_t1 - count_t0))s"
    exit 0
fi

# ---- Full BFS pipeline ----
echo "Running BFS pipeline: depth=$DEPTH fen=$FEN K=$K threads=$THREADS memtable=2^$LOG2_MEMTABLE"
echo "Work dir: $WORK"
[[ -n "$WAVE_DIR" ]] && echo "Wave tier: $WAVE_DIR (symlinked from \$WORK/wave_\$N)"
echo "Live stats: run scripts/watch_wave_bfs.sh $WORK in another terminal"
echo ""

if [[ "$RESUME" == "1" && -d "$WORK/wave_0" ]]; then
    echo "Resume: $WORK already contains wave_0 — keeping existing state."
else
    rm -rf "$WORK"
    mkdir -p "$WORK"
fi

# Tiered storage: pre-create wave_N as symlinks into $WAVE_DIR so wave bytes
# land on a separate filesystem (e.g. bulk HDD) while spill stays on $WORK
# (fast NVMe). Engine + merger paths unchanged; the symlinks redirect IO.
if [[ -n "$WAVE_DIR" ]]; then
    if [[ "$RESUME" == "1" && -d "$WAVE_DIR" ]]; then
        echo "Resume: $WAVE_DIR exists — keeping bulk tier state and (re)building symlinks."
    else
        rm -rf "$WAVE_DIR"
        mkdir -p "$WAVE_DIR"
    fi
    for i in $(seq 0 "$DEPTH"); do
        mkdir -p "$WAVE_DIR/wave_$i"
        # Replace any stale symlink so we always point at the current $WAVE_DIR.
        rm -f "$WORK/wave_$i"
        ln -s "$WAVE_DIR/wave_$i" "$WORK/wave_$i"
    done
fi

# Resume guard for wave_init: skip if wave_0 contains any non-empty bucket
# file. (The previous check `-s bucket_0000.bin` fails when the seed hashes
# to a bucket other than 0 — bucket_0000 is empty in that case.)
if [[ "$RESUME" == "1" && -d "$WORK/wave_0" && -n "$(find "$WORK/wave_0" -maxdepth 1 -name 'bucket_*.bin' -size +0c -print -quit 2>/dev/null)" ]]; then
    echo "Resume: wave_0 already populated — skipping wave_init."
else
    echo "===== wave_init ====="
    echo "wave_init:$K:$WORK/wave_0:$FEN
quit" | $ENGINE | tail -1
fi

total_start=$(date +%s)
for ply in $(seq 1 "$DEPTH"); do
    prev=$((ply - 1))
    echo ""
    echo "===== ply $ply ====="
    t0=$(date +%s)

    # Per-ply K and PASSES (env var overrides or fall back to the global ones).
    p_K=$(ply_K "$ply")
    p_PASSES=$(ply_PASSES "$ply")
    p_L2M=$(ply_L2M "$ply")
    if (( p_PASSES > p_K )); then
        echo "PASSES ($p_PASSES) must be <= K ($p_K) for ply $ply"; exit 1
    fi
    [[ "$p_K" != "$K" || "$p_PASSES" != "$PASSES" || "$p_L2M" != "$LOG2_MEMTABLE" ]] && \
        echo "  config: K=$p_K PASSES=$p_PASSES memtable=2^$p_L2M"

    # Resume guard. Two cases of "this ply is already done enough to advance":
    #   (1) Fully complete (DFS_GLOBAL_PRIMARY mode): wave_${ply}_global.xz
    #       and wave_${ply}.unique sidecar both exist. Skip the whole iter.
    #   (2) Bucketed complete: all K .done markers present in wave_$ply/.
    #       In standard mode this means skip the iter; in PRIMARY mode the
    #       global merge step hasn't run yet, so we skip expand+merge but
    #       fall through to the global merge step below.
    # Test `[[ -d wave_$ply ]]` before `find` — find on a missing path
    # exits 1 even with 2>/dev/null and trips set -e + pipefail.
    skip_expand_merge=0
    if [[ "$RESUME" == "1" ]]; then
        if [[ "$DFS_GLOBAL_PRIMARY" == "1" && -e "$WORK/wave_${ply}_global.xz" && -s "$WORK/wave_${ply}.unique" ]]; then
            unique_resume=$(cat "$WORK/wave_${ply}.unique")
            expected_resume=${WISMUTH[$ply]:-unknown}
            verdict_resume="(no reference for this fen)"
            if [[ "$FEN" == "start" && "$expected_resume" != "unknown" ]]; then
                if [[ "$unique_resume" == "$expected_resume" ]]; then
                    verdict_resume="\033[32m✓ matches Wismuth ($expected_resume)\033[0m"
                else
                    verdict_resume="\033[31m✗ MISMATCH (got $unique_resume, expected $expected_resume)\033[0m"
                fi
            fi
            echo -e "  wave_$ply: $unique_resume unique  $verdict_resume  (resumed, fully complete)"
            continue
        fi
        if [[ -d "$WORK/wave_$ply" ]]; then
            done_count=$(find -L "$WORK/wave_$ply" -maxdepth 1 -name 'bucket_*.bin.done' -type f 2>/dev/null | wc -l)
            if [[ "$done_count" -eq "$p_K" ]]; then
                unique_resume=$(find -L "$WORK/wave_$ply" -maxdepth 1 -name 'bucket_*.bin.done' -type f -print0 2>/dev/null | xargs -0 -r awk 'FNR==2 {s+=$1} END {print s+0}')
                unique_resume=${unique_resume:-0}
                expected_resume=${WISMUTH[$ply]:-unknown}
                verdict_resume="(no reference for this fen)"
                if [[ "$FEN" == "start" && "$expected_resume" != "unknown" ]]; then
                    if [[ "$unique_resume" == "$expected_resume" ]]; then
                        verdict_resume="\033[32m✓ matches Wismuth ($expected_resume)\033[0m"
                    else
                        verdict_resume="\033[31m✗ MISMATCH (got $unique_resume, expected $expected_resume)\033[0m"
                    fi
                fi
                if [[ "$DFS_GLOBAL_PRIMARY" == "1" ]]; then
                    echo -e "  wave_$ply: $unique_resume unique  $verdict_resume  (bucketed resumed; needs global merge)"
                    skip_expand_merge=1
                else
                    echo -e "  wave_$ply: $unique_resume unique  $verdict_resume  (resumed, skipped)"
                    continue
                fi
            fi
        fi
    fi

    # DFS_GLOBAL_PRIMARY: if we're about to actually run expand+merge, and
    # the previous ply's bucketed is gone but its global xz exists, re-derive
    # bucketed now. Guarded on skip_expand_merge so resumed plies don't pay
    # for a re-derive they don't need (and that would leak a wave_$prev/ dir
    # because the post-expand cleanup is also skipped on resume `continue`).
    if [[ "$skip_expand_merge" != "1" && "$DFS_GLOBAL_PRIMARY" == "1" && -e "$WORK/wave_${prev}_global.xz" && ! -d "$WORK/wave_$prev" ]]; then
        echo "  re-derive: wave_${prev}_global.xz -> wave_$prev/ (K=$p_K)"
        mkdir -p "$WORK/wave_$prev"
        "$WAVE_GLOBAL_TO_BUCKETS" "$WORK/wave_${prev}_global.xz" "$WORK/wave_$prev" "$p_K" 2>&1 | grep -E "^(input xz|total records|output buckets|elapsed)" | sed 's/^/    /' || true
    fi

    if [[ "$skip_expand_merge" == "1" ]]; then
        :
    elif [[ "$p_PASSES" -le 1 ]]; then
        echo "  expand: wave_$prev -> spill_$ply"
        # Single-pass also gets spill overflow when configured. Without this,
        # plies whose spill exceeds /mnt/scratch fail with ENOSPC mid-expand
        # (bit us at d10 the first time around). Range is [0, K) = full range,
        # which matches the engine's default output filter.
        [[ -n "$SPILL_OVERFLOW_DIR" ]] && echo "    spill overflow: ${SPILL_OVERFLOW_PCT}% routed to $SPILL_OVERFLOW_DIR"
        setup_spill_overflow "$ply" 0 "$p_K"
        echo "wave_expand:$WORK/wave_$prev:$WORK/spill_$ply:$p_K:$THREADS:$p_L2M:full
quit" | $ENGINE 2>&1 | grep -E "^(input positions|child records|mode|memtable|elapsed)" | sed 's/^/    /'

        echo "  merge:  spill_$ply -> wave_$ply"
        WAVE_MERGE_DELETE_AFTER=1 $WAVE_MERGER "$WORK/spill_$ply" "$WORK/wave_$ply" 2>&1 | grep -E "^(total |dedup|elapsed)" | sed 's/^/    /'
        cleanup_spill_overflow
    else
        # Multi-pass: expand + merge each pass's bucket slice immediately, so
        # spill_$ply never accumulates across passes (would otherwise overflow
        # scratch). Each pass produces wave_$ply outputs for its bucket range;
        # .done markers accumulate over passes for a complete wave_$ply at end.
        echo "  expand+merge: wave_$prev -> wave_$ply  (multi-pass, PASSES=$p_PASSES)"
        [[ -n "$SPILL_OVERFLOW_DIR" ]] && echo "    spill overflow: ${SPILL_OVERFLOW_PCT}% per pass routed to $SPILL_OVERFLOW_DIR"
        for p in $(seq 0 $((p_PASSES - 1))); do
            bucket_lo=$(( p_K * p / p_PASSES ))
            bucket_hi=$(( p_K * (p + 1) / p_PASSES ))
            echo "    pass $((p+1))/$p_PASSES expand: buckets [$bucket_lo, $bucket_hi)"
            setup_spill_overflow "$ply" "$bucket_lo" "$bucket_hi"
            echo "wave_expand:$WORK/wave_$prev:$WORK/spill_$ply:$p_K:$THREADS:$p_L2M:full:$bucket_lo:$bucket_hi
quit" | $ENGINE 2>&1 | grep -E "^(input positions|child records|mode|memtable|elapsed|buckets \[)" | sed 's/^/      /'
            echo "    pass $((p+1))/$p_PASSES merge:  spill_$ply (pass-range) -> wave_$ply"
            WAVE_MERGE_DELETE_AFTER=1 $WAVE_MERGER "$WORK/spill_$ply" "$WORK/wave_$ply" 2>&1 | grep -E "^(total |dedup|elapsed)" | sed 's/^/      /'
            cleanup_spill_overflow
            # Engine resume markers are scoped to (this pass's input + bucket
            # range), but the next pass uses a different bucket range — clear
            # them so input files are reprocessed fully for the new range.
            rm -rf "$WORK/spill_$ply/_progress"
        done
    fi

    # DFS_GLOBAL_PRIMARY: expand+merge for this ply have consumed wave_$prev
    # (either the original bucketed output of the previous iteration or the
    # one we re-derived at the top of this iteration). Either way, we no
    # longer need it on disk because wave_${prev}_global.xz is the canonical
    # representation. Drop the bucketed form now to keep peak disk small.
    # Skip when no global xz exists (e.g. wave_0 isn't global-merged).
    if [[ "$DFS_GLOBAL_PRIMARY" == "1" && -e "$WORK/wave_${prev}_global.xz" && -d "$WORK/wave_$prev" ]]; then
        rm -rf "$WORK/wave_$prev"
    fi

    # DFS_GLOBAL_MERGE: collapse the K bucket files of wave_$ply into a
    # single DFS-emission-ordered, xz-compressed global file. Bucketed
    # outputs are preserved (next ply still reads them); the global file's
    # size is the compression measurement we care about.
    if [[ "$DFS_GLOBAL_MERGE" == "1" ]]; then
        gm_t0=$(date +%s)
        gm_out="$WORK/wave_${ply}_global.xz"
        rm -f "$gm_out"
        echo "  global merge: wave_$ply -> wave_${ply}_global.xz"
        "$WAVE_GLOBAL_MERGER" "$WORK/wave_$ply" "$gm_out" 2>&1 | grep -E "^(wave_global_merge|total records|input bytes|position bytes|output bytes|ratio|elapsed)" | sed 's/^/    /'
        gm_t1=$(date +%s)
        if [[ -s "$gm_out" ]]; then
            gm_sz=$(stat -c %s "$gm_out")
            echo "    file: $gm_out ($(awk -v b=$gm_sz 'BEGIN {printf "%.2f", b/1073741824}') GB on disk)  [global $((gm_t1-gm_t0))s]"
        fi

        # DFS_GLOBAL_PRIMARY: after global merge succeeds + xz integrity
        # check passes, delete the bucketed wave_$ply. The global xz
        # becomes the only persistent representation; bucketed gets
        # re-derived on demand at the start of the next ply's expand.
        # Capture the unique count from bucketed .done markers BEFORE the
        # rm, then drop a wave_${ply}.unique sidecar so the post-ply
        # Wismuth check below can find it after bucketed is gone.
        if [[ "$DFS_GLOBAL_PRIMARY" == "1" && -s "$gm_out" ]]; then
            unique_preempt=$(find -L "$WORK/wave_$ply" -maxdepth 1 -name 'bucket_*.bin.done' -type f -print0 2>/dev/null | xargs -0 -r awk 'FNR==2 {s+=$1} END {print s+0}')
            unique_preempt=${unique_preempt:-0}
            if xz -t "$gm_out" 2>/dev/null; then
                echo "$unique_preempt" > "$WORK/wave_${ply}.unique"
                echo "    ✓ xz -t passed; deleting bucketed wave_$ply (global is now primary)"
                rm -rf "$WORK/wave_$ply"
            else
                echo "    ✗ xz -t FAILED on $gm_out — keeping bucketed wave_$ply for safety"
                exit 1
            fi
        fi
    fi

    t1=$(date +%s)
    # Unique count: prefer the wave_${ply}.unique sidecar (written by
    # DFS_GLOBAL_PRIMARY before bucketed delete); else fall back to summing
    # bucket_*.bin.done markers in wave_$ply.
    if [[ -s "$WORK/wave_${ply}.unique" ]]; then
        unique=$(cat "$WORK/wave_${ply}.unique")
    else
        unique=$(find -L "$WORK/wave_$ply" -maxdepth 1 -name 'bucket_*.bin.done' -type f -print0 2>/dev/null | xargs -0 -r awk 'FNR==2 {sum+=$1} END {print sum+0}')
    fi
    unique=${unique:-0}
    expected=${WISMUTH[$ply]:-unknown}
    if [[ "$FEN" == "start" && "$expected" != "unknown" ]]; then
        if [[ "$unique" == "$expected" ]]; then
            verdict="\033[32m✓ matches Wismuth ($expected)\033[0m"
        else
            verdict="\033[31m✗ MISMATCH (got $unique, expected $expected)\033[0m"
        fi
    else
        verdict="(no reference for this fen)"
    fi
    echo -e "  wave_$ply: $unique unique  $verdict  [ply $((t1-t0))s]"
done
total_end=$(date +%s)
echo ""
echo "===== DONE in $((total_end - total_start))s ====="
# In DFS_GLOBAL_PRIMARY mode, wave_$DEPTH bucketed is gone — report whatever
# primary representation exists on disk for the final ply.
if [[ -d "$WORK/wave_$DEPTH" ]]; then
    echo "Final wave_$DEPTH on disk:"
    du -sh "$WORK/wave_$DEPTH"
elif [[ -e "$WORK/wave_${DEPTH}_global.xz" ]]; then
    echo "Final wave_${DEPTH}_global.xz on disk:"
    du -sh "$WORK/wave_${DEPTH}_global.xz"
fi
