using Npgsql;

namespace GrandChessTree.Api.timescale
{
    public class PerftReadings
    {
        private readonly string _connectionString;


        public PerftReadings(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new Exception("Failed to get default connection string");
        }

        public async Task InsertReadings(
            List<object[]> readings,
               CancellationToken cancellationToken = default)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
            var tempTable = $"temp_perft_readings_{IdGenerator.Generate()}";

            try
            {
                // Create temporary table
                var createCommandText = @$"
                CREATE TEMP TABLE {tempTable} (
                    time             TIMESTAMPTZ       NOT NULL,
                    account_id       BIGINT            NOT NULL,
                    worker_id        SMALLINT          NOT NULL,
                    nodes            BIGINT            NOT NULL,
                    occurrences      SMALLINT          NOT NULL,
                    duration         BIGINT            NOT NULL,
                    depth            SMALLINT          NOT NULL,
                    root_position_id SMALLINT          NOT NULL,
                    task_type        SMALLINT          NOT NULL
                );";

                await using var createCmd = new NpgsqlCommand(createCommandText, conn);
                await createCmd.ExecuteNonQueryAsync(cancellationToken);

                // Perform binary COPY import
                var copyCommandText = @$"COPY {tempTable} FROM STDIN (FORMAT BINARY);";

                await using (var writer = await conn.BeginBinaryImportAsync(copyCommandText, cancellationToken))
                {
                    foreach (var reading in readings)
                    {
                        await writer.WriteRowAsync(cancellationToken, reading);
                    }

                    await writer.CompleteAsync(cancellationToken);
                }

                // Insert from temp table into the actual hypertable
                var insertCommandText = @$"
                INSERT INTO perft_readings (
                    time, account_id, worker_id, nodes, occurrences, 
                    duration, depth, root_position_id, task_type
                )
                SELECT time, account_id, worker_id, nodes, occurrences, 
                    duration, depth, root_position_id, task_type 
                FROM {tempTable}
                ON CONFLICT DO NOTHING;";  // Adjust conflict handling as needed

                await using var insertCmd = new NpgsqlCommand(insertCommandText, conn);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
            finally
            {
                // Drop temp table
                await using var dropCmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {tempTable};", conn);
                await dropCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
