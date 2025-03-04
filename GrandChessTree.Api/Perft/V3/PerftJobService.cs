using System.Collections.Concurrent;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.timescale;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.V3
{
    public class TaskUpdate
    {
        public required PerftTaskType TaskType { get; set; }
        public required int Depth { get; set; }
        public required int RootPositionId { get; set; }
        public required bool IsVerified { get; set; }
        public required ulong ComputedNodes { get; set; }
    }

    public class PerftJobService
    {
        private readonly ILogger<PerftJobService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        public PerftJobService(IServiceScopeFactory scopeFactory, ILogger<PerftJobService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }
        private readonly static ConcurrentQueue<TaskUpdate> TaskUpdates = new();

        public void AddUpdates(IEnumerable<TaskUpdate> updates)
        {
            foreach (var update in updates)
            {
                TaskUpdates.Enqueue(update);
            }
        }

        public async Task Process(CancellationToken cancellationToken)
        {
            var updates = new List<TaskUpdate>();
            while (TaskUpdates.TryDequeue(out var task))
            {
                updates.Add(task);
            }

            if (updates.Count == 0)
            {
                return;
            }

            var failedTasks = new List<TaskUpdate>();

            try
            {
                using var scope = _scopeFactory.CreateScope();

                var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>() ?? throw new Exception("failed to init dbcontext");

                foreach (var group in updates.GroupBy(u => (u.RootPositionId, u.Depth)))
                {
                    var job = await _dbContext.PerftJobs.FirstOrDefaultAsync(c => c.RootPositionId == group.Key.RootPositionId && c.Depth == group.Key.Depth);
                    if (job == null)
                    {
                        continue;
                    }

                    job.Add(group);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                foreach (var update in updates)
                {
                    TaskUpdates.Enqueue(update);
                }
                _logger.LogError(ex, "Couldn't process perft jobs batch");
            }
        }
    }

    public class PerftJobBackgroundService : BackgroundService
    {
        private readonly ILogger<PerftJobBackgroundService> _logger;
        private readonly PerftJobService _jobService;
        public PerftJobBackgroundService(ILogger<PerftJobBackgroundService> logger, PerftJobService jobService)
        {
            _logger = logger;
            _jobService = jobService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _jobService.Process(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing consumer message batch.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
