using System.Collections.Generic;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using Npgsql;

namespace GrandChessTree.Toolkit
{
    public static class PositionSeeder
    {
        public static async Task SeedStartPos(int depth)
        {
            Console.WriteLine("Enter pgsql connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }
            // Open a connection to PostgreSQL
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            Console.WriteLine($"Starting bulk insert of {PositionsD4.Dict.Count} rows...");

            // Use COPY for bulk insert
            await using var writer = await conn.BeginTextImportAsync("COPY perft_items (hash, fen, depth, available_at, pass_count, confirmed, occurrences, root_position_id, launch_depth) FROM STDIN (FORMAT csv)");

            try
            {
                // Iterate over the dictionary and write each row to the COPY stream
                foreach (var (hash, fen) in PositionsD4.Dict)
                {
                    OccurrencesD4.Dict.TryGetValue(hash, out var occurrences);
                    await writer.WriteLineAsync($"{hash},{fen},{depth},0,0,false,{occurrences},0,{depth-4}");
                }

                Console.WriteLine("Bulk insert completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during bulk insert: {ex.Message}");
            }
        }

        public static async Task SeedKiwipete(int depth, int itemDepth)
        {

            Console.WriteLine("Generating positions");

            var (initialBoard, whiteToMove) = FenParser.Parse("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - ");

            var boards = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, itemDepth, whiteToMove);

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
            await using var writer = await conn.BeginTextImportAsync("COPY perft_items (hash, fen, depth, available_at, pass_count, confirmed, occurrences, root_position_id, launch_depth) FROM STDIN (FORMAT csv)");

            try
            {
                // Iterate over the dictionary and write each row to the COPY stream
                foreach (var (hash, fen, occurrences) in boards)
                {
                    await writer.WriteLineAsync($"{hash},{fen},{depth},0,0,false,{occurrences},1,{depth - itemDepth}");
                }

                Console.WriteLine("Bulk insert completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during bulk insert: {ex.Message}");
            }
        }

        public static async Task SeedSJE(int depth, int itemDepth)
        {

            Console.WriteLine("Generating positions");

            var (initialBoard, whiteToMove) = FenParser.Parse("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10");

            var boards = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, itemDepth, whiteToMove);

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
            await using var writer = await conn.BeginTextImportAsync("COPY perft_items (hash, fen, depth, available_at, pass_count, confirmed, occurrences, root_position_id, launch_depth) FROM STDIN (FORMAT csv)");

            try
            {
                // Iterate over the dictionary and write each row to the COPY stream
                foreach (var (hash, fen, occurrences) in boards)
                {
                    await writer.WriteLineAsync($"{hash},{fen},{depth},0,0,false,{occurrences},2,{depth - itemDepth}");
                }

                Console.WriteLine("Bulk insert completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during bulk insert: {ex.Message}");
            }
        }
    }


    public static class FenUpdater
    {
        public static async Task Seed()
        {
            Console.WriteLine("Enter pgsql connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            Console.WriteLine($"Starting batch update of {PositionsD4.Dict.Count} rows...");

            await using var transaction = await conn.BeginTransactionAsync(); // Start a transaction

            try
            {
                // Step 1: Create a temporary table inside the transaction
                var createTempTableQuery = @"
            CREATE TEMP TABLE temp_update_perft_items (
                hash NUMERIC(20) PRIMARY KEY,
                fen TEXT
            ) ON COMMIT DROP;"; // Table drops when transaction ends

                await using (var cmd = new NpgsqlCommand(createTempTableQuery, conn, transaction))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                // Step 2: Bulk insert into the temporary table
                await using (var writer = await conn.BeginTextImportAsync(
                    "COPY temp_update_perft_items (hash, fen) FROM STDIN (FORMAT csv)"))
                {
                    foreach (var (hash, fen) in PositionsD4.Dict)
                    {
                        await writer.WriteLineAsync($"{hash},{fen}");
                    }
                } // <== Ensure writer is disposed before running UPDATE!

                Console.WriteLine("Temporary table populated with updates.");

                // Step 3: Perform a bulk update using the temporary table
                var updateQuery = @"
            UPDATE perft_items 
            SET fen = temp.fen
            FROM temp_update_perft_items temp
            WHERE perft_items.hash = temp.hash;";

                await using (var updateCmd = new NpgsqlCommand(updateQuery, conn, transaction))
                {
                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"Successfully updated {rowsAffected} rows.");
                }

                await transaction.CommitAsync(); // Commit the transaction
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(); // Rollback in case of error
                Console.WriteLine($"Error during bulk update: {ex.Message}");
            }
        }


    }
}
