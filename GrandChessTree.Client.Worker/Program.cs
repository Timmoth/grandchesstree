using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;



try
{

    if (args.Length == 0)
    {
        //Console.WriteLine("No arguments passed.");
        //return;
    }

    RuntimeHelpers.RunClassConstructor(typeof(AttackTables).TypeHandle);
    RuntimeHelpers.RunClassConstructor(typeof(Perft).TypeHandle);

    //// test
    ///
    Console.WriteLine("loading position");
    var (board, whiteToMove) = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
    Console.WriteLine("allocating hash table");

    Summary summary = default;
    unsafe
    {
        Perft.AllocateHashTable();
    }

    var sw = Stopwatch.StartNew();

    //var fens = LeafNodeGenerator.GenerateLeafNodes(ref board, 6, whiteToMove);

    //var ordered = fens.OrderByDescending(f => f.occurrences);

    //var top1000avg = ordered.Take(1000).Average(c => c.occurrences);
    //var top10000avg = ordered.Take(10000).Average(c => c.occurrences);
    //var top100000avg = ordered.Take(100000).Average(c => c.occurrences);

    //Console.WriteLine("av1k: " + top1000avg);
    //Console.WriteLine("av10k: " + top10000avg);
    //Console.WriteLine("av100k: " + top100000avg);
    Console.WriteLine("starting search");

    Perft.PerftRoot(ref board, ref summary, 5, whiteToMove);
    Console.WriteLine("search complete");

    var ms = sw.ElapsedMilliseconds;
    var s = (float)ms / 1000;

    Console.WriteLine($"fen:{board.ToFen(whiteToMove, 0, 1)}");
    Console.WriteLine($"nps:{(summary.Nodes / s).FormatBigNumber()}");
    Console.WriteLine($"time:{s}");
    summary.Print();

    // test
    return;
    //
    // ConcurrentQueue<string> commandQueue = new();
    // using ManualResetEventSlim commandAvailable = new(false);
    // var hasQuit = false;
    //
    //
    // _ = Task.Run(ReadCommands);
    //
    // Console.WriteLine("ready");
    // while (!hasQuit)
    // {
    //     commandAvailable.Wait(); // Wait until a command is available
    //     commandAvailable.Reset(); // Reset the event for the next wait
    //
    //     while (commandQueue.TryDequeue(out var command))
    //     {
    //         if (command.StartsWith("reset"))
    //         {
    //             Perft.ClearTable();
    //             Console.WriteLine("ready");
    //         }
    //         else if (command.StartsWith("begin"))
    //         {
    //             Console.WriteLine("processing");
    //             var commandParts = command.Split(":");
    //             if (int.TryParse(commandParts[1], out var depth))
    //             {
    //                 var (board, whiteToMove) = FenParser.Parse(commandParts[2]);
    //                 var sw = Stopwatch.StartNew();
    //                 var summary = Perft.PerftRoot(ref board, depth, whiteToMove);
    //                 var ms = sw.ElapsedMilliseconds;
    //                 var s = (float)ms / 1000;
    //                 Console.WriteLine($"fen:{board.ToFen(whiteToMove, 0, 1)}");
    //                 Console.WriteLine($"hash:{Zobrist.CalculateZobristKey(ref board, whiteToMove)}");
    //                 Console.WriteLine($"nps:{(summary.Nodes / s)}");
    //                 summary.Print();
    //                 Console.WriteLine("done");
    //             }
    //         }
    //     }
    // }
    //
    // return;
    //
    // void ReadCommands()
    // {
    //     while (true)
    //     {
    //         var command = Console.ReadLine();
    //         if (string.IsNullOrEmpty(command))
    //         {
    //             continue; // Skip empty commands
    //         }
    //
    //         command = command.Trim();
    //         if (command.Contains("quit", StringComparison.OrdinalIgnoreCase))
    //         {
    //             hasQuit = true;
    //             Environment.Exit(0);
    //             break;
    //         }
    //
    //         commandQueue.Enqueue(command);
    //         commandAvailable.Set(); // Signal that a command is available
    //     }
    // }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex}");
}
