using System.Collections.Concurrent;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.timescale;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.V3
{

    public class PerftContributionUpdate
    {
        public required PerftTaskType TaskType { get; set; }
        public required int Depth { get; set; }
        public required int RootPositionId { get; set; }
        public required ulong ComputedNodes { get; set; }
        public required long AccountId { get; set; }
    }

    public class PerftContributionService
    {
        private readonly ILogger<PerftContributionService> _logger;
        private readonly static ConcurrentQueue<PerftContributionUpdate> Updates = new();
        private readonly IServiceScopeFactory _scopeFactory;

        public PerftContributionService(IServiceScopeFactory scopeFactory, ILogger<PerftContributionService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void AddUpdates(IEnumerable<PerftContributionUpdate> updates)
        {
            foreach (var update in updates)
            {
                Updates.Enqueue(update);
            }
        }

        public async Task Process(CancellationToken cancellationToken)
        {
            var updates = new List<PerftContributionUpdate>();
            while (Updates.TryDequeue(out var task))
            {
                updates.Add(task);
            }

            if (updates.Count == 0)
            {
                return;
            }

            var failedTasks = new List<PerftCompletedFastTask>();

            try
            {
                using var scope = _scopeFactory.CreateScope();

                var _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>() ?? throw new Exception("failed to init dbcontext");

                foreach(var group in updates.GroupBy(u => (u.AccountId, u.RootPositionId, u.Depth)))
                {
                    var contribution = await _dbContext.PerftContributions.FirstOrDefaultAsync(c => c.AccountId == group.Key.AccountId && c.RootPositionId == group.Key.RootPositionId && c.Depth == group.Key.Depth);
                    if (contribution == null)
                    {
                        contribution = new PerftContribution()
                        {
                            AccountId = group.Key.AccountId,
                            Depth = group.Key.Depth,
                            RootPositionId = group.Key.RootPositionId,
                            CompletedFastTasks = 0,
                            CompletedFullTasks = 0,
                            FastTaskNodes = 0,
                            FullTaskNodes = 0,
                        };
                        _dbContext.PerftContributions.Add(contribution);
                    }

                    contribution.Add(group);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                foreach(var update in updates)
                {
                    Updates.Enqueue(update);
                }
                _logger.LogError(ex, "Couldn't process perft contribution batch");
            }
        }
    }

    public class PerftContributionBackgroundService : BackgroundService
    {
        private readonly ILogger<PerftContributionBackgroundService> _logger;
        private readonly PerftContributionService _contributionService;
        public PerftContributionBackgroundService(ILogger<PerftContributionBackgroundService> logger, PerftContributionService contributionService)
        {
            _logger = logger;
            _contributionService = contributionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _contributionService.Process(stoppingToken);
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
