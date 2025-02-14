﻿using System.Collections.Concurrent;
using System.Diagnostics;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Toolkit;

Console.WriteLine("----The Grand Chess Tree Toolkit----");
string? input;
while ((input = Console.ReadLine()) != "quit"){
    if (string.IsNullOrEmpty(input))
    {
        Console.WriteLine("Invalid command.");
        return;
    }

    var commandParts = input.Split(':');

    if (commandParts.Length == 0)
    {
        Console.WriteLine("Invalid command.");
        return;
    }

    var command = commandParts[0];
    if (command == "seed_startpos")
    {
        if (commandParts.Length != 2 || !int.TryParse(commandParts[1], out var depth))
        {
            Console.WriteLine("Invalid seed command format is 'seed_startpos:<depth>'.");
            return;
        }
        await PositionSeeder.SeedStartPos(depth);
    }
    else if (command == "seed_kiwipete")
    {
        if (commandParts.Length != 3 || !int.TryParse(commandParts[1], out var depth)
            || !int.TryParse(commandParts[2], out var itemDepth))
        {
            Console.WriteLine("Invalid seed command format is 'seed_kiwipete:<depth>:<item_depth>'.");
            return;
        }
        await PositionSeeder.SeedKiwipete(depth, itemDepth);
    }
    else if (command == "seed_sje")
    {
        if (commandParts.Length != 3 || !int.TryParse(commandParts[1], out var depth)
            || !int.TryParse(commandParts[2], out var itemDepth))
        {
            Console.WriteLine("Invalid seed command format is 'seed_sje:<depth>:<item_depth>'.");
            return;
        }
        await PositionSeeder.SeedSJE(depth, itemDepth);
    }
    else if (command == "seed_account")
    {
        await AccountSeeder.Seed();
    }
    else if (command == "seed_apikey")
    {
        await ApiKeySeeder.Seed();
    }
    else if (command == "perft_full_reset")
    {
        if (commandParts.Length != 3 || !int.TryParse(commandParts[1], out var depth)
            || !int.TryParse(commandParts[2], out var rootPositionId))
        {
            Console.WriteLine("Invalid command format is 'perft_full_reset:<depth>:<root_position_id>'.");
            return;
        }
        await PerftClearer.FullReset(depth, rootPositionId);
    }
    else if (command == "perft_release_incomplete")
    {
        if (commandParts.Length != 3 || !int.TryParse(commandParts[1], out var depth) 
            || !int.TryParse(commandParts[2], out var rootPositionId))
        {
            Console.WriteLine("Invalid command format is 'perft_release_incomplete:<depth>:<root_position_id>'.");
            return;
        }
        await PerftClearer.ReleaseIncompleteTasks(depth, rootPositionId);
    }
    else if (command == "perft")
    {
        if (commandParts.Length != 3 ||
            !int.TryParse(commandParts[1], out var depth))
        {
            Console.WriteLine("Invalid command format is 'perft:<depth>:<fen>'.");
            return;
        }


        var (board, whiteToMove) = FenParser.Parse(commandParts[2]);
        Summary summary = default;
        unsafe
        {
            Perft.HashTable = Perft.AllocateHashTable(256);
        }

        var sw = Stopwatch.StartNew();
        Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);
        var ms = sw.ElapsedMilliseconds;
        var s = (float)ms / 1000;
        var nps = summary.Nodes / s;
        Console.WriteLine($"nps:{(nps).FormatBigNumber()} {ms}ms");
        summary.Print();
        Console.WriteLine($"{board.Hash}:{board.ToFen(whiteToMove, 0, 1)}");
    }else    if (command == "perft_mt")
    {
        if (commandParts.Length != 3 ||
            !int.TryParse(commandParts[1], out var depth))
        {
            Console.WriteLine("Invalid command format is 'perft:<depth>:<fen>'.");
            return;
        }
        var (initialBoard, whiteToMove) = FenParser.Parse(commandParts[2]);

        var divideResults = LeafNodeGenerator.GenerateLeafNodesIncludeDuplicates(ref initialBoard, 1, whiteToMove);
        Summary summary = default;

        Thread[] threads = new Thread[divideResults.Count];

        object lockObj = new();

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < divideResults.Count; i++)
        {
            var index = i;
            var fen = divideResults[i].fen;
            threads[index] = new Thread(() =>
            {
                var (board, wtm) = FenParser.Parse(fen);
                Summary s = default;
                unsafe
                {
                    Perft.HashTable = Perft.AllocateHashTable(1024);
                }
                Perft.PerftRoot(ref board, ref s, depth - 1, wtm);

                lock (lockObj)
                {
                    summary.Accumulate(ref s);
                }
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
        var nps = summary.Nodes / s;
        Console.WriteLine($"nps:{(nps).FormatBigNumber()} {ms}ms");
        summary.Print();
    }
    else if (command == "perft_mt_bulk")
    {
        if (commandParts.Length != 4 ||
            !int.TryParse(commandParts[1], out var depth) ||
            !int.TryParse(commandParts[2], out var launchDepth))
        {
            Console.WriteLine("Invalid command format is 'perft:<depth>:<launch_depth>:<fen>'.");
            return;
        }
        var (initialBoard, whiteToMove) = FenParser.Parse(commandParts[3]);

        var divideResults = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, launchDepth, whiteToMove);
        var sw = Stopwatch.StartNew();
        ulong totalNodes = 0;

        Thread[] threads = new Thread[28];

        var queue = new ConcurrentQueue<(ulong hash, string fen, int occurrences)>(divideResults);

        using (StreamWriter writer = new StreamWriter("out.txt", append: true))
        {


            object lockObj = new();
            for (int i = 0; i < threads.Length; i++)
            {
                var index = i;

                threads[index] = new Thread(() =>
                {
                    unsafe
                    {
                        PerftBulk.HashTable = PerftBulk.AllocateHashTable(2046);
                    }

                    var count = 0;
                    while (queue.TryDequeue(out var item))
                    {
                        var (board, wtm) = FenParser.Parse(item.fen);

                        var nodes = PerftBulk.PerftRootBulk(ref board, depth - launchDepth, wtm);

                        lock (lockObj)
                        {
                            totalNodes += nodes * (ulong)item.occurrences;
                            writer.WriteLine($"{item.hash},{nodes},{item.occurrences}");
                        }

                        count++;
                        if (index == 0 && count % 2 == 0)
                        {
                            var ms = sw.ElapsedMilliseconds;
                            var s = (float)ms / 1000;
                            var nps = totalNodes / s;
                            Console.WriteLine($"nps:{(nps).FormatBigNumber()} {ms}ms");
                            Console.WriteLine($"nodes:{totalNodes}");
                            writer.Flush();
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

        }

        var ms = sw.ElapsedMilliseconds;
        var s = (float)ms / 1000;
        var nps = totalNodes / s;
        Console.WriteLine($"nps:{(nps).FormatBigNumber()} {ms}ms");
        Console.WriteLine($"nodes:{totalNodes}");
    }

    else if (command == "perft_test")
    {
        if (commandParts.Length != 4 ||
            !int.TryParse(commandParts[1], out var depth) ||
            !int.TryParse(commandParts[2], out var hashMb) ||
            !int.TryParse(commandParts[3], out var iterations))
        {
            Console.WriteLine("Invalid command format is 'perft_test:<depth>:<hash_mb>:<iterations>'.");
            return;
        }


        var (board, whiteToMove) = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        Summary summary = default;
        unsafe
        {
            Perft.ClearTable(Perft.HashTable);
            Perft.FreeHashTable();
            Perft.HashTable = Perft.AllocateHashTable(hashMb);
        }

        Console.WriteLine("Starting warmup");
        for (var i = 0; i < 10; i++)
        {
            summary = default;
            unsafe
            {
                Perft.ClearTable(Perft.HashTable);
            }

            Perft.PerftRoot(ref board, ref summary, 5, whiteToMove);
        }

        Console.WriteLine("Starting iterations");
        ulong totalNodes = 0;
        ulong totalMs = 0;
        float minNps = float.MaxValue;
        float maxNps = float.MinValue;

        for (var i = 0; i < iterations; i++)
        {
            summary = default;
            unsafe
            {
                Perft.ClearTable(Perft.HashTable);
            }

            var sw = Stopwatch.StartNew();
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);
            var ms = sw.ElapsedMilliseconds;
            var s = (float)ms / 1000;
            var nps = summary.Nodes / s;
            Console.WriteLine($"iteration: {i} nps:{(nps).FormatBigNumber()} {ms}ms");
            totalMs += (ulong)ms;
            totalNodes += summary.Nodes;
            minNps = Math.Min(minNps, nps);
            maxNps = Math.Max(maxNps, nps);
        }

        var totalS = (float)totalMs / 1000;
        Console.WriteLine($"completed {iterations} nps: min:{(minNps).FormatBigNumber()} max:{(maxNps).FormatBigNumber()} av:{(totalNodes / totalS).FormatBigNumber()} {totalS}seconds");
    }
    else if(command == "seed_fen")
    {
        await FenUpdater.Seed();
    }
    else if (command == "dump_db")
    {
        if (commandParts.Length != 3 || !int.TryParse(commandParts[1], out var depth)
            || !int.TryParse(commandParts[2], out var rootPositionId))
        {
            Console.WriteLine("Invalid command format is 'dump_db:<depth>:<root_position_id>'.");
            return;
        }
        await DbDumper.Dump(rootPositionId, depth);
    }   
    else if (command == "dump_db_summary")
    {
        if (commandParts.Length != 5 || 
            !int.TryParse(commandParts[1], out var depth)
            || !int.TryParse(commandParts[2], out var rootPositionId)
            || !int.TryParse(commandParts[3], out var launchDepth)
            )
        {
            Console.WriteLine("Invalid command format is 'dump_db_summary:<depth>:<root_position_id>:<launch_depth>:<fen>'.");
            return;
        }
        await DbDumper.ConstructSummary(rootPositionId, depth, launchDepth, commandParts[4]);
    }
    else if (command == "quick_perft_summary")
    {
        if (commandParts.Length != 4 ||
            !int.TryParse(commandParts[1], out var depth)
            || !int.TryParse(commandParts[2], out var rootPositionId)
            )
        {
            Console.WriteLine("Invalid command format is 'quick_perft_summary:<depth>:<root_position_id>:<fen>'.");
            return;
        }
        await DbDumper.QuickSearch(rootPositionId, depth, commandParts[3]);
    }
    else
    {
        Console.WriteLine("unrecognized command.");
    }

}
