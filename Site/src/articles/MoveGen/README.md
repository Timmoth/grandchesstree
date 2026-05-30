# MoveGen

The reference C# move generator the
[move-generation article series](../) builds toward. Used by:

- [`Perft-Checker/`](../../../../Perft-Checker/) as a baseline UCI engine for cross-validation
- the in-browser perft tester at [grandchesstree.com/perft-test](https://grandchesstree.com/perft-test) via the AOT WASM build
- the published articles as the canonical implementation

Passes perft on all six standard CPW positions; 119/119 xUnit tests green.

## Projects

| Project           | Role                                                            |
|-------------------|-----------------------------------------------------------------|
| `MoveGen.App`     | The move generator. Console app with a UCI mode and a `--demo`. |
| `MoveGen.Tests`   | xUnit tests, organised per article (`Part1Tests.cs`…`Part7Tests.cs`). |
| `MoveGen.Wasm`    | Browser-WASM build of `MoveGen.App`. See its own [README](MoveGen.Wasm/README.md). |

## Build & test

```sh
dotnet build MoveGen.sln
dotnet test MoveGen.Tests
```

## Run

```sh
# UCI mode (default)
dotnet run --project MoveGen.App

# Or a quick perft demo:
dotnet run --project MoveGen.App -- --demo
```

For use from external UCI harnesses (e.g. Perft-Checker during dev),
`movegen-engine.sh` is a thin wrapper around the dotnet host — avoids
codesigning the self-contained binary on Apple Silicon:

```sh
./movegen-engine.sh
```

## WASM build

The `MoveGen.Wasm/` project compiles the same source files to a browser-WASM
bundle. See [`MoveGen.Wasm/README.md`](MoveGen.Wasm/README.md) for prerequisites
(requires the `wasm-tools` workload), build steps, and deploy script.
