// External merger for BucketSpillSink output.
//
// Input: a directory of `bucket_NNNN.bin` files, each holding 16-byte
// (h1, h2) records (little-endian u64 each).
// Output: per-bucket unique counts and a grand total.
//
// Memory: bounded by the largest bucket file (read mmap'd, sorted in RAM as
// Vec<u128>). Per-bucket work is parallel via rayon.

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Instant;

use memmap2::MmapOptions;
use rayon::prelude::*;

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.len() < 2 {
        eprintln!("usage: merge <bucket_dir>");
        std::process::exit(2);
    }
    let dir = PathBuf::from(&args[1]);
    if !dir.is_dir() {
        eprintln!("not a directory: {}", dir.display());
        std::process::exit(2);
    }

    let mut buckets: Vec<PathBuf> = fs::read_dir(&dir)
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

    let total_size: u64 = buckets
        .iter()
        .map(|p| fs::metadata(p).map(|m| m.len()).unwrap_or(0))
        .sum();
    println!(
        "buckets: {}   total spill: {:.2} GB ({} records)",
        buckets.len(),
        total_size as f64 / (1u64 << 30) as f64,
        total_size / 16
    );

    let total_unique = AtomicU64::new(0);
    let total_seen = AtomicU64::new(0);

    let t = Instant::now();

    let results: Vec<(String, u64, u64)> = buckets
        .par_iter()
        .map(|path| process_bucket(path, &total_unique, &total_seen))
        .collect();

    println!();
    for (name, seen, uniq) in &results {
        println!("{name:>20}  records={seen:>14}  unique={uniq:>14}");
    }

    let grand_seen = total_seen.load(Ordering::Relaxed);
    let grand_uniq = total_unique.load(Ordering::Relaxed);

    println!();
    println!("total records seen   : {grand_seen}");
    println!("total unique positions: {grand_uniq}");
    println!("dedup ratio          : {:.2}x", grand_seen as f64 / grand_uniq as f64);
    println!("elapsed              : {:.1}s", t.elapsed().as_secs_f64());
}

fn process_bucket(
    path: &Path,
    total_unique: &AtomicU64,
    total_seen: &AtomicU64,
) -> (String, u64, u64) {
    let name = path
        .file_name()
        .unwrap()
        .to_string_lossy()
        .into_owned();
    let file = fs::File::open(path).expect("open bucket");
    let len = file.metadata().expect("stat").len() as usize;
    if len == 0 {
        return (name, 0, 0);
    }
    assert_eq!(len % 16, 0, "bucket {} not a multiple of 16 bytes", name);
    let n = len / 16;

    let mmap = unsafe { MmapOptions::new().map(&file).expect("mmap") };

    // Load into Vec<u128> for in-place sort. Each entry encoded as
    //   (h1 as u128) | ((h2 as u128) << 64)
    // so that lexicographic u128 ordering puts h1 in the high half — fine, equal h1 sorts by h2.
    // Actually we want primary order on h1 then h2; pack (h1 << 64) | h2 so sort is correct.
    let mut keys: Vec<u128> = Vec::with_capacity(n);
    let bytes = &mmap[..];
    for chunk in bytes.chunks_exact(16) {
        let h1 = u64::from_le_bytes(chunk[0..8].try_into().unwrap());
        let h2 = u64::from_le_bytes(chunk[8..16].try_into().unwrap());
        let packed = ((h1 as u128) << 64) | (h2 as u128);
        keys.push(packed);
    }

    keys.sort_unstable();
    let before = keys.len() as u64;
    keys.dedup();
    let after = keys.len() as u64;

    total_seen.fetch_add(before, Ordering::Relaxed);
    total_unique.fetch_add(after, Ordering::Relaxed);

    (name, before, after)
}
