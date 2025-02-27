using System.Collections.Concurrent;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using System.Text.Json.Serialization;
using GrandChessTree.Api.Database;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.V3
{
    public class PerftCompletedFullTask
    {
        [JsonPropertyName("completed_at")]
        public long CompletedAt = 0;

        [JsonPropertyName("attempts")]
        public int Attempts = 0;

        [JsonPropertyName("worker_id")]
        public int WorkerId { get; set; } = 0;

        [JsonPropertyName("worker_id")]
        public long AccountId { get; set; } = 0;

        [JsonPropertyName("task_id")]
        public required long TaskId { get; set; }

        [JsonPropertyName("nodes")]
        public required ulong Nodes { get; set; }
        [JsonPropertyName("captures")]
        public required ulong Captures { get; set; }
        [JsonPropertyName("enpassants")]
        public required ulong Enpassants { get; set; }
        [JsonPropertyName("castles")]
        public required ulong Castles { get; set; }
        [JsonPropertyName("promotions")]
        public required ulong Promotions { get; set; }
        [JsonPropertyName("direct_checks")]
        public required ulong DirectChecks { get; set; }
        [JsonPropertyName("single_discovered_checks")]
        public required ulong SingleDiscoveredChecks { get; set; }
        [JsonPropertyName("direct_discovered_checks")]
        public required ulong DirectDiscoveredChecks { get; set; }
        [JsonPropertyName("double_discovered_checks")]
        public required ulong DoubleDiscoveredChecks { get; set; }
        [JsonPropertyName("direct_mates")]
        public required ulong DirectMates { get; set; }
        [JsonPropertyName("single_discovered_mates")]
        public required ulong SingleDiscoveredMates { get; set; }
        [JsonPropertyName("direct_discovered_mates")]
        public required ulong DirectDiscoverdMates { get; set; }
        [JsonPropertyName("double_discovered_mates")]
        public required ulong DoubleDiscoverdMates { get; set; }
    }

    public class PerftFullTaskBackgroundService : BackgroundService
    {
        private readonly ILogger<PerftFullTaskBackgroundService> _logger;
        private readonly PerftFullTaskService _fullTaskService;
        public PerftFullTaskBackgroundService(ILogger<PerftFullTaskBackgroundService> logger, PerftFullTaskService fullTaskService)
        {
            _logger = logger;
            _fullTaskService = fullTaskService;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _fullTaskService.Process(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing consumer message batch.");
                }

                if (_fullTaskService.HasLessThenFullBatch)
                {
                    await Task.Delay(200, stoppingToken);
                }
            }
        }
    }

    public class PerftFullTaskService
    {
        private readonly ILogger<PerftFullTaskService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly PerftReadings _perftReadings;
        private readonly IServiceScopeFactory _scopeFactory;

        public PerftFullTaskService(ILogger<PerftFullTaskService> logger, TimeProvider timeProvider, PerftReadings perftReadings, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _timeProvider = timeProvider;
            _perftReadings = perftReadings;
            _scopeFactory = scopeFactory;
        }

        private readonly static ConcurrentQueue<PerftCompletedFullTask> CompletedTasks = new();

        public void Enqueue(PerftFullTaskResultBatch batch, long accountId)
        {
            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            foreach (var result in batch.Results)
            {
                CompletedTasks.Enqueue(new PerftCompletedFullTask()
                {
                    CompletedAt = currentTimestamp,
                    TaskId = result.TaskId,
                    WorkerId = batch.WorkerId,
                    AccountId = accountId,
                    Nodes = result.Nodes,
                    Captures = result.Captures,
                    Enpassants = result.Enpassants,
                    Castles = result.Castles,
                    Promotions = result.Promotions,
                    DirectChecks = result.DirectChecks,
                    SingleDiscoveredChecks = result.SingleDiscoveredChecks,
                   DirectDiscoveredChecks = result.DirectDiscoveredChecks,
                   DoubleDiscoveredChecks = result.DoubleDiscoveredChecks,
                   DirectMates = result.DirectMates,
                   SingleDiscoveredMates = result.SingleDiscoveredMates,
                   DirectDiscoverdMates = result.DirectDiscoverdMates,
                   DoubleDiscoverdMates = result.DoubleDiscoverdMates,
                });

            }
        }
        public bool HasLessThenFullBatch => CompletedTasks.Count < maxBatchSize;

        public const int maxBatchSize = 100;
        public async Task Process(CancellationToken cancellationToken)
        {
            var taskBatch = new List<PerftCompletedFullTask>();
            while(taskBatch.Count <= maxBatchSize && CompletedTasks.TryDequeue(out var task))
            {
                if(task.Attempts >= 4)
                {
                    // Failed.
                    continue;
                }

                taskBatch.Add(task);
            }

            if (taskBatch.Count == 0)
            {
                return;
            }

            var failedTasks = new List<PerftCompletedFullTask>();

            try
            {
                using var scope = _scopeFactory.CreateScope();

                var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>() ?? throw new Exception("failed to init dbcontext");


                var taskIds = taskBatch.Select(r => r.TaskId).ToList();
                var readings = new List<object[]>();
                // Start a new transaction for each attempt.
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                // Re-fetch the tasks within the transaction.
                var tasks = await _dbContext.PerftTasksV3
                    .Where(t => taskIds.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, cancellationToken);

                if (tasks.Count == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new Exception("Couldn't find submitted tasks");
                }

                // Process each result.
                foreach (var result in taskBatch)
                {
                    if (!tasks.TryGetValue(result.TaskId, out var task) || task.FullTaskAccountId != result.AccountId)
                    {
                        // This might happen if the task was updated concurrently.
                        failedTasks.Add(result);
                        continue;
                    }

                    task.FinishFullTask(result);
                    readings.Add(task.ToFullTaskReading());
                }

                // Attempt to save changes.
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Commit the transaction.
                await transaction.CommitAsync(cancellationToken);

                await _perftReadings.InsertReadings(readings, cancellationToken);

                _logger.LogInformation("Processed: {} tasks {} in queue", readings.Count, CompletedTasks.Count);
            }
            catch (Exception ex)
            {
                failedTasks.Clear();
                failedTasks.AddRange(taskBatch);
                _logger.LogError(ex, "Couldn't process full task batch of {} items. {} in queue", taskBatch.Count, CompletedTasks.Count);
            }
            finally
            {
                foreach (var task in failedTasks)
                {
                    task.Attempts += 1;
                    CompletedTasks.Enqueue(task);
                }
            }
        }

    }
}
