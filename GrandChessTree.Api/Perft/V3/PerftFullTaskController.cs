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

namespace GrandChessTree.Api.Controllers
{
    [ApiController]
    [Route("api/v3/perft/full")]
    public class PerftFullTaskController : ControllerBase
    {     
        private readonly ILogger<PerftFullTaskController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly ApiKeyAuthenticator _apiKeyAuthenticator;
        private readonly PerftReadings _perftReadings;
        private readonly PerftFullTaskService _perftFullTaskService;
        public PerftFullTaskController(ILogger<PerftFullTaskController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, ApiKeyAuthenticator apiKeyAuthenticator, PerftReadings perftReadings, PerftFullTaskService perftFullTaskService)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
            _apiKeyAuthenticator = apiKeyAuthenticator;
            _perftReadings = perftReadings;
            _perftFullTaskService = perftFullTaskService;
        }

        [ApiKeyAuthorize]
        [HttpPost("tasks")]
        public async Task<IActionResult> CreateNewTaskBatch(CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyAuthenticator.GetApiKey(HttpContext, cancellationToken);

            if(apiKey == null)
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
                    WHERE full_task_started_at <= {0} AND full_task_finished_at = 0
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
                item.StartFullTask(currentTimestamp, apiKey.AccountId);
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
             [FromBody] PerftFullTaskResultBatch request,
             CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyAuthenticator.GetApiKey(HttpContext, cancellationToken);
            if (apiKey == null)
            {
                return Unauthorized();
            }

            _perftFullTaskService.Enqueue(request, apiKey.AccountId);

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
            var currentTime = _timeProvider.GetUtcNow();
            var currentTimestamp = currentTime.ToUnixTimeSeconds();
            var pastMinuteTimestamp = currentTime.AddSeconds(-60).ToUnixTimeSeconds();
            var pastHourTimestamp = currentTime.AddHours(-1).ToUnixTimeSeconds();


            var realTimeStatsResult = await _dbContext.Database
                .SqlQueryRaw<RealTimeStatsModel>(@"
      SELECT
    COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
FROM public.perft_readings 
WHERE time >= NOW() - INTERVAL '1 hour' AND task_type = 0;")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);


            var progressStatsResult = await _dbContext.Database
           .SqlQueryRaw<ProgressStatsModel>(@"
         SELECT
SUM(nodes * occurrences) AS total_nodes
FROM public.perft_readings
WHERE task_type = 0;")
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
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "account_id" })]
        [OutputCache(Duration = 300, VaryByQueryKeys = new[] { "account_id" })]
        public async Task<IActionResult> GetPerformanceChart([FromQuery(Name = "account_id")] int? accountId, CancellationToken cancellationToken)
        {
            // Build the SQL query with optional filtering by account_id.
            string sql;
            object[] sqlParams;

            if (accountId.HasValue)
            {
                sql = @"
                   SELECT 
              time_bucket('15 minutes', time) AS timestamp,
              SUM(nodes * occurrences)::numeric / 900.0 AS nps
            FROM public.perft_readings
            WHERE time >= NOW() - INTERVAL '1 hour' AND task_type = 0
              AND account_id = {0}
            GROUP BY timestamp
            ORDER BY timestamp;
            ";
                sqlParams = new object[] { accountId.Value };
            }
            else
            {
                sql = @"
                   SELECT 
              time_bucket('15 minutes', time) AS timestamp,
              SUM(nodes * occurrences)::numeric / 900.0 AS nps
            FROM public.perft_readings
            WHERE time >= NOW() - INTERVAL '1 hour' AND task_type = 0
            GROUP BY timestamp
            ORDER BY timestamp;";
                sqlParams = new object[] { };
            }

            var result = await _dbContext.Database
                .SqlQueryRaw<PerformanceChartEntry>(sql, sqlParams)
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

            var stats = await _dbContext.PerftTasksV3
                .AsNoTracking()
                .Include(i => i.FullTaskAccount)
                .Where(i => i.FullTaskFinishedAt > 0)
                .GroupBy(i => i.FullTaskAccountId)
                .Select(g => new PerftLeaderboardResponse()
                {
                    AccountId = g.Key.HasValue ? g.Key.Value : 0,
                    AccountName = g.Select(i => i.FullTaskAccount != null ? i.FullTaskAccount.Name : "Unknown").FirstOrDefault(),
                    TotalNodes = (long)g.Sum(i => (float)i.FullTaskNps * (i.FullTaskFinishedAt - i.FullTaskStartedAt)),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FullTaskFinishedAt - i.FullTaskStartedAt),  // Total time in seconds across all tasks
                    NodesPerSecond = g.Where(i => i.FullTaskFinishedAt >= oneHourAgo).Sum(i => (float)i.FullTaskNps * (i.FullTaskFinishedAt - i.FullTaskStartedAt)) / 3600.0f
                })
                .ToArrayAsync(cancellationToken);


            return Ok(stats);
        }
    }
}
