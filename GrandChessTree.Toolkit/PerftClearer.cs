﻿using Npgsql;

namespace GrandChessTree.Toolkit
{
    public static class PerftClearer
    {
        public static async Task FullReset(int depth, int rootPositionId)
        {
            Console.WriteLine("Enter pgsql connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // Delete from perft_tasks
                const string deleteQuery = "DELETE FROM perft_tasks WHERE depth = @depth and root_position_id = @position;";

                await using (var deleteCmd = new NpgsqlCommand(deleteQuery, conn, transaction))
                {
                    deleteCmd.Parameters.AddWithValue("depth", depth);
                    deleteCmd.Parameters.AddWithValue("position", rootPositionId);
                    int deletedRows = await deleteCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"Deleted {deletedRows} rows from perft_tasks.");
                }

                // Update perft_items
                const string updateQuery = @"
                    UPDATE perft_items 
                    SET available_at = 0, pass_count = 0, confirmed = false 
                    WHERE depth = @depth and root_position_id = @position;";

                await using (var updateCmd = new NpgsqlCommand(updateQuery, conn, transaction))
                {
                    updateCmd.Parameters.AddWithValue("depth", depth);
                    updateCmd.Parameters.AddWithValue("position", rootPositionId);
                    int updatedRows = await updateCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"Updated {updatedRows} rows in perft_items.");
                }

                // Commit the transaction
                await transaction.CommitAsync();
                Console.WriteLine("Update and cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error during update and cleanup: {ex.Message}");
            }
        }

        public static async Task ReleaseIncompleteFullTasks(int depth, int rootPositionId)
        {
            Console.WriteLine("Enter PostgreSQL connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // Get the current time minus 1 minute
                var timeLimit = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
                const string updateQuery = @"
                    UPDATE perft_tasks_v3 
                    SET full_task_started_at = 0
                    WHERE depth = @depth and root_position_id = @position and full_task_finished_at = 0;";

                await using (var updateCmd = new NpgsqlCommand(updateQuery, conn, transaction))
                {
                    updateCmd.Parameters.AddWithValue("depth", depth);
                    updateCmd.Parameters.AddWithValue("timeLimit", timeLimit);
                    updateCmd.Parameters.AddWithValue("position", rootPositionId);

                    int updatedRows = await updateCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"Reset {updatedRows} tasks.");
                }

                // Commit the transaction
                await transaction.CommitAsync();
                Console.WriteLine("Update and cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error during update and cleanup: {ex.Message}");
            }
        }

        public static async Task ReleaseIncompleteFastTasks(int depth, int rootPositionId)
        {
            Console.WriteLine("Enter PostgreSQL connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var transaction = await conn.BeginTransactionAsync();

            try
            {
                // Get the current time minus 1 minute
                var timeLimit = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();

                // Reset the associated perft_items for the deleted tasks
                const string updateQuery = @"
                    UPDATE perft_tasks_v3 
                    SET fast_task_started_at = 0
                    WHERE depth = @depth and root_position_id = @position and fast_task_finished_at = 0;";

                await using (var updateCmd = new NpgsqlCommand(updateQuery, conn, transaction))
                {
                    updateCmd.Parameters.AddWithValue("depth", depth);
                    updateCmd.Parameters.AddWithValue("timeLimit", timeLimit);
                    updateCmd.Parameters.AddWithValue("position", rootPositionId);

                    int updatedRows = await updateCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"Reset {updatedRows} perft_items.");
                }

                // Commit the transaction
                await transaction.CommitAsync();
                Console.WriteLine("Update and cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error during update and cleanup: {ex.Message}");
            }
        }


    }
}
