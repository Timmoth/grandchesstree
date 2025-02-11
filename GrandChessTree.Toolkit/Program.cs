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
    if (command == "seed_positions")
    {
        if (commandParts.Length != 2 || !int.TryParse(commandParts[1], out var depth))
        {
            Console.WriteLine("Invalid seed command format is 'seed_positions:<depth>'.");
            return;
        }
        await PositionSeeder.Seed(depth);
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
        if (commandParts.Length != 2 || !int.TryParse(commandParts[1], out var depth))
        {
            Console.WriteLine("Invalid command format is 'perft_full_reset:<depth>'.");
            return;
        }
        await PerftClearer.FullReset(depth);
    }
    else if (command == "perft_release_incomplete")
    {
        if (commandParts.Length != 2 || !int.TryParse(commandParts[1], out var depth))
        {
            Console.WriteLine("Invalid command format is 'perft_release_incomplete:<depth>'.");
            return;
        }
        await PerftClearer.ReleaseIncompleteTasks(depth);
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
    else
    {
        Console.WriteLine("unrecognized command.");
    }

}
