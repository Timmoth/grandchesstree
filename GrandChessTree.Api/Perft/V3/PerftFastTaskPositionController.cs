using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using SendGrid;

namespace GrandChessTree.Api.Perft.PerftNodes
{
    [ApiController]
    [Route("api/v3/perft/fast/{positionId}/{depth}")]
    public class PerftFastTaskPositionController : ControllerBase
    {
        private readonly ILogger<PerftFastTaskPositionController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly PerftReadings _perftReadings;
        public PerftFastTaskPositionController(ILogger<PerftFastTaskPositionController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, PerftReadings perftReadings)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
            _perftReadings = perftReadings;
        }

        public class ProgressStatsModel
        {
            [Column("completed_tasks")]
            [JsonPropertyName("completed_tasks")]
            public ulong completed_tasks { get; set; }
            [Column("total_nodes")]
            [JsonPropertyName("total_nodes")]
            public ulong total_nodes { get; set; }
        }

        public class RealTimeStatsModel
        {
            [Column("tpm")]
            [JsonPropertyName("tpm")]
            public float tpm { get; set; }
            [Column("nps")]
            [JsonPropertyName("nps")]
            public float nps { get; set; }
        }

        [HttpGet("stats")]
        [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "positionId", "depth" })]
        [OutputCache(Duration = 30, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetStats(int positionId, int depth, CancellationToken cancellationToken)
        {
            var response = await _perftReadings.GetTaskPerformance(PerftTaskType.Fast, positionId, depth, cancellationToken);
            return Ok(response);
        }

        public class PerformanceChartEntry
        {
            [Column("timestamp")]
            [JsonPropertyName("timestamp")]
            public long timestamp { get; set; }
            [Column("tpm")]
            [JsonPropertyName("tpm")]
            public float tpm { get; set; }
            [Column("nps")]
            [JsonPropertyName("nps")]
            public float nps { get; set; }
        }


        [HttpGet("stats/charts/performance")]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "positionId", "depth" })]
        [OutputCache(Duration = 300, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetPerformanceChart(int positionId, int depth, CancellationToken cancellationToken)
        {
          var response = await _perftReadings.GetPerformanceChart(PerftTaskType.Fast, positionId, depth, cancellationToken);

            return Ok(response);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        [OutputCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetLeaderboard(int positionId, int depth, CancellationToken cancellationToken)
        {
            var oneHourAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600; // Get timestamp for one hour ago

            var stats = await _dbContext.PerftTasksV3
                .AsNoTracking()
                .Include(i => i.FastTaskAccount)
                .Where(i => i.FastTaskFinishedAt > 0 && i.RootPositionId == positionId && i.Depth == depth)
                .GroupBy(i => i.FastTaskAccountId)
                .Select(g => new PerftLeaderboardResponse()
                {
                    AccountId = g.Key.HasValue ? g.Key.Value : 0,
                    AccountName = g.Select(i => i.FastTaskAccount != null ? i.FastTaskAccount.Name : "Unknown").FirstOrDefault(),
                    TotalNodes = (long)g.Sum(i => (float)i.FastTaskNodes * i.Occurrences),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FastTaskFinishedAt - i.FastTaskStartedAt),  // Total time in seconds across all tasks
                    CompletedTasks = g.Count(),  // Number of tasks completed
                    TasksPerMinute = g.Count(i => i.FastTaskFinishedAt >= oneHourAgo) / 60.0f,  // Tasks completed in last hour / 60
                    NodesPerSecond = g.Where(i => i.FastTaskFinishedAt >= oneHourAgo).Sum(i => (float)i.FastTaskNps * (i.FastTaskFinishedAt - i.FastTaskStartedAt)) / 3600.0f
                })
                .ToArrayAsync(cancellationToken);


            return Ok(stats);
        }


        [HttpGet("results")]
        [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        [OutputCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetResults(int positionId, int depth,
           CancellationToken cancellationToken)
        {
            var result = await _dbContext.Database
                .SqlQueryRaw<PerftNodesResult>(@"
            SELECT 
                SUM(t.fast_task_nodes * t.occurrences) AS nodes
            FROM public.perft_tasks_v3 t
            WHERE t.root_position_id = {0} AND t.depth = {1}", positionId, depth)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
            {
                return NotFound();
            }

            return Ok(result);
        }

    }
}
