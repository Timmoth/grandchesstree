using GrandChessTree.Shared.Helpers;
using Npgsql;

namespace GrandChessTree.Toolkit
{
    public static class PositionSeeder
    {
        public static async Task SeedPosition(string fen, int depth, int launchDepth, int rootPositionId)
        {
            Console.WriteLine("Generating positions");

            var (initialBoard, whiteToMove) = FenParser.Parse(fen);

            var boards = LeafNodeGenerator.GenerateCompressedLeafNodes(ref initialBoard, launchDepth, whiteToMove);

            var total = boards.Sum(b => b.occurrences);
            var unique = boards.Count();
            Console.WriteLine($"total positions: {total}");
            Console.WriteLine($"uniques {unique}");
          

            Console.WriteLine("Enter pgsql connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }
            // Open a connection to PostgreSQL
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            Console.WriteLine($"Starting bulk insert of {boards.Count} rows...");

            // Use COPY for bulk insert
            await using var writer = await conn.BeginTextImportAsync("COPY perft_tasks_v3 (board, depth, occurrences, root_position_id, launch_depth) FROM STDIN (FORMAT csv)");

            try
            {
                // Iterate over the dictionary and write each row to the COPY stream
                foreach (var (board, occurrences) in boards)
                {
                    await writer.WriteLineAsync($"{board},{depth},{occurrences},{rootPositionId},{depth - launchDepth}");
                }

                Console.WriteLine("Bulk insert completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during bulk insert: {ex.Message}");
            }
        }
    }
}
