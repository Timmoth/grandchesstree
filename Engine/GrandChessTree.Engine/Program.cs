using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Moves;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Engine
{
    internal class Program
    {
        // UCI shim state (for perftcheck / other UCI drivers).
        static Board _uciBoard;
        static bool _uciBoardSet;
        static bool _uciWhiteToMove;

        static void Main(string[] args)
        {
            string? input;
            while ((input = Console.ReadLine()) != "quit")
            {
                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("Invalid command.");
                    Console.WriteLine("type 'help' for more info.");
                    continue;
                }

                // Minimal UCI subset for perftcheck-style drivers.
                // Recognised: uci, isready, ucinewgame, setoption, position fen ..., position startpos, go perft N.
                if (HandleUci(input))
                    continue;

                var commandParts = input.Split(':');

                if (commandParts.Length == 0)
                {
                    Console.WriteLine("Invalid command.");
                    Console.WriteLine("type 'help' for more info.");
                    continue;
                }

                var command = commandParts[0].ToLower();
                if(command == "help" || command == "h")
                {
                    Console.WriteLine("commands:");
                    Console.WriteLine("help                                        - this output");
                    Console.WriteLine("stats:<depth>:<mb_hash>:<fen>               - calculates the full perft stats, single-threaded");
                    Console.WriteLine("stats_mt:<depth>:<mb_hash>:<threads>:<fen>  - calculates the full perft stats, multi-threaded");
                    Console.WriteLine("nodes:<depth>:<mb_hash>:<fen>               - calculates the perft nodes, single-threaded");
                    Console.WriteLine("nodes_mt:<depth>:<mb_hash>:<threads>:<fen>  - calculates the perft nodes, multi-threaded");
                    Console.WriteLine("divide:<depth>:<mb_hash>:<fen>[:<moves>]   - calculates the perft nodes for each move, single-threaded.");
                    Console.WriteLine("                                              optional <moves> is a space-separated list of UCI moves applied to <fen> first;");
                    Console.WriteLine("                                              the trailing 'fen:' line in the result then carries the resulting FEN — used by perftcheck drill-down.");
                    Console.WriteLine("divide_mt:<depth>:<mb_hash>:<threads>:<fen> - calculates the perft nodes for each move, multi-threaded");
                    Console.WriteLine("unique:<depth>:<mb_hash>:<fen>              - calculates the number of unique positions, single-threaded");
                    Console.WriteLine("unique_mt:<depth>:<mb_hash>:<threads>:<fen> - same as unique, multi-threaded");
                    Console.WriteLine("unique_dump:<depth>:<mb_hash>:<path>:<fen>  - unique, also dumps each inserted key to <path> (raw u64 LE)");
                    Console.WriteLine("unique_mt_dump:<depth>:<mb_hash>:<threads>:<path>:<fen> - unique_mt with key dump");
                    Console.WriteLine("unique_spill_mt:<depth>:<mb_hash>:<threads>:<buckets>:<out_dir>:<fen> - scalable: 128-bit keys streamed to K bucket files; post-process with the Rust merger");
                    Console.WriteLine("wave_init:<buckets>:<out_dir>:<fen>           - BFS wave[0]: write 1 starting position to bucket files (26-byte records)");
                    Console.WriteLine("wave_expand:<in_dir>:<out_dir>:<buckets>:<threads> - BFS step: read positions from in_dir, expand each by 1 ply, spill children to out_dir buckets");
                    Console.WriteLine("exit                                        - closes the program");
                    Console.WriteLine("clear                                       - clears the console output");
                    Console.WriteLine("parameters:");
                    Console.WriteLine("<depth>    - the number of ply to search up to");
                    Console.WriteLine("<mb_hash>  - the hash table size in MB per thread");
                    Console.WriteLine("<threads>  - the number of threads to use in a multi-threaded command");
                    Console.WriteLine("<fen>      - the fen string for the position to search");
                    Console.WriteLine("special positions can be used in place of their fen:");
                    Console.WriteLine("start      - rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                    Console.WriteLine("kiwipete   - r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -");
                    Console.WriteLine("sje        - r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10");
                }else if(command == "exit")
                {
                    Environment.Exit(0);
                    return;
                }else if(command == "clear")
                {
                    Console.Clear();
                }
                else if (command == "stats")
                {
                    RunPerftStats(commandParts);
                }
                else if (command == "stats_mt")
                {
                    RunPerftStatsMt(commandParts);
                }
                else if (command == "nodes")
                {
                    RunPerftNodes(commandParts);
                }
                else if (command == "nodes_mt")
                {
                    RunPerftNodesMt(commandParts);
                }
                else if (command == "divide")
                {
                    RunDivideNodes(commandParts);
                }
                else if (command == "divide_mt")
                {
                    RunDivideNodesMt(commandParts);
                }
                else if (command == "unique")
                {
                    RunPerftUnique(commandParts);
                }
                else if (command == "unique_mt")
                {
                    RunUniqueMt(commandParts);
                }
                else if (command == "unique_dump")
                {
                    RunPerftUniqueDump(commandParts);
                }
                else if (command == "unique_mt_dump")
                {
                    RunUniqueMtDump(commandParts);
                }
                else if (command == "unique_spill_mt")
                {
                    RunUniqueSpillMt(commandParts);
                }
                else if (command == "unique_spill_mt128")
                {
                    RunUniqueSpillMt128(commandParts);
                }
                else if (command == "wave_init")
                {
                    RunWaveInit(commandParts);
                }
                else if (command == "wave_expand")
                {
                    RunWaveExpand(commandParts);
                }
                else if (command == "decode_fen")
                {
                    var (brd, wtm) = BoardStateSerialization.Deserialize(commandParts[1]);
                    Console.WriteLine(brd.ToFen(wtm, 0, 1));
                }
            }           
        }

        // Returns true if the line was a recognised UCI command and was handled.
        // Used so perftcheck can drive the engine with no wrapper.
        static bool HandleUci(string input)
        {
            if (input == "uci")
            {
                Console.WriteLine("id name GrandChessTree");
                Console.WriteLine("id author Tim Jones");
                Console.WriteLine("uciok");
                Console.Out.Flush();
                return true;
            }
            if (input == "isready")
            {
                Console.WriteLine("readyok");
                Console.Out.Flush();
                return true;
            }
            if (input == "ucinewgame" || input.StartsWith("setoption ", StringComparison.Ordinal))
            {
                return true; // no-op
            }
            if (input.StartsWith("position ", StringComparison.Ordinal))
            {
                var rest = input.Substring("position ".Length).Trim();
                string fen;
                if (rest.StartsWith("startpos", StringComparison.Ordinal))
                {
                    fen = Constants.StartPosFen;
                }
                else if (rest.StartsWith("fen ", StringComparison.Ordinal))
                {
                    fen = rest.Substring(4);
                }
                else
                {
                    return true;
                }
                // Strip optional " moves ..." suffix. We don't apply moves (perft test cases don't use them).
                int movesIdx = fen.IndexOf(" moves ", StringComparison.Ordinal);
                if (movesIdx >= 0) fen = fen.Substring(0, movesIdx);
                try
                {
                    var (board, wtm) = FenParser.Parse(fen.Trim());
                    _uciBoard = board;
                    _uciWhiteToMove = wtm;
                    _uciBoardSet = true;
                }
                catch
                {
                    _uciBoardSet = false;
                }
                return true;
            }
            if (input.StartsWith("go perft ", StringComparison.Ordinal))
            {
                var depthStr = input.Substring("go perft ".Length).Trim();
                if (!int.TryParse(depthStr, out var depth) || !_uciBoardSet)
                {
                    Console.WriteLine("Nodes searched: 0");
                    Console.Out.Flush();
                    return true;
                }
                PerftBulk.AllocateHashTable(64);
                var nodes = PerftBulk.PerftRootBulk(ref _uciBoard, depth, _uciWhiteToMove);
                Console.WriteLine($"Nodes searched: {nodes}");
                Console.Out.Flush();
                return true;
            }
            // Swallow other UCI commands silently so they don't trigger the
            // "Invalid command" path.
            if (input.StartsWith("go ", StringComparison.Ordinal) || input == "stop")
                return true;
            return false;
        }

        public static string ResolveFen(string input)
        {
            if(input == "start")
            {
                return Constants.StartPosFen;
            }else if(input == "kiwipete")
            {
                return Constants.KiwiPeteFen;
            }else if (input == "sje")
            {
                return Constants.SjeFen;
            }
            else
            {
                return input;
            }
        }

        public static void RunPerftStats(string[] commandParts)
        {
            if (commandParts.Length != 4 ||
                      !int.TryParse(commandParts[1], out var depth) ||
                      !int.TryParse(commandParts[2], out var mbHash))
            {
                Console.WriteLine("Invalid command format is 'stats:<depth>:<mb_hash>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var (board, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[3]));
            Summary summary = default;
            Perft.AllocateHashTable(mbHash);

            var sw = Stopwatch.StartNew();
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);
            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = summary.Nodes / s;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"nps: {(nps).FormatBigNumber()}");
            Console.WriteLine($"time: {ms}ms");
            summary.Print();
            Console.WriteLine("-----------------");
        }

        public static void RunPerftStatsMt(string[] commandParts)
        {
            if (commandParts.Length != 5 ||
                !int.TryParse(commandParts[1], out var depth) ||
                !int.TryParse(commandParts[2], out var mbHash) ||
                 !int.TryParse(commandParts[3], out var threadCount))
            {
                Console.WriteLine("Invalid command format is 'stats_mt:<depth>:<mb_hash>:<threads>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var launchDepth = 0;
            if (depth > 7)
            {
                launchDepth = 3;
            }
            else if (depth > 4)
            {
                launchDepth = 2;
            }
            else
            {
                launchDepth = 1;
            }

            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[4]));

            var divideResults = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);
            var sw = Stopwatch.StartNew();
            ulong totalNodes = 0;

            Thread[] threads = new Thread[threadCount];

            var queue = new ConcurrentQueue<(ulong hash, string fen, int occurrences)>(divideResults);
            Summary totalSummary = default;

            object lockObj = new();
            for (int i = 0; i < threads.Length; i++)
            {
                var index = i;

                threads[index] = new Thread(() =>
                {
                    Perft.AllocateHashTable(mbHash);
                    Summary summary = default;

                    var count = 0;
                    while (queue.TryDequeue(out var item))
                    {
                        var (board, wtm) = FenParser.Parse(item.fen);
                        summary = default;
                        Perft.PerftRoot(ref board, ref summary, depth - launchDepth, wtm);

                        lock (lockObj)
                        {
                            totalNodes += summary.Nodes * (ulong)item.occurrences;
                            totalSummary.Accumulate(ref summary, (ulong)item.occurrences);
                        }

                        count++;
                        if (index == 0 && count % 2 == 0)
                        {
                            var ms = sw.ElapsedMilliseconds;
                            var s = (float)ms / 1000;
                            var nps = totalNodes / s;
                            Console.WriteLine($"nps:{(nps).FormatBigNumber()} {s}s nodes:{totalNodes.FormatBigNumber()}");
                        }
                    }

                    PerftBulk.FreeHashTable();
                });
                threads[index].Start();
            }

            // Wait for all threads to complete
            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = totalNodes / s;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"nps: {(nps).FormatBigNumber()}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine($"hash: {initialBoard.Hash}");
            Console.WriteLine($"fen: {initialBoard.ToFen(whiteToMove, 0, 1)}");
            totalSummary.Print();
            Console.WriteLine("-----------------");

        }

        public static void RunPerftNodes(string[] commandParts)
        {
            if (commandParts.Length != 4 ||
                !int.TryParse(commandParts[1], out var depth) ||
                !int.TryParse(commandParts[2], out var mbHash))
            {
                Console.WriteLine("Invalid command format is 'nodes:<depth>:<mb_hash>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }


            var (board, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[3]));
            PerftBulk.AllocateHashTable(mbHash);
            var sw = Stopwatch.StartNew();
            var nodes = PerftBulk.PerftRootBulk(ref board, depth, whiteToMove);
            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = nodes / s;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"nodes: {nodes}");
            Console.WriteLine($"nps: {(nps).FormatBigNumber()}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine($"hash: {board.Hash}");
            Console.WriteLine($"fen: {board.ToFen(whiteToMove, 0, 1)}");
            Console.WriteLine("-----------------");
        }

        public static void RunDivideNodes(string[] commandParts)
        {
            if (commandParts.Length < 4 || commandParts.Length > 5 ||
                !int.TryParse(commandParts[1], out var depth) ||
                !int.TryParse(commandParts[2], out var mbHash))
            {
                Console.WriteLine("Invalid command format is 'divide:<depth>:<mb_hash>:<fen>[:<moves>]'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            if(depth == 0)
            {
                Console.WriteLine("depth must be greater then 1");
            }

            var sw = Stopwatch.StartNew();
            var (board, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[3]));
            PerftBulk.AllocateHashTable(mbHash);

            // Optional 5th field: space-separated UCI moves to apply before
            // running divide. Lets perftcheck drill down through a move
            // sequence in a single round-trip and read the resulting FEN
            // off the trailing 'fen:' line.
            if (commandParts.Length == 5 &&
                !string.IsNullOrWhiteSpace(commandParts[4]))
            {
                foreach (var token in commandParts[4]
                             .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!TryApplyUciMove(ref board, ref whiteToMove, token))
                    {
                        Console.WriteLine($"error: illegal move '{token}'");
                        Console.WriteLine("-----results-----");
                        Console.WriteLine("nodes: 0");
                        Console.WriteLine("-----------------");
                        return;
                    }
                }
            }

            Span<uint> moves = stackalloc uint[218];
            var totalNodes = 0ul;
            var moveCount = MoveGenerator.GenerateMoves(ref moves, ref board, whiteToMove);
            for (int j = 0; j < moveCount; j++)
            {
                var move = moves[j];
                var newBoard = board;
                if (whiteToMove)
                {
                    newBoard.ApplyWhiteMove(move);
                }
                else
                {
                    newBoard.ApplyBlackMove(move);
                }

                var nodes = PerftBulk.PerftRootBulk(ref newBoard, depth - 1, !whiteToMove);
                totalNodes += nodes;
                Console.WriteLine($"{move.ToUciMoveName()} {nodes}");
            }

            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = totalNodes / s;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"nodes: {totalNodes}");
            Console.WriteLine($"nps: {(nps).FormatBigNumber()}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine($"hash: {board.Hash}");
            Console.WriteLine($"fen: {board.ToFen(whiteToMove, 0, 1)}");
            Console.WriteLine("-----------------");
        }

        public static void RunDivideNodesMt(string[] commandParts)
        {
            if (commandParts.Length != 5 ||
                 !int.TryParse(commandParts[1], out var depth) ||
                 !int.TryParse(commandParts[2], out var mbHash) ||
                  !int.TryParse(commandParts[3], out var threadCount))
            {
                Console.WriteLine("Invalid command format is 'divide_mt:<depth>:<mb_hash>:<threads>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var sw = Stopwatch.StartNew();
            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[4]));

            Span<uint> moves = new uint[218];
            var totalNodes = 0ul;
            var moveCount = MoveGenerator.GenerateMoves(ref moves, ref initialBoard, whiteToMove);

            var queue = new ConcurrentQueue<uint>();
            for(var i = 0; i < moveCount; i++)
            {
                queue.Enqueue(moves[i]);
            }

            Thread[] threads = new Thread[threadCount];
            object lockObj = new();
            for (int i = 0; i < threads.Length; i++)
            {
                var index = i;

                threads[index] = new Thread(() =>
                {
                    PerftBulk.AllocateHashTable(mbHash);
                    while (queue.TryDequeue(out var move))
                    {
                        var newBoard = initialBoard;
                        if (whiteToMove)
                        {
                            newBoard.ApplyWhiteMove(move);
                        }
                        else
                        {
                            newBoard.ApplyBlackMove(move);
                        }

                        var nodes = PerftBulk.PerftRootBulk(ref newBoard, depth - 1, !whiteToMove);

                        Console.WriteLine($"{move.ToUciMoveName()} {nodes}");

                        lock (lockObj)
                        {
                            totalNodes += nodes;
                        }
                    }

                    PerftBulk.FreeHashTable();
                });
                threads[index].Start();
            }

            // Wait for all threads to complete
            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = totalNodes / s;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"nodes: {totalNodes}");
            Console.WriteLine($"nps: {(nps).FormatBigNumber()}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine($"hash: {initialBoard.Hash}");
            Console.WriteLine($"fen: {initialBoard.ToFen(whiteToMove, 0, 1)}");
            Console.WriteLine("-----------------");
        }

        public static void RunPerftNodesMt(string[] commandParts)
        {
            if (commandParts.Length != 5 ||
          !int.TryParse(commandParts[1], out var depth) ||
          !int.TryParse(commandParts[2], out var mbHash) ||
           !int.TryParse(commandParts[3], out var threadCount))
            {
                Console.WriteLine("Invalid command format is 'nodes_mt:<depth>:<mb_hash>:<threads>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            // launchDepth: split work into this-many-ply leaf nodes for the worker queue.
            // Bigger launch = more, smaller work items = better load balance, more queue overhead.
            // Heuristic: aim for at least ~threadCount*256 work items so the slowest thread
            // doesn't dominate; cap at depth-2 so each subtree is still substantive.
            var launchDepth = 1;
            if (depth >= 10) launchDepth = 5;       // ~5M items, hugely balanced for deep runs
            else if (depth >= 8) launchDepth = 4;   // ~200K items, ~8K/thread
            else if (depth >= 6) launchDepth = 3;
            else if (depth >= 5) launchDepth = 2;
            // depth-2 cap so each leaf has at least perft(2) of work to do
            if (launchDepth > depth - 2) launchDepth = Math.Max(1, depth - 2);

            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[4]));

            var divideResults = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);
            var sw = Stopwatch.StartNew();
            ulong totalNodes = 0;

            Thread[] threads = new Thread[threadCount];

            var queue = new ConcurrentQueue<(ulong hash, string fen, int occurrences)>(divideResults);

            // Shared TT: one global allocation. mbHash is now interpreted as the
            // TOTAL table size (was per-thread before). Shared TTs benefit from
            // being just big enough to cover the hot set — over-allocating hurts
            // cache locality. Typical sweet spot is 128-512 MB regardless of
            // thread count.
            PerftBulk.AllocateHashTable(mbHash);

            object lockObj = new();
            for (int i = 0; i < threads.Length; i++)
            {
                var index = i;

                threads[index] = new Thread(() =>
                {
                    var count = 0;
                    while (queue.TryDequeue(out var item))
                    {
                        var (board, wtm) = FenParser.Parse(item.fen);
                        var nodes = PerftBulk.PerftRootBulk(ref board, depth - launchDepth, wtm);

                        lock (lockObj)
                        {
                            totalNodes += nodes * (ulong)item.occurrences;
                        }

                        count++;
                        if (index == 0 && count % 2 == 0)
                        {
                            var ms = sw.ElapsedMilliseconds;
                            var s = (float)ms / 1000;
                            var nps = totalNodes / s;
                            Console.WriteLine($"nps:{(nps).FormatBigNumber()} {s}s nodes:{totalNodes.FormatBigNumber()}");
                        }
                    }
                });
                threads[index].Start();
            }

            // Wait for all threads to complete
            foreach (Thread thread in threads)
            {
                thread.Join();
            }
            PerftBulk.FreeHashTable();

            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = totalNodes / s;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"nodes: {totalNodes}");
            Console.WriteLine($"nps: {(nps).FormatBigNumber()}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine($"hash: {initialBoard.Hash}");
            Console.WriteLine($"fen: {initialBoard.ToFen(whiteToMove, 0, 1)}");
            Console.WriteLine("-----------------");
        }

         public static void RunPerftUnique(string[] commandParts)
        {
            if (commandParts.Length != 4 ||
                 !int.TryParse(commandParts[1], out var depth) ||
                 !int.TryParse(commandParts[2], out var mbHash))
            {
                Console.WriteLine("Invalid command format is 'unique:<depth>:<mb_hash>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            PerftUnique.AllocateHashTable(mbHash);
            PerftUnique.UniquePositions.Clear();

            var (board, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[3]));
            var sw = Stopwatch.StartNew();
            PerftUnique.PerftRootUnique(ref board, depth, whiteToMove);
            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"unique positions: {(ulong)PerftUnique.UniquePositions.Count}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine("-----------------");
        }

        public static void RunUniqueMt(string[] commandParts)
        {
            if (commandParts.Length != 5 ||
                           !int.TryParse(commandParts[1], out var depth) ||
                           !int.TryParse(commandParts[2], out var mbHash) ||
                            !int.TryParse(commandParts[3], out var threadCount))
            {
                Console.WriteLine("Invalid command format is 'unique_mt:<depth>:<mb_hash>:<threads>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var launchDepth = 0;
            if (depth > 7)
            {
                launchDepth = 3;
            }
            else if (depth > 4)
            {
                launchDepth = 2;
            }
            else
            {
                launchDepth = 1;
            }

            var sw = Stopwatch.StartNew();
            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[4]));

            var launchNodes = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);

            var queue = new ConcurrentQueue<string>(launchNodes.Select(n => n.fen));
       
            PerftUnique.UniquePositions.Clear();
            // Shared TT: mbHash interpreted as TOTAL size, allocated once.
            PerftUnique.AllocateHashTable(mbHash);

            Thread[] threads = new Thread[threadCount];

            object lockObj = new();

            for (int i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[index] = new Thread(() =>
                {
                    var count = 0;
                    while (queue.TryDequeue(out var fen))
                    {
                        var (board, wtm) = FenParser.Parse(fen);
                        PerftUnique.PerftRootUnique(ref board, depth - launchDepth, wtm);

                        count++;
                        if(index == 0 && count % 2 == 0)
                        {
                            Console.WriteLine($"unique positions: {((ulong)PerftUnique.UniquePositions.Count).FormatBigNumber()} {PerftUnique.UniquePositions.PercentFull * 100}%");
                        }
                    }
                });
                threads[index].Start();
            }

            // Wait for all threads to complete
            foreach (Thread thread in threads)
            {
                thread.Join();
            }
            PerftUnique.FreeHashTable();

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"unique positions: {(ulong)PerftUnique.UniquePositions.Count}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine("-----------------");
        }

        // Same shape as RunUniqueSpillMt but uses the 128-bit memtable.
        // log2_memtable_slots: 2^N slots × 16 B per slot. So 31 = 32 GB, 32 = 64 GB.
        public static void RunUniqueSpillMt128(string[] commandParts)
        {
            if (commandParts.Length != 10 ||
                !int.TryParse(commandParts[1], out var depth) ||
                !int.TryParse(commandParts[2], out var mbPerftTt) ||
                !int.TryParse(commandParts[3], out var log2MemTable) ||
                !int.TryParse(commandParts[4], out var threadCount) ||
                !int.TryParse(commandParts[5], out var numBuckets) ||
                !int.TryParse(commandParts[6], out var shardCount) ||
                !int.TryParse(commandParts[7], out var shardId))
            {
                Console.WriteLine("Invalid command format is 'unique_spill_mt128:<depth>:<mb_perft_tt>:<log2_memtable_slots>:<threads>:<buckets>:<shards>:<shard_id>:<out_dir>:<fen>'.");
                return;
            }

            var outDir = commandParts[8];

            int launchDepth;
            if (depth > 7) launchDepth = 3;
            else if (depth > 4) launchDepth = 2;
            else launchDepth = 1;

            PerftUnique.ReallocateMemTable128(log2MemTable);
            PerftUnique.SetShard(shardCount, shardId);
            // Shared perft TT: mbPerftTt is TOTAL size, allocated once.
            PerftUnique.AllocateHashTable(mbPerftTt);

            var sw = Stopwatch.StartNew();
            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[9]));
            var launchNodes = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);
            var queue = new ConcurrentQueue<string>(launchNodes.Select(n => n.fen));

            using var sink = new BucketSpillSink(outDir, numBuckets);
            PerftUnique.SpillSink = sink;

            long progressBytes = 0;
            var threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[index] = new Thread(() =>
                {
                    var count = 0;
                    while (queue.TryDequeue(out var fen))
                    {
                        var (board, wtm) = FenParser.Parse(fen);
                        PerftUnique.PerftRootUnique(ref board, depth - launchDepth, wtm);
                        count++;
                        if (index == 0 && count % 4 == 0)
                        {
                            var bytes = sink.TotalBytesWritten;
                            var delta = bytes - progressBytes;
                            progressBytes = bytes;
                            var mt = PerftUnique.UniquePositions128!;
                            Console.WriteLine($"spilled: {((ulong)bytes).FormatBigNumber()}B (+{((ulong)delta).FormatBigNumber()}B since last), memtable128 {mt.PercentFull * 100:0.00}%");
                        }
                    }
                });
                threads[index].Start();
            }
            foreach (var t in threads) t.Join();

            sink.FlushAll();
            PerftUnique.SpillSink = null;
            var totalBytes = sink.TotalBytesWritten;
            var mt128 = PerftUnique.UniquePositions128!;
            var memCount = (ulong)mt128.Count;
            var memLoadFinal = mt128.PercentFull * 100;
            PerftUnique.FreeMemTable128();
            PerftUnique.FreeHashTable();

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results (128-bit memtable)-----");
            Console.WriteLine($"shard:         {shardId} / {shardCount}");
            Console.WriteLine($"memtable hits: {memCount} unique (final load {memLoadFinal:0.00}%)");
            Console.WriteLine($"total records: {totalBytes / 16}");
            Console.WriteLine($"total bytes:   {totalBytes}");
            Console.WriteLine($"buckets:       {numBuckets} files in {outDir}");
            Console.WriteLine($"walk time:     {ms}ms");
            Console.WriteLine("merge with: ./target/release/merge " + outDir);
            Console.WriteLine("-----------------");
        }

        // wave_init: serialize the starting position into the output wave dir (one 26-byte record).
        // Used as the seed for BFS wave 0.
        public static void RunWaveInit(string[] commandParts)
        {
            if (commandParts.Length != 4 ||
                !int.TryParse(commandParts[1], out var numBuckets))
            {
                Console.WriteLine("Invalid command format is 'wave_init:<buckets>:<out_dir>:<fen>'.");
                return;
            }
            var outDir = commandParts[2];
            var (board, wtm) = FenParser.Parse(ResolveFen(commandParts[3]));

            using var sink = new BucketPositionSpillSink(outDir, numBuckets);
            // Canonicalize EP for the seed position. (Start position has no EP set anyway.)
            bool epAvailable = false;
            if (board.EnPassantFile < 8)
            {
                epAvailable = wtm ? board.CanWhitePawnEnpassant() : board.CanBlackPawnEnpassant();
            }
            if (!epAvailable && board.EnPassantFile < 8) board.EnPassantFile = 8;

            Span<byte> buf = stackalloc byte[BucketPositionSpillSink.RecordSize];
            buf.Clear();
            BoardStateSerialization.WriteToSpan(ref board, buf, wtm);
            // Route by Zobrist hash (recompute on the canonical board to match how wave_expand routes).
            ulong hash = Zobrist.CalculateZobristKey(ref board, wtm);
            sink.Record(buf, hash);
            sink.FlushAll();

            Console.WriteLine($"wave_init: 1 position written to {outDir} (whiteToMove={wtm})");
        }

        // wave_expand: read positions from in_dir (one wave's bucket files), expand each by 1 ply,
        // spill children into out_dir bucket files. Output is un-sorted with duplicates; feed
        // to the Rust merger to produce the sorted unique next wave.
        //
        // Forms:
        //   wave_expand:<in>:<out>:<K>:<threads>
        //   wave_expand:<in>:<out>:<K>:<threads>:<log2_memtable>
        //   wave_expand:<in>:<out>:<K>:<threads>:<log2_memtable>:<mode>
        //
        //   log2_memtable: 0 = no memtable; N>0 allocates a 2^N-slot 128-bit lock-free
        //                  hashset (16 B/slot) for local duplicate suppression before spill.
        //                  Falls through to spill on TableFull — never drops records.
        //   mode: "full"  (default) — 26-byte position records, merge with `wave_merge`.
        //         "count" — 16-byte (h1,h2) records for ply-12-count style runs;
        //                   merge with the `merge` binary (no output wave written).
        //
        // Resume: each input bucket gets a `_progress/<name>.done` marker after THIS thread's
        // buffers are flushed (sink.FlushOwn, not FlushAll — FlushAll races against peers).
        // Reruns skip files whose markers exist. Set WAVE_EXPAND_NO_RESUME=1 to force.
        //
        // Crash semantics: if the process dies mid-input-file, that file's partial records
        // are already in the output buckets but its marker is missing. Reprocessing on resume
        // will re-emit those records, producing duplicates that the merge phase dedups out.
        // Worst-case wasted spill ≈ threadCount × one-input-bucket worth of expansion.
        //
        // Progress: a background thread rewrites `out_dir/progress.json` every 3 seconds.
        public static void RunWaveExpand(string[] commandParts)
        {
            if (commandParts.Length < 5 || commandParts.Length > 9 ||
                !int.TryParse(commandParts[3], out var numBuckets) ||
                !int.TryParse(commandParts[4], out var threadCount))
            {
                Console.WriteLine("Invalid command format: 'wave_expand:<in_dir>:<out_dir>:<buckets>:<threads>[:<log2_memtable>[:<mode>[:<bucket_lo>:<bucket_hi>]]]'.");
                Console.WriteLine("  mode: 'full' (default, 26-byte position records) or 'count' (16-byte hash records)");
                Console.WriteLine("  bucket_lo/bucket_hi: half-open output bucket range for multi-pass expand;");
                Console.WriteLine("                      defaults [0, K). Pass N times with disjoint ranges to chunk a ply.");
                return;
            }
            int log2Memtable = 0;
            string mode = "full";
            if (commandParts.Length >= 6 && !int.TryParse(commandParts[5], out log2Memtable))
            {
                Console.WriteLine($"Invalid log2_memtable: {commandParts[5]}");
                return;
            }
            if (commandParts.Length >= 7) mode = commandParts[6].Trim().ToLowerInvariant();
            if (mode != "full" && mode != "count")
            {
                Console.WriteLine($"Invalid mode: {mode} (expected 'full' or 'count')");
                return;
            }
            int bucketLo = 0;
            int bucketHi = numBuckets;
            if (commandParts.Length == 8)
            {
                Console.WriteLine("bucket_lo specified without bucket_hi");
                return;
            }
            if (commandParts.Length >= 9)
            {
                if (!int.TryParse(commandParts[7], out bucketLo) || !int.TryParse(commandParts[8], out bucketHi))
                {
                    Console.WriteLine($"Invalid bucket range: {commandParts[7]}:{commandParts[8]}");
                    return;
                }
                if (bucketLo < 0 || bucketHi > numBuckets || bucketLo >= bucketHi)
                {
                    Console.WriteLine($"bucket range [{bucketLo}, {bucketHi}) out of [0, {numBuckets})");
                    return;
                }
            }
            var inDir = commandParts[1];
            var outDir = commandParts[2];

            if (!Directory.Exists(inDir))
            {
                Console.WriteLine($"Input dir not found: {inDir}");
                return;
            }
            var allInputFiles = Directory.GetFiles(inDir, "bucket_*.bin").OrderBy(s => s).ToArray();
            if (allInputFiles.Length == 0)
            {
                Console.WriteLine($"No bucket_*.bin files in {inDir}");
                return;
            }

            Directory.CreateDirectory(outDir);
            var progressDir = Path.Combine(outDir, "_progress");
            Directory.CreateDirectory(progressDir);

            bool noResume = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAVE_EXPAND_NO_RESUME"));

            var inputFiles = new List<string>(allInputFiles.Length);
            int skipped = 0;
            long skippedRecords = 0;
            foreach (var f in allInputFiles)
            {
                var marker = Path.Combine(progressDir, Path.GetFileName(f) + ".done");
                if (!noResume && File.Exists(marker))
                {
                    skipped++;
                    skippedRecords += ReadInputRecordCount(f);
                }
                else
                {
                    inputFiles.Add(f);
                }
            }

            long totalInputRecords = skippedRecords;
            long pendingInputRecords = 0;
            foreach (var f in inputFiles)
            {
                var n = ReadInputRecordCount(f);
                totalInputRecords += n;
                pendingInputRecords += n;
            }
            Console.WriteLine($"wave_expand: {allInputFiles.Length} input bucket files, {totalInputRecords} positions");
            if (skipped > 0)
            {
                Console.WriteLine($"  resume: {skipped} already done ({skippedRecords} positions), {inputFiles.Count} pending ({pendingInputRecords} positions)");
            }

            // No perft TT for the BFS step — at depth=1 there's nothing to cache.
            PerftUnique.AllocateHashTable(0);
            PerftUnique.SetShard(1, 0);
            // Bucket-range filter — leaf emit will drop records outside [lo, hi).
            // Defaults [0, K) accept all → identical to pre-multi-pass behaviour.
            PerftUnique.SetBucketRange(numBuckets, bucketLo, bucketHi);

            // Memtable suppression. Allocating a 128-bit LockFreeHashSet filters local
            // duplicates before they reach disk. TableFull falls through to spill (the
            // merger dedups anything the memtable couldn't catch), so over-subscription
            // is safe — never drops records, only loses some suppression efficiency.
            if (log2Memtable > 0)
            {
                PerftUnique.ReallocateMemTable128(log2Memtable);
                long mtBytes = (1L << log2Memtable) * 16L;
                Console.WriteLine($"  memtable: 2^{log2Memtable} slots × 16 B = {mtBytes / (1L<<30)} GB");
            }
            else
            {
                PerftUnique.FreeMemTable128();
            }

            // Pick the sink based on mode. The closure delegates let the rest of the
            // method stay sink-agnostic.
            IDisposable sinkDisposable;
            Action flushAll, flushOwn;
            Func<long> getBytes, getRecords;
            Action clearGlobal;
            int recBytes;
            if (mode == "count")
            {
                var sink = new BucketSpillSink(outDir, numBuckets, bucketLo: bucketLo, bucketHi: bucketHi);
                PerftUnique.SpillSink = sink;
                sinkDisposable = sink;
                flushAll = sink.FlushAll; flushOwn = sink.FlushOwn;
                getBytes = () => sink.TotalBytesWritten; getRecords = () => sink.TotalRecordsWritten;
                clearGlobal = () => PerftUnique.SpillSink = null;
                recBytes = 16;
            }
            else
            {
                var sink = new BucketPositionSpillSink(outDir, numBuckets, bucketLo: bucketLo, bucketHi: bucketHi);
                PerftUnique.PositionSpillSink = sink;
                sinkDisposable = sink;
                flushAll = sink.FlushAll; flushOwn = sink.FlushOwn;
                getBytes = () => sink.TotalBytesWritten; getRecords = () => sink.TotalRecordsWritten;
                clearGlobal = () => PerftUnique.PositionSpillSink = null;
                recBytes = 26;
            }
            string rangeMsg = (bucketLo == 0 && bucketHi == numBuckets) ? "all buckets" : $"buckets [{bucketLo}, {bucketHi}) of {numBuckets}";
            Console.WriteLine($"  mode: {mode} ({recBytes}-byte spill records); {rangeMsg}");

            var sw = Stopwatch.StartNew();
            var fileQueue = new ConcurrentQueue<string>(inputFiles);
            long processed = skippedRecords;
            int filesDone = skipped;
            int filesInProgress = 0;

            // Background progress JSON writer.
            var progressJsonPath = Path.Combine(outDir, "progress.json");
            var startedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var progressStop = new ManualResetEventSlim(false);
            int totalFiles = allInputFiles.Length;
            var progressThread = new Thread(() =>
            {
                while (!progressStop.IsSet)
                {
                    WriteProgressJson(progressJsonPath, startedUnix, sw.Elapsed.TotalSeconds,
                        totalFiles, Volatile.Read(ref filesDone), Volatile.Read(ref filesInProgress),
                        totalInputRecords, Interlocked.Read(ref processed),
                        getBytes(), getRecords(), mode);
                    progressStop.Wait(TimeSpan.FromSeconds(3));
                }
            }) { IsBackground = true, Name = "wave_expand_progress" };
            progressThread.Start();

            var threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                var idx = i;
                threads[idx] = new Thread(() =>
                {
                    var buf = new byte[BucketPositionSpillSink.RecordSize];
                    while (fileQueue.TryDequeue(out var path))
                    {
                        Interlocked.Increment(ref filesInProgress);
                        // Auto-detect xz compression by magic bytes; when found, pipe through
                        // `xz -d -c` so the engine reads transparently regardless of whether
                        // the upstream merger wrote the bucket compressed.
                        var (inputStream, xzProc) = OpenWaveInput(path);
                        long records = ReadInputRecordCount(path);  // from .done marker; falls back to file size
                        long doneInFile = 0;
                        try
                        {
                            while (true)
                            {
                                int got = 0;
                                while (got < BucketPositionSpillSink.RecordSize)
                                {
                                    int r = inputStream.Read(buf, got, BucketPositionSpillSink.RecordSize - got);
                                    if (r == 0) break;
                                    got += r;
                                }
                                if (got < BucketPositionSpillSink.RecordSize) break;

                                var (board, wtm) = BoardStateSerialization.FromByteArray(buf);
                                board.MoveMask = 0;
                                PerftUnique.PerftRootUnique(ref board, 1, wtm);

                                doneInFile++;
                                if (idx == 0 && (doneInFile & 0xFFFFF) == 0)
                                {
                                    Console.WriteLine($"  thread 0 file={Path.GetFileName(path)} {doneInFile}/{records} spilled={((ulong)getBytes()).FormatBigNumber()}B");
                                }
                            }
                            Interlocked.Add(ref processed, records);
                        }
                        finally
                        {
                            inputStream.Dispose();
                            if (xzProc != null)
                            {
                                if (!xzProc.HasExited) xzProc.WaitForExit();
                                xzProc.Dispose();
                            }
                        }
                        // Boundary: flush THIS thread's buffers only. FlushAll here would
                        // race against peers' concurrent Record calls. Each input bucket
                        // is processed by exactly one thread, so flushing this thread's
                        // state is sufficient for the marker to be honest.
                        flushOwn();
                        var marker = Path.Combine(progressDir, Path.GetFileName(path) + ".done");
                        try { File.WriteAllText(marker, ""); } catch { /* best-effort */ }
                        Interlocked.Increment(ref filesDone);
                        Interlocked.Decrement(ref filesInProgress);
                    }
                });
                threads[idx].Start();
            }
            foreach (var t in threads) t.Join();

            flushAll();
            clearGlobal();
            var childRecs = getRecords();
            var totalSpillBytes = getBytes();
            sinkDisposable.Dispose();
            if (log2Memtable > 0) PerftUnique.FreeMemTable128();
            // Reset bucket filter so subsequent commands in the same engine session
            // (single REPL invocation) aren't accidentally filtered.
            PerftUnique.ClearBucketRange();

            progressStop.Set();
            progressThread.Join();
            WriteProgressJson(progressJsonPath, startedUnix, sw.Elapsed.TotalSeconds,
                totalFiles, Volatile.Read(ref filesDone), 0,
                totalInputRecords, Interlocked.Read(ref processed),
                totalSpillBytes, childRecs, mode);

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"input positions:  {processed} (of which {skippedRecords} resumed from prior run)");
            Console.WriteLine($"child records:    {childRecs} (avg {(processed > 0 ? (double)childRecs / processed : 0):0.00} children/position)");
            Console.WriteLine($"spill bytes:      {totalSpillBytes}");
            Console.WriteLine($"buckets:          {numBuckets} files in {outDir}");
            Console.WriteLine($"elapsed:          {ms}ms");
            Console.WriteLine("next: sort+dedup each bucket via the Rust merger, then this becomes the next wave's input");
            Console.WriteLine("-----------------");
        }

        // xz file magic header: FD 37 7A 58 5A 00
        private static readonly byte[] XzMagic = new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 };

        // Detect whether a wave bucket file is xz-compressed by peeking at its
        // first 6 bytes. If so, return a stream sourced from `xz -d -c` reading
        // the file; otherwise return a plain FileStream. Caller disposes the
        // returned stream and waits on the returned Process (if non-null).
        private static (System.IO.Stream stream, System.Diagnostics.Process? proc) OpenWaveInput(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            Span<byte> head = stackalloc byte[XzMagic.Length];
            int read = fs.Read(head);
            bool isXz = read == XzMagic.Length;
            if (isXz)
            {
                for (int i = 0; i < XzMagic.Length; i++)
                    if (head[i] != XzMagic[i]) { isXz = false; break; }
            }
            if (!isXz)
            {
                fs.Seek(0, SeekOrigin.Begin);
                return (fs, null);
            }
            fs.Dispose();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xz",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("-T");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(path);
            var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("failed to spawn xz");
            return (proc.StandardOutput.BaseStream, proc);
        }

        // Look up an input bucket's record count from the merger's .done marker
        // (preferred — works for compressed inputs too) and fall back to file
        // size / RecordSize when no marker is present.
        // Marker format: "<seen>\n<unique>\n"
        private static long ReadInputRecordCount(string bucketPath)
        {
            var markerPath = bucketPath + ".done";
            if (File.Exists(markerPath))
            {
                try
                {
                    var lines = File.ReadAllLines(markerPath);
                    if (lines.Length >= 2 && long.TryParse(lines[1].Trim(), out var unique))
                        return unique;
                }
                catch { /* fall through to size-based fallback */ }
            }
            var fi = new FileInfo(bucketPath);
            return fi.Length / BucketPositionSpillSink.RecordSize;
        }

        private static void WriteProgressJson(string path, long startedUnix, double elapsedSeconds,
            int filesTotal, int filesDone, int filesInProgress,
            long inputRecordsTotal, long inputRecordsDone,
            long spillBytes, long spillRecords, string mode)
        {
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int filesRemaining = Math.Max(0, filesTotal - filesDone);
            double recordsPerSec = elapsedSeconds > 0 ? inputRecordsDone / elapsedSeconds : 0;
            double bytesPerSec = elapsedSeconds > 0 ? spillBytes / elapsedSeconds : 0;
            long remainingRecords = Math.Max(0, inputRecordsTotal - inputRecordsDone);
            double etaSeconds = recordsPerSec > 0 ? remainingRecords / recordsPerSec : 0;
            double avgChildren = inputRecordsDone > 0 ? (double)spillRecords / inputRecordsDone : 0;
            var json = "{" +
                $"\"phase\":\"wave_expand\"," +
                $"\"mode\":\"{mode}\"," +
                $"\"started_unix\":{startedUnix}," +
                $"\"now_unix\":{nowUnix}," +
                $"\"elapsed_seconds\":{elapsedSeconds:0.0}," +
                $"\"input_files_total\":{filesTotal}," +
                $"\"input_files_completed\":{filesDone}," +
                $"\"input_files_in_progress\":{filesInProgress}," +
                $"\"input_files_remaining\":{filesRemaining}," +
                $"\"input_records_total\":{inputRecordsTotal}," +
                $"\"input_records_processed\":{inputRecordsDone}," +
                $"\"input_records_per_sec\":{recordsPerSec:0}," +
                $"\"spill_records\":{spillRecords}," +
                $"\"spill_bytes\":{spillBytes}," +
                $"\"spill_bytes_per_sec\":{bytesPerSec:0}," +
                $"\"avg_children_per_position\":{avgChildren:0.000}," +
                $"\"eta_seconds\":{etaSeconds:0}" +
                "}\n";
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch { /* best-effort, never crash the expand on a progress write */ }
        }

        public static void RunPerftUniqueDump(string[] commandParts)
        {
            if (commandParts.Length != 5 ||
                 !int.TryParse(commandParts[1], out var depth) ||
                 !int.TryParse(commandParts[2], out var mbHash))
            {
                Console.WriteLine("Invalid command format is 'unique_dump:<depth>:<mb_hash>:<path>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var dumpPath = commandParts[3];
            PerftUnique.AllocateHashTable(mbHash);
            PerftUnique.UniquePositions.Clear();

            using var sink = new KeyDumpSink(dumpPath);
            PerftUnique.UniquePositions.DumpSink = sink;

            var (board, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[4]));
            var sw = Stopwatch.StartNew();
            PerftUnique.PerftRootUnique(ref board, depth, whiteToMove);
            var ms = sw.ElapsedMilliseconds;

            PerftUnique.UniquePositions.DumpSink = null;

            Console.WriteLine("-----results-----");
            Console.WriteLine($"unique positions: {(ulong)PerftUnique.UniquePositions.Count}");
            Console.WriteLine($"dump bytes:       {sink.BytesWritten} ({sink.BytesWritten / 8} keys)");
            Console.WriteLine($"dump path:        {dumpPath}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine("-----------------");
        }

        public static void RunUniqueSpillMt(string[] commandParts)
        {
            if (commandParts.Length != 10 ||
                !int.TryParse(commandParts[1], out var depth) ||
                !int.TryParse(commandParts[2], out var mbPerftTt) ||
                !int.TryParse(commandParts[3], out var log2MemTable) ||
                !int.TryParse(commandParts[4], out var threadCount) ||
                !int.TryParse(commandParts[5], out var numBuckets) ||
                !int.TryParse(commandParts[6], out var shardCount) ||
                !int.TryParse(commandParts[7], out var shardId))
            {
                Console.WriteLine("Invalid command format is 'unique_spill_mt:<depth>:<mb_perft_tt>:<log2_memtable_slots>:<threads>:<buckets>:<shards>:<shard_id>:<out_dir>:<fen>'.");
                Console.WriteLine("  log2_memtable_slots: e.g. 31 = 2^31 slots = 16 GB lock-free hashset");
                Console.WriteLine("  shards/shard_id: pass 1/0 to disable sharding; otherwise (shards) passes filter by top bits of hash");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var outDir = commandParts[8];

            int launchDepth;
            if (depth > 7) launchDepth = 3;
            else if (depth > 4) launchDepth = 2;
            else launchDepth = 1;

            PerftUnique.ReallocateMemTable(log2MemTable);
            PerftUnique.SetShard(shardCount, shardId);
            // Shared perft TT: mbPerftTt is TOTAL size, allocated once.
            PerftUnique.AllocateHashTable(mbPerftTt);

            var sw = Stopwatch.StartNew();
            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[9]));
            var launchNodes = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);
            var queue = new ConcurrentQueue<string>(launchNodes.Select(n => n.fen));

            using var sink = new BucketSpillSink(outDir, numBuckets);
            PerftUnique.SpillSink = sink;

            long progressBytes = 0;
            var threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[index] = new Thread(() =>
                {
                    var count = 0;
                    while (queue.TryDequeue(out var fen))
                    {
                        var (board, wtm) = FenParser.Parse(fen);
                        PerftUnique.PerftRootUnique(ref board, depth - launchDepth, wtm);
                        count++;
                        if (index == 0 && count % 4 == 0)
                        {
                            var bytes = sink.TotalBytesWritten;
                            var delta = bytes - progressBytes;
                            progressBytes = bytes;
                            var memLoad = PerftUnique.UniquePositions.PercentFull * 100;
                            Console.WriteLine($"spilled: {((ulong)bytes).FormatBigNumber()}B (+{((ulong)delta).FormatBigNumber()}B since last), memtable {memLoad:0.00}%");
                        }
                    }
                });
                threads[index].Start();
            }
            foreach (var t in threads) t.Join();

            sink.FlushAll();
            PerftUnique.SpillSink = null;
            var totalBytes = sink.TotalBytesWritten;
            var memCount = (ulong)PerftUnique.UniquePositions.Count;
            var memLoadFinal = PerftUnique.UniquePositions.PercentFull * 100;
            PerftUnique.FreeHashTable();

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"shard:         {shardId} / {shardCount}");
            Console.WriteLine($"memtable hits: {memCount} unique (final load {memLoadFinal:0.00}%)");
            Console.WriteLine($"total records: {totalBytes / 16}");
            Console.WriteLine($"total bytes:   {totalBytes}");
            Console.WriteLine($"buckets:       {numBuckets} files in {outDir}");
            Console.WriteLine($"walk time:     {ms}ms");
            Console.WriteLine("merge with: ./target/release/merge " + outDir);
            Console.WriteLine("-----------------");
        }

        public static void RunUniqueMtDump(string[] commandParts)
        {
            if (commandParts.Length != 6 ||
                           !int.TryParse(commandParts[1], out var depth) ||
                           !int.TryParse(commandParts[2], out var mbHash) ||
                            !int.TryParse(commandParts[3], out var threadCount))
            {
                Console.WriteLine("Invalid command format is 'unique_mt_dump:<depth>:<mb_hash>:<threads>:<path>:<fen>'.");
                Console.WriteLine("type 'help' for more info.");
                return;
            }

            var dumpPath = commandParts[4];

            var launchDepth = 0;
            if (depth > 7)
            {
                launchDepth = 3;
            }
            else if (depth > 4)
            {
                launchDepth = 2;
            }
            else
            {
                launchDepth = 1;
            }

            var sw = Stopwatch.StartNew();
            var (initialBoard, whiteToMove) = FenParser.Parse(ResolveFen(commandParts[5]));

            var launchNodes = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);

            var queue = new ConcurrentQueue<string>(launchNodes.Select(n => n.fen));

            PerftUnique.UniquePositions.Clear();
            // Shared TT: mbHash interpreted as TOTAL size, allocated once.
            PerftUnique.AllocateHashTable(mbHash);
            using var sink = new KeyDumpSink(dumpPath);
            PerftUnique.UniquePositions.DumpSink = sink;

            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[index] = new Thread(() =>
                {
                    var count = 0;
                    while (queue.TryDequeue(out var fen))
                    {
                        var (board, wtm) = FenParser.Parse(fen);
                        PerftUnique.PerftRootUnique(ref board, depth - launchDepth, wtm);

                        count++;
                        if (index == 0 && count % 2 == 0)
                        {
                            Console.WriteLine($"unique positions: {((ulong)PerftUnique.UniquePositions.Count).FormatBigNumber()} {PerftUnique.UniquePositions.PercentFull * 100}%");
                        }
                    }
                });
                threads[index].Start();
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            PerftUnique.FreeHashTable();
            PerftUnique.UniquePositions.DumpSink = null;

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"unique positions: {(ulong)PerftUnique.UniquePositions.Count}");
            Console.WriteLine($"dump bytes:       {sink.BytesWritten} ({sink.BytesWritten / 8} keys)");
            Console.WriteLine($"dump path:        {dumpPath}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine("-----------------");
        }

        // Match a UCI move string ("e2e4", "e7e8q") against the legal-move
        // list for the current side, apply it, and flip side-to-move.
        // Returns false if no legal move matches the string.
        static bool TryApplyUciMove(ref Board board, ref bool whiteToMove, string uci)
        {
            Span<uint> legal = stackalloc uint[218];
            int n = MoveGenerator.GenerateMoves(ref legal, ref board, whiteToMove);
            for (int i = 0; i < n; i++)
            {
                if (legal[i].ToUciMoveName() == uci)
                {
                    if (whiteToMove) board.ApplyWhiteMove(legal[i]);
                    else             board.ApplyBlackMove(legal[i]);
                    whiteToMove = !whiteToMove;
                    return true;
                }
            }
            return false;
        }
    }
}
