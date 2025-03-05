using System.Collections.Concurrent;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using System.Text.Json.Serialization;
using GrandChessTree.Api.Database;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.V3
{
    public class PerftCompletedFastTask
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
    }

    public class PerftFastTaskBackgroundService : BackgroundService
    {
        private readonly ILogger<PerftFastTaskBackgroundService> _logger;
        private readonly PerftFastTaskService _fastTaskService;

        public PerftFastTaskBackgroundService(ILogger<PerftFastTaskBackgroundService> logger, PerftFastTaskService fastTaskService)
        {
            _logger = logger;
            _fastTaskService = fastTaskService;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _fastTaskService.Process(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing consumer message batch.");
                }

                if (_fastTaskService.HasLessThenFullBatch)
                {
                    await Task.Delay(200, stoppingToken);
                }
            }
        }
    }
    public class PerftFastTaskService
    {
        private readonly ILogger<PerftFastTaskService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly PerftReadings _perftReadings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly PerftJobService _perftJobService;
        private readonly PerftContributionService _perftContributionService;

        public PerftFastTaskService(ILogger<PerftFastTaskService> logger, IServiceScopeFactory scopeFactory, TimeProvider timeProvider, PerftReadings perftReadings, PerftJobService perftJobService, PerftContributionService perftContributionService)
        {
            _logger = logger;
            _timeProvider = timeProvider;
            _perftReadings = perftReadings;
            _scopeFactory = scopeFactory;
            _perftJobService = perftJobService;
            _perftContributionService = perftContributionService;
        }

        private readonly static ConcurrentQueue<PerftCompletedFastTask> CompletedTasks = new();

        public bool HasLessThenFullBatch => CompletedTasks.Count < maxBatchSize;

        public void Enqueue(PerftFastTaskResultBatch batch, long accountId)
        {
            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            for(int i = 0; i < batch.Results.Length; i += 2)
            {
                CompletedTasks.Enqueue(new PerftCompletedFastTask()
                {
                    CompletedAt = currentTimestamp,
                    TaskId = (int)batch.Results[i],
                    WorkerId = batch.WorkerId,
                    AccountId = accountId,
                    Nodes = batch.Results[i+1],
                });
            }
        }

        public void Enqueue(PerftFastTaskResultBatchOld batch, long accountId)
        {
            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            foreach (var result in batch.Results)
            {
                CompletedTasks.Enqueue(new PerftCompletedFastTask()
                {
                    CompletedAt = currentTimestamp,
                    TaskId = result.TaskId,
                    WorkerId = batch.WorkerId,
                    AccountId = accountId,
                    Nodes = result.Nodes,
                });
            }
        }

        public const int maxBatchSize = 100;
        public async Task Process(CancellationToken cancellationToken)
        {
            var taskBatch = new List<PerftCompletedFastTask>();
            while(taskBatch.Count <= maxBatchSize && CompletedTasks.TryDequeue(out var task))
            {
                if(task.Attempts >= 4)
                {
                    // Failed.
                    continue;
                }

                taskBatch.Add(task);
            }

            if(taskBatch.Count == 0)
            {
                return;
            }


            var failedTasks = new List<PerftCompletedFastTask>();

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
                var taskUpdates = new List<TaskUpdate>();
                var contributions = new List<PerftContributionUpdate>();

                // Process each result.
                foreach (var result in taskBatch)
                {
                    if (!tasks.TryGetValue(result.TaskId, out var task) || task.FastTaskAccountId != result.AccountId)
                    {
                        // This might happen if the task was updated concurrently.
                        failedTasks.Add(result);
                        continue;
                    }

                    taskUpdates.Add(task.FinishFastTask(result));
                    readings.Add(task.ToFastTaskReading());

                    contributions.Add(new PerftContributionUpdate()
                    {
                        TaskType = PerftTaskType.Fast,
                        Depth = task.Depth,
                        RootPositionId = task.RootPositionId,
                        ComputedNodes = task.FastTaskNodes * (ulong)task.Occurrences,
                        AccountId = result.AccountId,
                    });
                }

                // Attempt to save changes.
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Commit the transaction.
                await transaction.CommitAsync(cancellationToken);
                await _perftReadings.InsertReadings(readings, cancellationToken);

                _perftJobService.AddUpdates(taskUpdates);
                _perftContributionService.AddUpdates(contributions);

                _logger.LogInformation("Processed: {} tasks, {} in queue", readings.Count, CompletedTasks.Count);
            }
            catch (Exception ex) 
            {
                failedTasks.Clear();
                failedTasks.AddRange(taskBatch);
                _logger.LogError(ex, "Couldn't process fast batch of {} tasks, {} in queue", taskBatch.Count, CompletedTasks.Count);
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
