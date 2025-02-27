using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.Perft.V3;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrandChessTree.Api.Perft.PerftNodes
{
    [ApiController]
    [Route("api/v3/perft/fast")]
    public class PerftFastTaskController : ControllerBase
    {
        private readonly ILogger<PerftFastTaskController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly ApiKeyAuthenticator _apiKeyAuthenticator;
        private readonly PerftReadings _perftReadings;
        private readonly PerftFastTaskService _perftFastTaskService;
        public PerftFastTaskController(ILogger<PerftFastTaskController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, ApiKeyAuthenticator apiKeyAuthenticator, PerftReadings perftReadings, PerftFastTaskService perftFastTaskService)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
            _apiKeyAuthenticator = apiKeyAuthenticator;
            _perftReadings = perftReadings;
            _perftFastTaskService = perftFastTaskService;
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

            var currentTime = _timeProvider.GetUtcNow();
            var currentTimestamp = currentTime.ToUnixTimeSeconds();

            // Re-issue tasks that started more then two hours in the past
            var expiredAtTimeStamp = currentTime.AddHours(-2).ToUnixTimeSeconds();

            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var tasks = await _dbContext.PerftTasksV3
               .FromSqlRaw(@"
                    SELECT * FROM public.perft_tasks_v3 
                    WHERE fast_task_started_at <= {0} AND fast_task_finished_at = 0
                    ORDER BY depth ASC
                    LIMIT 500 FOR UPDATE SKIP LOCKED", expiredAtTimeStamp)
               .ToListAsync(cancellationToken);

            if (!tasks.Any())
            {
                return NotFound();
            }

            // Update fast tasks to prevent immediate reprocessing
            foreach (var item in tasks)
            {
                item.StartFastTask(currentTimestamp, apiKey.AccountId);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Commit transaction to finalize changes
            await transaction.CommitAsync(cancellationToken);

            // Prepare response
            return Ok(tasks.Select(task => new PerftFastTaskResponse
            {
                TaskId = task.Id,
                Board = task.Board,
                LaunchDepth = task.LaunchDepth,
                Depth = task.Depth,
            }));
        }

        [ApiKeyAuthorize]
        [HttpPost("results")]
        public async Task<IActionResult> SubmitResults(
            [FromBody] PerftFastTaskResultBatch request,
            CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyAuthenticator.GetApiKey(HttpContext, cancellationToken);
            if (apiKey == null)
            {
                return Unauthorized();
            }

            _perftFastTaskService.Enqueue(request, apiKey.AccountId);

            return Ok();
        }



        public class PerftLeaderboardResponse
        {
            [JsonPropertyName("account_id")]
            public long AccountId { get; set; }

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
            var result = await _perftReadings.GetTaskStats(PerftTaskType.Fast, cancellationToken);
            return Ok(result);
        }



        [HttpGet("stats/charts/performance")]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "account_id" })]
        [OutputCache(Duration = 300, VaryByQueryKeys = new[] { "account_id" })]
        public async Task<IActionResult> GetPerformanceChart([FromQuery(Name = "account_id")] int? accountId,
            CancellationToken cancellationToken)
        {
            List<PerformanceChartEntry> results;
            if (accountId.HasValue)
            {
                results = await _perftReadings.GetAccountTaskPerformance(PerftTaskType.Fast, accountId.Value, cancellationToken);
            }
            else
            {
                results = await _perftReadings.GetTaskPerformance(PerftTaskType.Fast, cancellationToken);
            }

            return Ok(results);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 120)]
        [OutputCache(Duration = 120)]
        public async Task<IActionResult> GetLeaderboard(CancellationToken cancellationToken)
        {
            var oneHourAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600; // Get timestamp for one hour ago

            var stats = await _dbContext.PerftTasksV3
                .AsNoTracking()
                .Include(i => i.FastTaskAccount)
                .Where(i => i.FastTaskFinishedAt > 0)
                .GroupBy(i => i.FastTaskAccountId)
                .Select(g => new PerftLeaderboardResponse()
                {
                    AccountId = g.Key.HasValue ? g.Key.Value : 0,
                    AccountName = g.Select(i => i.FastTaskAccount != null ? i.FastTaskAccount.Name : "Unknown").FirstOrDefault(),
                    TotalNodes = (long)g.Sum(i => ((float)i.FastTaskNodes * i.Occurrences)),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FastTaskFinishedAt - i.FastTaskStartedAt),  // Total time in seconds across all tasks
                    NodesPerSecond = g.Where(i => i.FastTaskFinishedAt >= oneHourAgo).Sum(i => (float)i.FastTaskNps * (i.FastTaskFinishedAt - i.FastTaskStartedAt)) / 3600.0f
                })
                .ToArrayAsync(cancellationToken);


            return Ok(stats);
        }
    }
}
