using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Moves;

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
                    Console.WriteLine("divide:<depth>:<mb_hash>:<fen>              - calculates the perft nodes for each move, single-threaded");
                    Console.WriteLine("divide_mt:<depth>:<mb_hash>:<threads>:<fen> - calculates the perft nodes for each move, multi-threaded");
                    Console.WriteLine("unique:<depth>:<mb_hash>:<fen>              - calculates the number of unique positions, single-threaded");
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
                else if (command == "decode_fen")
                {
                    var (brd, wtm) = BoardStateSerialization.Deserialize(commandParts[1]);
                    Console.WriteLine(brd.ToFen(wtm, 0, 1));
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

        public static void RunDivideNodes(string[] commandParts)
        {
            if (commandParts.Length != 4 ||
                !int.TryParse(commandParts[1], out var depth) ||
                !int.TryParse(commandParts[2], out var mbHash))
            {
                Console.WriteLine("Invalid command format is 'divide:<depth>:<mb_hash>:<fen>'.");
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

            Thread[] threads = new Thread[threadCount];

            object lockObj = new();

            for (int i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[index] = new Thread(() =>
                {
                    var count = 0;
                    PerftUnique.AllocateHashTable(mbHash);
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

                    PerftUnique.FreeHashTable();
                });
                threads[index].Start();
            }

            // Wait for all threads to complete
            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            var ms = sw.ElapsedMilliseconds;
            Console.WriteLine("-----results-----");
            Console.WriteLine($"unique positions: {(ulong)PerftUnique.UniquePositions.Count}");
            Console.WriteLine($"time: {ms}ms");
            Console.WriteLine("-----------------");
        }

    }
}
