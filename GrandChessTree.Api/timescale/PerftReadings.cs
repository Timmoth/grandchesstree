using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Shared.Api;
using Npgsql;

namespace GrandChessTree.Api.timescale
{
    public enum PerftTaskType
    {
        Fast, Full
    }
    public class PerftNodesStatsResponse
    {
        [JsonPropertyName("nps")]
        public float Nps { get; set; }

        [JsonPropertyName("total_nodes")]
        public ulong TotalNodes { get; set; }
    }


    public class PerformanceChartEntry
    {
        [Column("timestamp")]
        [JsonPropertyName("timestamp")]
        public long timestamp { get; set; }
        [Column("nps")]
        [JsonPropertyName("nps")]
        public float nps { get; set; }
    }


    public class PerftReadings
    {
        private readonly string _connectionString;
        private readonly TimeProvider _timeProvider;

        public PerftReadings(IConfiguration configuration, TimeProvider timeProvider)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new Exception("Failed to get default connection string");
            _timeProvider = timeProvider;
        }
        public class ProgressStatsModel
        {
            [Column("total_nodes")]
            [JsonPropertyName("total_nodes")]
            public ulong total_nodes { get; set; }
        }

        public class RealTimeStatsModel
        {
            [Column("nps")]
            [JsonPropertyName("nps")]
            public float nps { get; set; }
        }

        public async Task<List<PerformanceChartEntry>> GetPerformanceChart(PerftTaskType taskType, int positionId, int depth, CancellationToken cancellationToken)
        {
            // Get the current Unix time in seconds.
            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            // Calculate the last complete 15-minute interval.
            var end = ((now / 900) * 900);

            // Set start as 12 hours (43200 seconds) before the end of the last full interval.
            var start = end - 43200;

            string sql = taskType == PerftTaskType.Fast ? @"
        SELECT 
            time_bucket('15 minutes', time) AS timestamp,
            SUM(nodes * occurrences)::numeric / 900.0 AS nps
        FROM public.perft_readings
        WHERE time BETWEEN TO_TIMESTAMP(@start) AND TO_TIMESTAMP(@end)
            AND task_type = 1
            AND root_position_id = @positionId 
            AND depth = @depth
        GROUP BY timestamp
        ORDER BY timestamp;" :
         @"
        SELECT 
            time_bucket('15 minutes', time) AS timestamp,
            SUM(nodes * occurrences)::numeric / 900.0 AS nps
        FROM public.perft_readings
        WHERE time BETWEEN TO_TIMESTAMP(@start) AND TO_TIMESTAMP(@end)
            AND task_type = 0
            AND root_position_id = @positionId 
            AND depth = @depth
        GROUP BY timestamp
        ORDER BY timestamp;";

            var performanceEntries = new List<PerformanceChartEntry>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@start", start);
                cmd.Parameters.AddWithValue("@end", end);
                cmd.Parameters.AddWithValue("@positionId", positionId);
                cmd.Parameters.AddWithValue("@depth", depth);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        performanceEntries.Add(new PerformanceChartEntry
                        {
                            timestamp = new DateTimeOffset(reader.GetDateTime(0)).ToUnixTimeSeconds(),
                            nps = reader.GetFloat(1)
                        });
                    }
                }
            }

            return performanceEntries;
        }



        public async Task<PerftStatsResponse> GetTaskPerformance(PerftTaskType full, int positionId, int depth, CancellationToken cancellationToken)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Query to count total tasks
            const string totalTaskCountQuery = @"
        SELECT COUNT(*) FROM public.perft_tasks_v3 
        WHERE root_position_id = @positionId AND depth = @depth;";

            int totalTaskCount = 0;
            await using (var cmd = new NpgsqlCommand(totalTaskCountQuery, conn))
            {
                cmd.Parameters.AddWithValue("@positionId", positionId);
                cmd.Parameters.AddWithValue("@depth", depth);

                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                totalTaskCount = result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }

            // Query for real-time statistics
            string realTimeStatsQuery = full == PerftTaskType.Fast ? @"
        SELECT
            COALESCE(COUNT(*), 0) / 60.0 AS tpm,
            COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings
        WHERE root_position_id = @positionId AND depth = @depth
            AND time >= NOW() - INTERVAL '1 hour' AND task_type = 1;" :
            @"
        SELECT
            COALESCE(COUNT(*), 0) / 60.0 AS tpm,
            COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings
        WHERE root_position_id = @positionId AND depth = @depth
            AND time >= NOW() - INTERVAL '1 hour' AND task_type = 0;";

            float tpm = 0;
            float nps = 0;
            await using (var cmd = new NpgsqlCommand(realTimeStatsQuery, conn))
            {
                cmd.Parameters.AddWithValue("@positionId", positionId);
                cmd.Parameters.AddWithValue("@depth", depth);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        tpm = reader.GetFloat(0);
                        nps = reader.GetFloat(1);
                    }
                }
            }

            // Query for progress statistics
            string progressStatsQuery = full == PerftTaskType.Fast ? @"
        SELECT
            COUNT(*) AS completed_tasks,
            COALESCE(SUM(nodes * occurrences), 0) AS total_nodes
        FROM public.perft_readings
        WHERE root_position_id = @positionId AND depth = @depth AND task_type = 1;":
        @"
        SELECT
            COUNT(*) AS completed_tasks,
            COALESCE(SUM(nodes * occurrences), 0) AS total_nodes
        FROM public.perft_readings
        WHERE root_position_id = @positionId AND depth = @depth AND task_type = 0;";

            ulong completedTasks = 0;
            ulong totalNodes = 0;
            await using (var cmd = new NpgsqlCommand(progressStatsQuery, conn))
            {
                cmd.Parameters.AddWithValue("@positionId", positionId);
                cmd.Parameters.AddWithValue("@depth", depth);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        completedTasks = (ulong)reader.GetInt64(0);
                        totalNodes = (ulong)reader.GetInt64(1);
                    }
                }
            }

            return new PerftStatsResponse()
            {
                Nps = nps,
                Tpm = tpm,
                CompletedTasks = completedTasks,
                TotalNodes = totalNodes,
                PercentCompletedTasks = totalTaskCount > 0 ? (float)completedTasks / totalTaskCount * 100 : 0,
                TotalTasks = totalTaskCount
            };
        }


        public async Task<List<PerformanceChartEntry>> GetTaskPerformance(PerftTaskType full, CancellationToken cancellationToken)
        {
            string sql = full == PerftTaskType.Fast ? @"
        SELECT 
            time_bucket('15 minutes', time) AS timestamp,
            SUM(nodes * occurrences)::numeric / 900.0 AS nps
        FROM public.perft_readings
        WHERE time >= NOW() - INTERVAL '24 hour' AND task_type = 1
        GROUP BY timestamp
        ORDER BY timestamp;" :
        @"
        SELECT 
            time_bucket('15 minutes', time) AS timestamp,
            SUM(nodes * occurrences)::numeric / 900.0 AS nps
        FROM public.perft_readings
        WHERE time >= NOW() - INTERVAL '24 hour' AND task_type = 0
        GROUP BY timestamp
        ORDER BY timestamp;";

            var performanceEntries = new List<PerformanceChartEntry>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using (var cmd = new NpgsqlCommand(sql, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    performanceEntries.Add(new PerformanceChartEntry
                    {
                        timestamp = new DateTimeOffset(reader.GetDateTime(0)).ToUnixTimeSeconds(),
                        nps = reader.GetFloat(1)
                    });
                }
            }

            return performanceEntries;
        }

        public async Task<List<PerformanceChartEntry>> GetAccountTaskPerformance(PerftTaskType full, long accountId, CancellationToken cancellationToken)
        {
            string sql = full == PerftTaskType.Fast ? @"
        SELECT 
            time_bucket('15 minutes', time) AS timestamp,
            SUM(nodes * occurrences)::numeric / 900.0 AS nps
        FROM public.perft_readings
        WHERE time >= NOW() - INTERVAL '24 hour' AND task_type = 1
          AND account_id = @accountId
        GROUP BY timestamp
        ORDER BY timestamp;" : @"
        SELECT 
            time_bucket('15 minutes', time) AS timestamp,
            SUM(nodes * occurrences)::numeric / 900.0 AS nps
        FROM public.perft_readings
        WHERE time >= NOW() - INTERVAL '24 hour' AND task_type = 0
          AND account_id = @accountId
        GROUP BY timestamp
        ORDER BY timestamp;"
        ;

            var performanceEntries = new List<PerformanceChartEntry>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@accountId", accountId);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        performanceEntries.Add(new PerformanceChartEntry
                        {
                            timestamp = new DateTimeOffset(reader.GetDateTime(0)).ToUnixTimeSeconds(),
                            nps = reader.GetFloat(1)
                        });
                    }
                }
            }

            return performanceEntries;
        }

        public async Task<PerftNodesStatsResponse> GetTaskStats(PerftTaskType taskType, CancellationToken cancellationToken)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Query for RealTimeStatsModel
            string realTimeStatsQuery = taskType == PerftTaskType.Fast ? @"
        SELECT COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings 
        WHERE time >= NOW() - INTERVAL '1 hour' AND task_type = 1;" : @"
        SELECT COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings 
        WHERE time >= NOW() - INTERVAL '1 hour' AND task_type = 0;";

            float nps = 0;
            await using (var cmd = new NpgsqlCommand(realTimeStatsQuery, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    nps = reader.GetFloat(0);
                }
            }

            // Query for ProgressStatsModel
            string progressStatsQuery = taskType == PerftTaskType.Fast ? @"
        SELECT COALESCE(SUM(nodes * occurrences), 0) AS total_nodes
        FROM public.perft_readings
        WHERE task_type = 1;" :
         @"
        SELECT COALESCE(SUM(nodes * occurrences), 0) AS total_nodes
        FROM public.perft_readings
        WHERE task_type = 0;";

            ulong totalNodes = 0;
            await using (var cmd = new NpgsqlCommand(progressStatsQuery, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    totalNodes = (ulong)reader.GetInt64(0);
                }
            }

            return new PerftNodesStatsResponse()
            {
                Nps = nps,
                TotalNodes = totalNodes
            };
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
