# Results

Raw perft outputs cited on [grandchesstree.com](https://grandchesstree.com/) —
checked into git so the published result tables and the source data stay
in sync.

## File naming

`perft_p{POS}_d{DEPTH}_{KIND}.{json,csv}`

| Field        | Values |
|--------------|--------|
| `POS` — studied position | `0` = start position · `1` = Kiwipete · `2` = SJE's Symmetric Alternative |
| `DEPTH`      | 0…12 (depth fully traversed at the time of writing) |
| `KIND`       | `total` (aggregate counts, JSON), `divide` (per-root subtree totals, CSV), `dump` (every task's per-bucket result, CSV) |

Plus the per-position summary file `perft_p{POS}_results.json` — position
metadata + every depth in one place.

## Counts captured per (position, depth)

- `nodes`, `captures`, `enpassants`, `castles`, `promotions`
- check breakdown: `direct_checks`, `single_discovered_check`,
  `direct_discovered_check`, `double_discovered_check`, `total_checks`
- checkmate breakdown: `direct_checkmate`,
  `single_discovered_checkmate`, `direct_discoverd_checkmate`,
  `double_discoverd_checkmate`, `total_mates`
- run metadata: `started_at`, `finished_at`, `total_tasks`, `contributors`

(typo in `direct_discoverd_checkmate` / `double_discoverd_checkmate` is in
the API schema; preserved here to keep the data ingestible by existing
tooling.)

## process.py

Tiny analysis helper. Loads a `perft_p{POS}_d{DEPTH}_dump.csv` into pandas
for ad-hoc queries.

```sh
python -m venv venv && source venv/bin/activate
pip install -r requirements.txt
python process.py    # prompts for position and depth
```
