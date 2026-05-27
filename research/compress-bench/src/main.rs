use std::env;
use std::fs::File;
use std::path::PathBuf;
use std::time::Instant;

use memmap2::MmapOptions;
use roaring::RoaringTreemap;
use sucds::mii_sequences::EliasFanoBuilder;
use sucds::Serializable;

fn main() {
    let args: Vec<String> = env::args().collect();
    if args.len() < 2 {
        eprintln!("usage: compress-bench <keys.bin> [--limit N]");
        eprintln!("  keys.bin: file of little-endian u64 keys, one per 8 bytes");
        std::process::exit(2);
    }

    let path = PathBuf::from(&args[1]);
    let mut limit: Option<usize> = None;
    let mut i = 2;
    while i < args.len() {
        match args[i].as_str() {
            "--limit" => {
                i += 1;
                limit = Some(args[i].parse().expect("--limit N"));
            }
            other => {
                eprintln!("unknown arg: {other}");
                std::process::exit(2);
            }
        }
        i += 1;
    }

    let file = File::open(&path).expect("open input");
    let mmap = unsafe { MmapOptions::new().map(&file).expect("mmap") };
    let total_u64 = mmap.len() / 8;
    let n = limit.map(|l| l.min(total_u64)).unwrap_or(total_u64);

    println!("input            : {}", path.display());
    println!("u64 count        : {n} (of {total_u64} in file)");

    // Load into Vec<u64>.
    let load_start = Instant::now();
    let mut keys: Vec<u64> = Vec::with_capacity(n);
    let bytes = &mmap[..n * 8];
    for chunk in bytes.chunks_exact(8) {
        keys.push(u64::from_le_bytes(chunk.try_into().unwrap()));
    }
    println!("load             : {:.2}s", load_start.elapsed().as_secs_f64());

    // -- 1. Raw (no compression, no sort) -----------------------------------
    let raw_bytes = (keys.len() as u64) * 8;
    report("raw u64 (unsorted)", raw_bytes, keys.len());

    // -- 2. Sort and dedup (defensive; input should already be unique) ------
    let sort_start = Instant::now();
    keys.sort_unstable();
    let before = keys.len();
    keys.dedup();
    let dups = before - keys.len();
    println!(
        "sort+dedup       : {:.2}s ({} dup keys removed)",
        sort_start.elapsed().as_secs_f64(),
        dups
    );
    let n = keys.len();
    let sorted_bytes = (n as u64) * 8;
    report("sorted raw u64", sorted_bytes, n);

    // -- 3. Sorted + gaps + LEB128 varint -----------------------------------
    let t = Instant::now();
    let mut varint_bytes: u64 = 0;
    let mut prev: u64 = 0;
    for &v in &keys {
        let gap = v - prev;
        varint_bytes += leb128_len(gap) as u64;
        prev = v;
    }
    println!("varint encode    : {:.2}s", t.elapsed().as_secs_f64());
    report("sorted + gap + LEB128", varint_bytes, n);

    // -- 4. Sorted + gaps + Golomb-Rice (b = floor(log2(mean gap))) ---------
    // Cheap to compute, near-optimal for geometric gaps.
    if n > 1 {
        let max = *keys.last().unwrap();
        let mean_gap = (max as f64) / (n as f64);
        let b_bits = mean_gap.log2().floor().max(0.0) as u32;
        let mut total_bits: u64 = 0;
        let mut prev: u64 = 0;
        for &v in &keys {
            let gap = v - prev;
            let q = gap >> b_bits;
            // unary q with terminator + b_bits remainder
            total_bits += q + 1 + b_bits as u64;
            prev = v;
        }
        let rice_bytes = (total_bits + 7) / 8;
        println!("golomb b         : {b_bits}");
        report("sorted + Golomb-Rice", rice_bytes, n);
    }

    // -- 5. Elias-Fano (sucds) ---------------------------------------------
    let t = Instant::now();
    let max_key = *keys.last().unwrap() as usize;
    let universe = max_key.saturating_add(1).max(n);
    let mut builder = EliasFanoBuilder::new(universe, n).expect("EF builder");
    for &v in &keys {
        builder.push(v as usize).expect("EF push");
    }
    let ef = builder.build();
    let ef_bytes = ef.size_in_bytes() as u64;
    println!("EF build         : {:.2}s", t.elapsed().as_secs_f64());
    report("Elias-Fano (sucds)", ef_bytes, n);

    // -- 6. Roaring tree map (u64 keys) -------------------------------------
    let t = Instant::now();
    let mut roar = RoaringTreemap::new();
    for &v in &keys {
        roar.insert(v);
    }
    let roar_bytes = roar.serialized_size() as u64;
    println!("Roaring build    : {:.2}s", t.elapsed().as_secs_f64());
    report("RoaringTreemap (serialized)", roar_bytes, n);

    // -- 7. Cuckoo filter at ~3% FPR (approximate; for context) -------------
    let t = Instant::now();
    let mut cf =
        cuckoofilter::CuckooFilter::<std::collections::hash_map::DefaultHasher>::with_capacity(
            n.next_power_of_two().max(64),
        );
    for &v in &keys {
        let _ = cf.add(&v);
    }
    let cf_bytes = cf.memory_usage() as u64;
    println!("Cuckoo build     : {:.2}s", t.elapsed().as_secs_f64());
    report("CuckooFilter (approx)", cf_bytes, n);

    println!();
    println!("note: Cuckoo is approximate (false positives); all others are exact.");
}

fn report(name: &str, bytes: u64, n: usize) {
    if n == 0 {
        println!("{name:<32} {bytes:>14} B   (n=0)");
        return;
    }
    let bpe = (bytes as f64) / (n as f64);
    println!(
        "{name:<32} {bytes:>14} B   {bpe:>6.3} B/entry   ({:>5.2} MB)",
        (bytes as f64) / (1024.0 * 1024.0)
    );
}

fn leb128_len(mut v: u64) -> usize {
    let mut n = 1;
    while v >= 0x80 {
        v >>= 7;
        n += 1;
    }
    n
}
