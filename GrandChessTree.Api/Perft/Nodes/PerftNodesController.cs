using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.Database;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.PerftNodes
{
    [ApiController]
    [Route("api/v2/perft/nodes")]
    public class PerftNodesController : ControllerBase
    {
        private readonly ILogger<PerftNodesController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly ApiKeyAuthenticator _apiKeyAuthenticator;
        public PerftNodesController(ILogger<PerftNodesController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, ApiKeyAuthenticator apiKeyAuthenticator)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
            _apiKeyAuthenticator = apiKeyAuthenticator;
        }

        [ApiKeyAuthorize]
        [HttpPost("tasks")]
        public async Task<IActionResult> CreateNewTaskBatch(CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyAuthenticator.GetApiKey(HttpContext, cancellationToken);

            if (apiKey == null)
            {
                return Unauthorized();
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            var searchItems = await _dbContext.PerftNodesTask
               .FromSqlRaw(@"
                    SELECT * FROM public.perft_nodes_tasks 
                    WHERE available_at <= {0} AND finished_at = 0
                    ORDER BY depth ASC
                    LIMIT 20 FOR UPDATE SKIP LOCKED", currentTimestamp)
               .ToListAsync(cancellationToken);

            if (!searchItems.Any())
            {
                return NotFound();
            }

            // Update search items to prevent immediate reprocessing
            foreach (var item in searchItems)
            {
                item.AvailableAt = currentTimestamp + 3600; // Becomes available again in 1 hour
                item.StartedAt = currentTimestamp;
                item.AccountId = apiKey.AccountId;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Commit transaction to finalize changes
            await transaction.CommitAsync(cancellationToken);

            // Prepare response
            var response = searchItems.Select(task => new PerftNodesTaskResponse
            {
                TaskId = task.Id,
                Fen = task.Fen,
                LaunchDepth = task.LaunchDepth,
                Depth = task.Depth,
            }).ToArray();

            return Ok(response);
        }

        [ApiKeyAuthorize]
        [HttpPost("results")]
        public async Task<IActionResult> SubmitResults(
            [FromBody] PerftNodesTaskResultBatch request,
           CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyAuthenticator.GetApiKey(HttpContext, cancellationToken);

            if (apiKey == null)
            {
                return Unauthorized();
            }
            var accountId = apiKey.AccountId;

            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            // Extract IDs to fetch all tasks in one query
            var taskIds = request.Results.Select(r => r.PerftNodesTaskId).ToList();

            // Fetch all tasks with related SearchItem in a single batch query
            var searchTasks = await _dbContext.PerftNodesTask
                .Where(t => t.AccountId == accountId && taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

            if (searchTasks.Count == 0)
            {
                return NotFound();
            }

            // Process each request and update the corresponding task
            foreach (var result in request.Results)
            {
                if (!searchTasks.TryGetValue(result.PerftNodesTaskId, out var searchTask))
                {
                    continue; // Skip if task not found (shouldn't happen)
                }

                // Update the search item (parent)
                searchTask.AvailableAt = currentTimestamp;
                searchTask.WorkerId = request.WorkerId;

                var finishedAt = currentTimestamp == searchTask.StartedAt ? currentTimestamp + 1 : currentTimestamp;

                // Update search task properties
                var duration = (ulong)(finishedAt - searchTask.StartedAt);
                if (duration > 0)
                {
                    searchTask.Nps = result.Nodes * (ulong)searchTask.Occurrences / duration;
                }
                else
                {
                    searchTask.Nps = result.Nodes * (ulong)searchTask.Occurrences;
                }

                searchTask.FinishedAt = finishedAt;
                searchTask.Nodes = result.Nodes;
            }

            // Bulk save changes in one transaction
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok();
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
        public class PerftStatsResponse
        {
            [JsonPropertyName("nps")]
            public float Nps { get; set; }

            [JsonPropertyName("total_nodes")]
            public ulong TotalNodes { get; set; }
        }

        public class PerftLeaderboardResponse
        {
            [JsonPropertyName("account_name")]
            public string AccountName { get; set; } = "Unknown";

            [JsonPropertyName("total_nodes")]
            public long TotalNodes { get; set; }

            [JsonPropertyName("compute_time_seconds")]
            public long TotalTimeSeconds { get; set; }

            [JsonPropertyName("nps")]
            public float NodesPerSecond { get; set; }
        }


        [HttpGet("stats")]
        [ResponseCache(Duration = 30)]
        [OutputCache(Duration = 30)]
        public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
        {
            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            var pastMinuteTimestamp = currentTimestamp - 60;


            var realTimeStatsResult = await _dbContext.Database
                .SqlQueryRaw<RealTimeStatsModel>(@"
        SELECT
        COALESCE(SUM(t.nodes * t.occurrences), 0) / 3600.0 AS nps
        FROM public.perft_nodes_tasks t
        WHERE t.finished_at >= EXTRACT(EPOCH FROM NOW()) - 3600")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);


            var progressStatsResult = await _dbContext.Database
           .SqlQueryRaw<ProgressStatsModel>(@"
        SELECT
        COALESCE(SUM(t.nodes * t.occurrences), 0) AS total_nodes
        FROM public.perft_nodes_tasks t
        WHERE t.finished_at > 0")
           .AsNoTracking()
           .FirstOrDefaultAsync(cancellationToken);


            var response = new PerftStatsResponse()
            {
                Nps = realTimeStatsResult?.nps ?? 0,
                TotalNodes = progressStatsResult?.total_nodes ?? 0ul,
            };
            return Ok(response);
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


        [HttpGet("stats/charts/performance")]
        [ResponseCache(Duration = 300)]
        [OutputCache(Duration = 300)]
        public async Task<IActionResult> GetPerformanceChart(CancellationToken cancellationToken)
        {
            var result = await _dbContext.Database
                .SqlQueryRaw<PerformanceChartEntry>(@"
                    SELECT 
                      ((finished_at / 900)::bigint * 900) AS timestamp,
                      SUM(nodes * occurrences)::numeric / 900.0 AS nps
                    FROM public.perft_nodes_tasks
                    WHERE finished_at BETWEEN (EXTRACT(EPOCH FROM NOW()) - 43200)::bigint
                                             AND (EXTRACT(EPOCH FROM NOW()) - 900)::bigint
                    GROUP BY ((finished_at / 900)::bigint)
                    ORDER BY timestamp
                    ")
                .AsNoTracking()
                .ToListAsync(cancellationToken);


            return Ok(result);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 120)]
        [OutputCache(Duration = 120)]
        public async Task<IActionResult> GetLeaderboard(CancellationToken cancellationToken)
        {
            var oneHourAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600; // Get timestamp for one hour ago

            var stats = await _dbContext.PerftNodesTask
                .AsNoTracking()
                .Include(i => i.Account)
                .Where(i => i.FinishedAt > 0)
                .GroupBy(i => i.AccountId)
                .Select(g => new PerftLeaderboardResponse()
                {
                    AccountName = g.Select(i => i.Account != null ? i.Account.Name : "Unknown").FirstOrDefault(),
                    TotalNodes = (long)g.Sum(i => (float)i.Nps * (i.FinishedAt - i.StartedAt)),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FinishedAt - i.StartedAt),  // Total time in seconds across all tasks
                    NodesPerSecond = g.Where(i => i.FinishedAt >= oneHourAgo).Sum(i => (float)i.Nps * (i.FinishedAt - i.StartedAt)) / 3600.0f
                })
                .ToArrayAsync(cancellationToken);


            return Ok(stats);
        }
    }
}
