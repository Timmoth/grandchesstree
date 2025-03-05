using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Npgsql;

namespace GrandChessTree.Api.timescale
{
    public enum PerftTaskType
    {
        Full = 0,
        Fast = 1
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
        
        [Column("tpm")]
        [JsonPropertyName("tpm")]
        public float tpm { get; set; }
    }

    public class WorkerStats
    {
        [JsonPropertyName("worker_id")]
        public float WorkerId { get; set; }

        [JsonPropertyName("task_type")]
        public float TaskType { get; set; }

        [JsonPropertyName("nps")]
        public float Nps { get; set; }

        [JsonPropertyName("tpm")]
        public float Tpm { get; set; }
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

        public async Task<(float nps, float tpm)> GetLeaderboard(PerftTaskType taskType, int accountId, CancellationToken cancellationToken)
        {
            int taskTypeInt = taskType == PerftTaskType.Full ? 0 : 1;
            string sql = @$" SELECT 
                SUM(nodes * occurrences) / 3600 AS nps,
                COUNT(*) / 60 AS tpm
            FROM public.perft_readings
            WHERE time >= NOW() - INTERVAL '1 hour' and task_type = {taskTypeInt}
                    AND account_id = @account_id 
        ";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var response = new Dictionary<long, (float nps, float tpm)>();

            float tpm = 0;
            float nps = 0;
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@account_id", accountId);
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        tpm = reader.IsDBNull(0) ? 0 : reader.GetFloat(0);
                        nps = reader.IsDBNull(1) ? 0 : reader.GetFloat(1);
                    }
                }
            }

            return (tpm, nps);
        }

        public async Task<Dictionary<long, (float nps, float tpm)>> GetLeaderboard(PerftTaskType taskType, int positionId, int depth, CancellationToken cancellationToken)
        {
            int taskTypeInt = taskType == PerftTaskType.Full ? 0 : 1;
            string sql = @$" SELECT 
        account_id,
        SUM(nodes * occurrences) / 3600 AS nps,
        COUNT(*) / 60 AS tpm
    FROM public.perft_readings
    WHERE time >= NOW() - INTERVAL '1 hour' and task_type = {taskTypeInt}
            AND root_position_id = @positionId 
            AND depth = @depth
    GROUP BY account_id
";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var response = new Dictionary<long, (float nps, float tpm)>();

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@positionId", positionId);
                cmd.Parameters.AddWithValue("@depth", depth);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        response[reader.GetInt64(0)] = (reader.GetFloat(1), reader.GetFloat(2));
                    }
                }
            }


            return response;
        }

        public async Task<List<WorkerStats>> GetWorkerStats(int accountId, CancellationToken cancellationToken)
        {
            string sql = @$"SELECT 
	                            worker_id,
	                            task_type,
	                            SUM(nodes * occurrences) / 3600 AS nps,
	                            COUNT(*) / 60 AS tpm
                            FROM public.perft_readings
                            WHERE time >= NOW() - INTERVAL '1 hour'
                            and account_id = @account_id
                            GROUP BY worker_id, task_type
                            order by worker_id, task_type desc
                            ";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var response = new List<WorkerStats>();

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@account_id", accountId);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        response.Add(new WorkerStats()
                        {
                            WorkerId = reader.GetInt16(0),
                            TaskType = reader.GetInt16(1),
                            Nps = reader.GetFloat(2),
                            Tpm = reader.GetInt64(3)
                        });
                                               
                    }
                }
            }


            return response;
        }


        public async Task<Dictionary<long, (float nps, float tpm)>> GetLeaderboard(PerftTaskType taskType, CancellationToken cancellationToken)
        {
            int taskTypeInt = taskType == PerftTaskType.Full ? 0 : 1;
            string sql = @$"SELECT 
                                account_id,
                                SUM(nodes * occurrences) / 3600 AS nps,
                                COUNT(*) / 60 AS tpm
                            FROM public.perft_readings
                            WHERE time >= NOW() - INTERVAL '1 hour' and task_type = {taskTypeInt}
                            GROUP BY account_id
                ";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var response = new Dictionary<long, (float nps, float tpm)>();

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        response[reader.GetInt64(0)] = (reader.GetFloat(1), reader.GetFloat(2));
                    }
                }
            }


            return response;
        }

        public async Task<List<PerformanceChartEntry>> GetPerformanceChart(PerftTaskType taskType, int positionId, int depth, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow();
            var start = now.AddHours(-12); // 12 hours ago

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            string sql = @"
        SELECT 
            time_bucket('30 minutes', time) AS timestamp,
            COUNT(*) AS tpm,
            SUM(nodes * occurrences)::numeric / 1800.0 AS nps
        FROM public.perft_readings
        WHERE time >= @start
            AND task_type = @taskType::integer
            AND root_position_id = @positionId 
            AND depth = @depth
        GROUP BY timestamp
        ORDER BY timestamp;";

            var performanceEntries = new List<PerformanceChartEntry>();

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@start", start);
                cmd.Parameters.AddWithValue("@taskType", (int)taskType);
                cmd.Parameters.AddWithValue("@positionId", positionId);
                cmd.Parameters.AddWithValue("@depth", depth);

                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        performanceEntries.Add(new PerformanceChartEntry
                        {
                            timestamp = new DateTimeOffset(reader.GetDateTime(0)).ToUnixTimeSeconds(),
                            tpm = reader.GetFloat(1),
                            nps = reader.GetFloat(2)
                        });
                    }
                }
            }

            return performanceEntries;
        }

        public async Task<(float tpm, float nps)> GetTaskPerformance(PerftTaskType taskType, int positionId, int depth, CancellationToken cancellationToken)
        {
            // Precompute the start time so the query uses an index-friendly constant.
            var start = _timeProvider.GetUtcNow().AddHours(-1);

            // Assume that the enum values match the column values (e.g. Fast==1, else 0).
            int taskTypeValue = (int)taskType;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            string sql = @"
        SELECT
            COALESCE(COUNT(*), 0) / 60.0 AS tpm,
            COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings
        WHERE root_position_id = @positionId
          AND depth = @depth
          AND time >= @start
          AND task_type = @taskType;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@positionId", positionId);
            cmd.Parameters.AddWithValue("@depth", depth);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@taskType", taskTypeValue);

            float tpm = 0;
            float nps = 0;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                tpm = reader.GetFloat(0);
                nps = reader.GetFloat(1);
            }

            return (tpm, nps);
        }


        public async Task<List<PerformanceChartEntry>> GetTaskPerformance(PerftTaskType taskType, CancellationToken cancellationToken)
        {
            var start = _timeProvider.GetUtcNow().AddDays(-1); // 24 hours ago
            int taskTypeValue = (int)taskType; // Convert enum to integer

            string sql = @"
        SELECT 
            time_bucket('30 minutes', time) AS timestamp,
            COUNT(*) AS tpm,
            SUM(nodes * occurrences)::numeric / 1800.0 AS nps
        FROM public.perft_readings
        WHERE time >= @start AND task_type = @taskType
        GROUP BY timestamp
        ORDER BY timestamp;";

            var performanceEntries = new List<PerformanceChartEntry>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@taskType", taskTypeValue);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                performanceEntries.Add(new PerformanceChartEntry
                {
                    timestamp = new DateTimeOffset(reader.GetDateTime(0)).ToUnixTimeSeconds(),
                    tpm = reader.GetFloat(1),
                    nps = reader.GetFloat(2)
                });
            }

            return performanceEntries;
        }

        public async Task<List<PerformanceChartEntry>> GetAccountTaskPerformance(PerftTaskType taskType, long accountId, CancellationToken cancellationToken)
        {
            var start = _timeProvider.GetUtcNow().AddDays(-1); // 24 hours ago
            int taskTypeValue = (int)taskType; // Convert enum to integer

            string sql = @"
        SELECT 
            time_bucket('30 minutes', time) AS timestamp,
            COUNT(*) AS tpm,
            SUM(nodes * occurrences)::numeric / 1800.0 AS nps
        FROM public.perft_readings
        WHERE time >= @start 
          AND task_type = @taskType
          AND account_id = @accountId
        GROUP BY timestamp
        ORDER BY timestamp;";

            var performanceEntries = new List<PerformanceChartEntry>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@taskType", taskTypeValue);
            cmd.Parameters.AddWithValue("@accountId", accountId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                performanceEntries.Add(new PerformanceChartEntry
                {
                    timestamp = new DateTimeOffset(reader.GetDateTime(0)).ToUnixTimeSeconds(),
                    tpm = reader.GetFloat(1),
                    nps = reader.GetFloat(2)
                });
            }

            return performanceEntries;
        }

        public async Task<PerftNodesStatsResponse> GetTaskStats(PerftTaskType taskType, CancellationToken cancellationToken)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            // Precompute the start time so PostgreSQL can use indexes effectively.
            var start = _timeProvider.GetUtcNow().AddHours(-1);
            int taskTypeValue = (int)taskType;

            // Combine the two queries into one command using multiple result sets.
            string sql = @"
        SELECT COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings 
        WHERE time >= @start AND task_type = @taskType;

        SELECT COALESCE(SUM(nodes * occurrences), 0) AS total_nodes
        FROM public.perft_readings
        WHERE task_type = @taskType;
    ";

            float nps = 0;
            ulong totalNodes = 0;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@start", start);
            cmd.Parameters.AddWithValue("@taskType", taskTypeValue);

            // Execute both queries in one go.
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            // First result set: nps calculation.
            if (await reader.ReadAsync(cancellationToken))
            {
                nps = reader.GetFloat(0);
            }

            // Move to the second result set: total_nodes calculation.
            if (await reader.NextResultAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    totalNodes = (ulong)reader.GetInt64(0);
                }
            }

            return new PerftNodesStatsResponse
            {
                Nps = nps,
                TotalNodes = totalNodes
            };
        }



        public async Task InsertReadings(
            IEnumerable<object[]> readings,
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
