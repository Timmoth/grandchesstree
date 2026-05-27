// External sort + dedup for BFS wave-expansion output.
//
// Input:  a directory of `bucket_NNNN.bin` files, each holding 26-byte canonical
//         chess positions (the engine's BoardStateSerialization layout). Records
//         may contain duplicates and are unsorted within each bucket.
// Output: a directory of `bucket_NNNN.bin` files, each sorted and deduped on the
//         raw 26-byte record. This is the persisted form of the next BFS wave.
//
// Memory: bounded by the largest input bucket loaded as Vec<[u8; 26]>.
// Per-bucket work is parallel via rayon.
//
// Resume: a sidecar `bucket_NNNN.done` marker is written next to each completed
// output bucket. On rerun, buckets with a non-empty output AND a `.done` marker
// are skipped. Writes go to `bucket_NNNN.bin.tmp` and are renamed on success,
// so a crash mid-write leaves no half-finished output behind.
//
// Progress: a background thread rewrites `out_dir/progress.json` every few
// seconds with bucket counts, record counts, elapsed time and ETA. Safe to
// `cat` from another shell.

use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use memmap2::MmapOptions;
use rayon::prelude::*;

const RECORD: usize = 26;

struct Progress {
    started_unix: u64,
    started: Instant,
    buckets_total: usize,
    buckets_done: AtomicUsize,
    buckets_skipped: AtomicUsize,
    buckets_in_progress: AtomicUsize,
    records_seen: AtomicU64,
    records_unique: AtomicU64,
    bytes_in_total: u64,
    bytes_in_done: AtomicU64,
    stop: AtomicBool,
    progress_path: PathBuf,
}

impl Progress {
    fn write_snapshot(&self) {
        let elapsed = self.started.elapsed().as_secs_f64();
        let done = self.buckets_done.load(Ordering::Relaxed);
        let skipped = self.buckets_skipped.load(Ordering::Relaxed);
        let in_progress = self.buckets_in_progress.load(Ordering::Relaxed);
        let seen = self.records_seen.load(Ordering::Relaxed);
        let unique = self.records_unique.load(Ordering::Relaxed);
        let bytes_done = self.bytes_in_done.load(Ordering::Relaxed);
        let total_accounted = done + skipped;
        let remaining = self.buckets_total.saturating_sub(total_accounted);
        let eta_secs = if total_accounted > 0 && remaining > 0 {
            (elapsed / total_accounted as f64) * remaining as f64
        } else {
            0.0
        };
        let dedup = if unique > 0 { seen as f64 / unique as f64 } else { 0.0 };
        let bytes_per_sec = if elapsed > 0.0 { bytes_done as f64 / elapsed } else { 0.0 };
        let json = format!(
            "{{\
\"phase\":\"wave_merge\",\
\"started_unix\":{started},\
\"now_unix\":{now},\
\"elapsed_seconds\":{elapsed:.1},\
\"buckets_total\":{total},\
\"buckets_completed\":{done},\
\"buckets_skipped\":{skipped},\
\"buckets_in_progress\":{in_progress},\
\"buckets_remaining\":{remaining},\
\"records_seen\":{seen},\
\"records_unique\":{unique},\
\"dedup_ratio\":{dedup:.3},\
\"input_bytes_total\":{bytes_total},\
\"input_bytes_processed\":{bytes_done},\
\"input_bytes_per_sec\":{bps:.0},\
\"eta_seconds\":{eta:.0}\
}}\n",
            started = self.started_unix,
            now = now_unix(),
            elapsed = elapsed,
            total = self.buckets_total,
            done = done,
            skipped = skipped,
            in_progress = in_progress,
            remaining = remaining,
            seen = seen,
            unique = unique,
            dedup = dedup,
            bytes_total = self.bytes_in_total,
            bytes_done = bytes_done,
            bps = bytes_per_sec,
            eta = eta_secs,
        );
        let tmp = self.progress_path.with_extension("json.tmp");
        if let Ok(mut f) = fs::File::create(&tmp) {
            let _ = f.write_all(json.as_bytes());
            let _ = f.sync_all();
            let _ = fs::rename(&tmp, &self.progress_path);
        }
    }
}

// Parse MemAvailable from /proc/meminfo. Returns bytes. Linux-only; returns
// None on macOS / other (caller falls back to a conservative default).
fn read_mem_available() -> Option<u64> {
    let content = fs::read_to_string("/proc/meminfo").ok()?;
    for line in content.lines() {
        if let Some(rest) = line.strip_prefix("MemAvailable:") {
            let kb: u64 = rest.trim().split_whitespace().next()?.parse().ok()?;
            return Some(kb * 1024);
        }
    }
    None
}

fn now_unix() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 3 {
        eprintln!("usage: wave_merge <in_dir> <out_dir>");
        eprintln!("  in_dir : directory of bucket_*.bin files (26-byte records, unsorted, dupes allowed)");
        eprintln!("  out_dir: where to write sorted+deduped bucket_*.bin files (the next wave)");
        eprintln!("env:");
        eprintln!("  WAVE_MERGE_DELETE_AFTER=1 : delete each input bucket once its output is written");
        eprintln!("                              (bounds peak disk progressively; works in parallel mode)");
        eprintln!("  WAVE_MERGE_SEQUENTIAL=1   : process buckets one at a time (override auto-parallel)");
        eprintln!("  WAVE_MERGE_PARALLEL=N     : cap concurrent buckets at N (default: auto from RAM)");
        eprintln!("  WAVE_MERGE_NO_RESUME=1    : ignore existing .done markers; reprocess everything");
        std::process::exit(2);
    }
    let in_dir = PathBuf::from(&args[1]);
    let out_dir = PathBuf::from(&args[2]);
    if !in_dir.is_dir() {
        eprintln!("not a directory: {}", in_dir.display());
        std::process::exit(2);
    }
    fs::create_dir_all(&out_dir).expect("create out_dir");

    let mut buckets: Vec<PathBuf> = fs::read_dir(&in_dir)
        .expect("read_dir")
        .filter_map(|e| e.ok())
        .map(|e| e.path())
        .filter(|p| {
            p.file_name()
                .and_then(|n| n.to_str())
                .map(|n| n.starts_with("bucket_") && n.ends_with(".bin"))
                .unwrap_or(false)
        })
        .collect();
    buckets.sort();

    let total_in: u64 = buckets
        .iter()
        .map(|p| fs::metadata(p).map(|m| m.len()).unwrap_or(0))
        .sum();
    let total_in_records = total_in / RECORD as u64;

    let no_resume = std::env::var("WAVE_MERGE_NO_RESUME").ok().is_some();
    let delete_after = std::env::var("WAVE_MERGE_DELETE_AFTER").ok().is_some();
    // delete_after no longer forces sequential — with the in-place map_copy sort,
    // peak per-bucket RAM is just the bucket size (was 2× via the Vec copy), so
    // bounded parallel + delete-after is safe as long as we cap thread count to
    // keep `parallel × max_bucket` under available RAM.
    let sequential = std::env::var("WAVE_MERGE_SEQUENTIAL").ok().is_some();

    // Partition buckets into "already done" (skip) and "to process".
    let mut skipped_buckets: Vec<(String, u64, u64)> = Vec::new();
    let mut to_process: Vec<PathBuf> = Vec::with_capacity(buckets.len());
    let mut bytes_in_pending: u64 = 0;
    let mut bytes_in_done_seed: u64 = 0;
    for path in &buckets {
        let name = path.file_name().unwrap().to_string_lossy().into_owned();
        let done_path = out_dir.join(format!("{}.done", name));
        let out_path = out_dir.join(&name);
        if !no_resume && done_path.exists() && out_path.exists() {
            // Restore the counts from the marker so the final summary is honest
            // even across a resumed run. Marker format: "<seen>\n<unique>\n".
            let (seen, uniq) = read_marker(&done_path).unwrap_or((0, 0));
            skipped_buckets.push((name, seen, uniq));
            bytes_in_done_seed += fs::metadata(path).map(|m| m.len()).unwrap_or(0);
        } else {
            // Stale .tmp from a previous crash — clean up so we get a fresh write.
            let tmp_path = out_dir.join(format!("{}.tmp", name));
            let _ = fs::remove_file(&tmp_path);
            // A bare .bin with no .done marker means partial output; remove it.
            if !no_resume && out_path.exists() && !done_path.exists() {
                let _ = fs::remove_file(&out_path);
            }
            bytes_in_pending += fs::metadata(path).map(|m| m.len()).unwrap_or(0);
            to_process.push(path.clone());
        }
    }

    println!(
        "wave_merge: {} input buckets, {:.2} GB ({} records) -> {}",
        buckets.len(),
        total_in as f64 / (1u64 << 30) as f64,
        total_in_records,
        out_dir.display(),
    );
    if !skipped_buckets.is_empty() {
        println!(
            "  resume: {} buckets already complete, {} to process ({:.2} GB pending)",
            skipped_buckets.len(),
            to_process.len(),
            bytes_in_pending as f64 / (1u64 << 30) as f64,
        );
    }
    // Choose parallelism. With map_copy sort, peak RAM per bucket ≈ bucket size
    // (one COW mapping). To stay under available RAM, cap concurrent buckets at
    // floor(0.75 × MemAvailable / max_bucket_size), clamped to [1, num_cpus].
    // User override via WAVE_MERGE_PARALLEL=N. Forced sequential mode = 1.
    let max_bucket: u64 = to_process
        .iter()
        .filter_map(|p| fs::metadata(p).ok().map(|m| m.len()))
        .max()
        .unwrap_or(0);
    let parallel_jobs: usize = if sequential {
        1
    } else if let Ok(s) = std::env::var("WAVE_MERGE_PARALLEL") {
        s.parse().unwrap_or(1).max(1)
    } else {
        let avail = read_mem_available().unwrap_or(8u64 << 30);
        let cpus = std::thread::available_parallelism().map(|n| n.get()).unwrap_or(8);
        let by_ram = if max_bucket == 0 { cpus } else {
            (((avail as u128) * 3 / 4) / (max_bucket as u128).max(1)) as usize
        };
        by_ram.clamp(1, cpus)
    };

    if !sequential && parallel_jobs > 1 {
        let _ = rayon::ThreadPoolBuilder::new()
            .num_threads(parallel_jobs)
            .build_global();
    }

    println!(
        "  mode: {}{}  (max bucket {:.2} GB, peak ~{:.1} GB)",
        if sequential || parallel_jobs == 1 { "sequential".to_string() } else { format!("parallel x{}", parallel_jobs) },
        if delete_after { ", delete-after" } else { "" },
        max_bucket as f64 / (1u64 << 30) as f64,
        parallel_jobs as f64 * max_bucket as f64 / (1u64 << 30) as f64,
    );

    // Seed totals from skipped buckets so resumed runs show the right grand totals.
    let progress = Arc::new(Progress {
        started_unix: now_unix(),
        started: Instant::now(),
        buckets_total: buckets.len(),
        buckets_done: AtomicUsize::new(0),
        buckets_skipped: AtomicUsize::new(skipped_buckets.len()),
        buckets_in_progress: AtomicUsize::new(0),
        records_seen: AtomicU64::new(skipped_buckets.iter().map(|(_, s, _)| *s).sum()),
        records_unique: AtomicU64::new(skipped_buckets.iter().map(|(_, _, u)| *u).sum()),
        bytes_in_total: total_in,
        bytes_in_done: AtomicU64::new(bytes_in_done_seed),
        stop: AtomicBool::new(false),
        progress_path: out_dir.join("progress.json"),
    });
    progress.write_snapshot();

    // Background progress writer. Uses park_timeout so the join at shutdown
    // doesn't have to wait out a full sleep interval.
    let progress_bg = Arc::clone(&progress);
    let progress_thread = thread::spawn(move || {
        while !progress_bg.stop.load(Ordering::Relaxed) {
            thread::park_timeout(Duration::from_secs(3));
            if progress_bg.stop.load(Ordering::Relaxed) {
                break;
            }
            progress_bg.write_snapshot();
        }
    });

    let t = Instant::now();

    let results: Vec<(String, u64, u64)> = if sequential {
        to_process
            .iter()
            .map(|path| {
                progress.buckets_in_progress.fetch_add(1, Ordering::Relaxed);
                let bytes = fs::metadata(path).map(|m| m.len()).unwrap_or(0);
                let r = process_bucket(path, &out_dir, &progress);
                if delete_after {
                    let _ = fs::remove_file(path);
                }
                progress.buckets_in_progress.fetch_sub(1, Ordering::Relaxed);
                progress.buckets_done.fetch_add(1, Ordering::Relaxed);
                progress.bytes_in_done.fetch_add(bytes, Ordering::Relaxed);
                r
            })
            .collect()
    } else {
        to_process
            .par_iter()
            .map(|path| {
                progress.buckets_in_progress.fetch_add(1, Ordering::Relaxed);
                let bytes = fs::metadata(path).map(|m| m.len()).unwrap_or(0);
                let r = process_bucket(path, &out_dir, &progress);
                if delete_after {
                    let _ = fs::remove_file(path);
                }
                progress.buckets_in_progress.fetch_sub(1, Ordering::Relaxed);
                progress.buckets_done.fetch_add(1, Ordering::Relaxed);
                progress.bytes_in_done.fetch_add(bytes, Ordering::Relaxed);
                r
            })
            .collect()
    };

    progress.stop.store(true, Ordering::Relaxed);
    progress_thread.thread().unpark();
    let _ = progress_thread.join();
    progress.write_snapshot();

    let grand_seen = progress.records_seen.load(Ordering::Relaxed);
    let grand_uniq = progress.records_unique.load(Ordering::Relaxed);

    println!();
    if !skipped_buckets.is_empty() {
        println!("  (skipped {} buckets resumed from prior run)", skipped_buckets.len());
    }
    for (name, seen, uniq) in &results {
        println!("  {name:>20}  records={seen:>14}  unique={uniq:>14}");
    }

    println!();
    println!("total records seen    : {grand_seen}");
    println!("total unique positions: {grand_uniq}");
    println!(
        "dedup ratio           : {:.2}x",
        if grand_uniq > 0 { grand_seen as f64 / grand_uniq as f64 } else { 0.0 }
    );
    println!("elapsed               : {:.1}s", t.elapsed().as_secs_f64());
}

fn process_bucket(
    path: &Path,
    out_dir: &Path,
    progress: &Progress,
) -> (String, u64, u64) {
    let name = path.file_name().unwrap().to_string_lossy().into_owned();
    let out_path = out_dir.join(&name);
    let tmp_path = out_dir.join(format!("{}.tmp", name));
    let done_path = out_dir.join(format!("{}.done", name));

    let file = fs::File::open(path).expect("open bucket");
    let len = file.metadata().expect("stat").len() as usize;
    if len == 0 {
        fs::File::create(&out_path).expect("create empty out");
        write_marker(&done_path, 0, 0);
        return (name, 0, 0);
    }
    assert_eq!(len % RECORD, 0, "bucket {} not a multiple of {} bytes", name, RECORD);
    let n = len / RECORD;

    // MAP_PRIVATE: sort+dedup in-place in COW pages. The original input file is
    // never written back (kernel allocates anonymous pages on first touch).
    // Replaces the previous Vec<[u8; RECORD]> copy — halves peak per-bucket RAM
    // (was mmap pages + Vec; now just COW pages) and skips a sequential RECORD-byte
    // memcpy of the entire bucket.
    let mut mmap = unsafe { MmapOptions::new().map_copy(&file).expect("mmap_copy") };

    let recs: &mut [[u8; RECORD]] = unsafe {
        let (head, mid, tail) = mmap.align_to_mut::<[u8; RECORD]>();
        // [u8; RECORD] has alignment 1; align_to_mut yields no head/tail padding.
        debug_assert!(head.is_empty() && tail.is_empty());
        debug_assert_eq!(mid.len(), n);
        mid
    };

    recs.sort_unstable();
    let before = recs.len() as u64;
    let unique_len = dedup_sorted_in_place(recs);
    let after = unique_len as u64;

    // Atomic publish: write to .tmp, fsync, rename to final path. Then touch
    // the .done marker. A crash between rename and marker re-runs the bucket,
    // which is correct because the output is overwritten.
    {
        let f = fs::File::create(&tmp_path).expect("create tmp");
        let mut out = std::io::BufWriter::with_capacity(1 << 20, f);
        // Reinterpret the deduped prefix as a flat byte slice for one bulk write.
        let unique_bytes = unsafe {
            std::slice::from_raw_parts(recs.as_ptr() as *const u8, unique_len * RECORD)
        };
        out.write_all(unique_bytes).expect("write");
        out.flush().expect("flush");
        out.get_ref().sync_all().ok();
    }
    fs::rename(&tmp_path, &out_path).expect("rename tmp to out");
    write_marker(&done_path, before, after);

    progress.records_seen.fetch_add(before, Ordering::Relaxed);
    progress.records_unique.fetch_add(after, Ordering::Relaxed);

    (name, before, after)
}

// Dedup a sorted slice in place, returning the new logical length. Equivalent to
// Vec::dedup on a sorted Vec but works on a borrowed slice so we can run it on
// the mmap'd region directly. Records past the returned length are stale.
fn dedup_sorted_in_place(s: &mut [[u8; RECORD]]) -> usize {
    if s.is_empty() {
        return 0;
    }
    let mut write = 1;
    for read in 1..s.len() {
        if s[read] != s[write - 1] {
            if read != write {
                s[write] = s[read];
            }
            write += 1;
        }
    }
    write
}

fn write_marker(path: &Path, seen: u64, unique: u64) {
    if let Ok(mut f) = fs::File::create(path) {
        let _ = writeln!(f, "{}", seen);
        let _ = writeln!(f, "{}", unique);
        let _ = f.sync_all();
    }
}

fn read_marker(path: &Path) -> Option<(u64, u64)> {
    let s = fs::read_to_string(path).ok()?;
    let mut it = s.lines();
    let seen: u64 = it.next()?.trim().parse().ok()?;
    let unique: u64 = it.next()?.trim().parse().ok()?;
    Some((seen, unique))
}
