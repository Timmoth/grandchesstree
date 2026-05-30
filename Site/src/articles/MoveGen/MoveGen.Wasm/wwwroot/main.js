// Local smoke-test loader for the published AppBundle/. The real tool loads via
// WebApp/assets/movegen-wasm.js after we copy _framework/ over.
import { dotnet } from "./_framework/dotnet.js";

const runtime = await dotnet
  .withDiagnosticTracing(false)
  .withApplicationArgumentsFromQuery()
  .create();

await runtime.runMain();
const exports = await runtime.getAssemblyExports("MoveGen.Wasm");
const Exports = exports.MoveGen.Wasm.Exports;

window.MoveGenWasm = {
  perft: (fen, depth) => Number(Exports.Perft(fen, depth)),
  perftDivide: (fen, depth) => {
    const text = Exports.PerftDivide(fen, depth);
    if (!text) return [];
    return text.split("\n").map((line) => {
      const [uci, nodes] = line.split(" ");
      return { uci, nodes: BigInt(nodes) };
    });
  },
  engineInfo: () => Exports.EngineInfo(),
};

console.log(window.MoveGenWasm.engineInfo());
console.log("perft(startpos, 5) =", window.MoveGenWasm.perft(
  "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 5));
