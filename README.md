# The Grand Chess Tree

[grandchesstree.com](https://grandchesstree.com/) · [Discord](https://discord.gg/cTu3aeCZVe)

A community-driven project to traverse the depths of chess: counting **perft**
nodes at extreme depths from notable starting positions, with the tooling,
reference code, and writing to do it well.

## What's in this repo

| Where                          | What                                                                                                       |
|--------------------------------|------------------------------------------------------------------------------------------------------------|
| `Engine/`                      | The perft engine + external-memory BFS pipeline for unique-position counting. `GrandChessTree.sln`.        |
| `Site/src/articles/MoveGen/`   | Reference C# move generator that the article series builds toward. Used by Perft-Checker and the in-browser perft tester (AOT WASM). |
| `Perft-Checker/`               | `perftcheck` — CLI that validates any UCI engine's `go perft N` against ~28k known-correct positions.      |
| `Leaderboard/`                 | Benchmark harness ranking engines + move-gen libraries by perft NPS. Includes a FEN-corpus build pipeline. |
| `Unique-Positions/`            | Rust merger (`wave_merge`) that pairs with the BFS pipeline in `Engine/`.                                  |
| `Site/`                        | Source for [grandchesstree.com](https://grandchesstree.com/) (vanilla HTML/JS, one Node build script).     |
| `Site/src/results/`            | Raw perft outputs — every depth, every studied position, machine-readable.                                 |

Each directory has its own README with build, run, and architecture details.

## Headline perft numbers

Leaf counts from the standard starting position:

| Depth | Nodes                 |
|------:|----------------------:|
|     6 |           119,060,324 |
|     8 |        84,998,978,956 |
|    10 |    69,352,859,712,417 |
|    11 | 2,097,651,003,696,806 |

Full per-position depth tables (including captures, castles, promotions, and
checkmates) live in [`Site/src/results/`](Site/src/results/) and on
[grandchesstree.com](https://grandchesstree.com/) under each studied position:
[startpos](https://grandchesstree.com/startpos.html) ·
[kiwipete](https://grandchesstree.com/kiwipete.html) ·
[sje](https://grandchesstree.com/sje.html).

## Get involved

- **Collaborate** — pick any subdirectory and read its README.
- **Compete** — write a faster move generator. `perftcheck` validates any
  UCI engine against the bundled corpus; `Leaderboard` ranks by NPS.

[Join the Discord](https://discord.gg/cTu3aeCZVe) for coordination, results,
and engine-dev chat.

## License

[MIT](LICENSE).
