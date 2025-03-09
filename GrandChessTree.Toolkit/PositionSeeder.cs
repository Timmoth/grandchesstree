using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using Npgsql;

namespace GrandChessTree.Toolkit
{
    public static class PositionSeeder
    {
        public static async Task SeedPosition(string fen, int depth, int launchDepth, int rootPositionId)
        {
            const int BatchSize = 100000; // Adjust batch size as needed
            const int MaxRetries = 3;     // Maximum number of retries per batch

            Console.WriteLine("Generating positions");

            var (initialBoard, whiteToMove) = FenParser.Parse(fen);

            //UniqueLeafNodeGeneratorCompressed.PerftRootCompressedUniqueLeafNodes(ref initialBoard, launchDepth, whiteToMove);
            //var boards = UniqueLeafNodeGeneratorCompressed.boards;
            //var total = boards.Values.Sum(b => (float)b.occurrences);
            //var unique = boards.Count;

            //Console.WriteLine($"Total positions: {total}");
            //Console.WriteLine($"Unique positions: {unique}");

            //Console.WriteLine("Enter pgsql connection string...");
            //var connectionString = Console.ReadLine();

            //if (string.IsNullOrEmpty(connectionString))
            //{
            //    connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            //}

            //await using var conn = new NpgsqlConnection(connectionString);
            //await conn.OpenAsync();

            //Console.WriteLine($"Starting bulk insert of {boards.Count} rows in batches...");

            //try
            //{
            //    var orderedBoards = boards.Values.OrderBy(b => b.order).ToList();
            //    int totalBatches = (int)Math.Ceiling((double)orderedBoards.Count / BatchSize);

            //    for (int i = 0; i < totalBatches; i++)
            //    {
            //        bool batchInserted = false;
            //        int attempt = 0;
            //        while (!batchInserted && attempt < MaxRetries)
            //        {
            //            attempt++;
            //            try
            //            {
            //                // Check and re-open connection if necessary
            //                if (conn.State != System.Data.ConnectionState.Open)
            //                {
            //                    Console.WriteLine("Connection closed, re-opening...");
            //                    await conn.OpenAsync();
            //                }

            //                Console.WriteLine($"Processing batch {i + 1}/{totalBatches}, attempt {attempt}...");

            //                var batch = orderedBoards.Skip(i * BatchSize).Take(BatchSize);
            //                await using var writer = await conn.BeginTextImportAsync("COPY perft_tasks_v3 (board, depth, occurrences, root_position_id, launch_depth) FROM STDIN (FORMAT csv)");

            //                foreach (var entry in batch)
            //                {
            //                    await writer.WriteLineAsync($"{entry.board},{depth},{entry.occurrences},{rootPositionId},{depth - launchDepth}");
            //                }

            //                batchInserted = true;
            //                Console.WriteLine($"Batch {i + 1}/{totalBatches} inserted successfully on attempt {attempt}.");
            //            }
            //            catch (Exception ex)
            //            {
            //                Console.WriteLine($"Error during batch {i + 1}/{totalBatches} on attempt {attempt}: {ex.Message}");
            //                // Optionally, add a delay before retrying:
            //                await Task.Delay(2000);
            //            }
            //        }
            //        if (!batchInserted)
            //        {
            //            throw new Exception($"Failed to insert batch {i + 1}/{totalBatches} after {MaxRetries} attempts.");
            //        }
            //    }

            //    Console.WriteLine("Bulk insert completed successfully.");
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"Error during bulk insert: {ex.Message}");
            //}
        }

    }
}