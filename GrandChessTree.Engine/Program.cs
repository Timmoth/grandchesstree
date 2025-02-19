using System.Collections.Concurrent;
using System.Diagnostics;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Engine
{
    internal class Program
    {
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
                    Console.WriteLine("unique:<depth>:<mb_hash>:<fen>              - calculates the number of unique positions, single-threaded");
                    Console.WriteLine("exit                                        - closes the program");
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
                else if (command == "unique")
                {
                    RunPerftUnique(commandParts);
                }
            }           
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

            var launchDepth = 0;
            if (depth > 7)
            {
                launchDepth = 3;
            }else if(depth > 4)
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

            object lockObj = new();
            for (int i = 0; i < threads.Length; i++)
            {
                var index = i;

                threads[index] = new Thread(() =>
                {
                    PerftBulk.AllocateHashTable(mbHash);
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

    }
}
