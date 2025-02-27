using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Controllers
{
    [ApiController]
    [Route("api/v3/perft/full/{positionId}/{depth}")]
    public class PerftFullTaskPositionController : ControllerBase
    {     
        private readonly ILogger<PerftFullTaskPositionController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly PerftReadings _perftReadings;

        public PerftFullTaskPositionController(ILogger<PerftFullTaskPositionController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, PerftReadings perftReadings)
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
            var response = await _perftReadings.GetTaskPerformance(PerftTaskType.Full, positionId, depth, cancellationToken);
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
            var response = await _perftReadings.GetPerformanceChart(PerftTaskType.Full, positionId, depth, cancellationToken);

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
                .Include(i => i.FullTaskAccount)
                .Where(i => i.FullTaskFinishedAt > 0 && i.RootPositionId == positionId && i.Depth == depth)
                .GroupBy(i => i.FullTaskAccountId)
                .Select(g => new PerftLeaderboardResponse()
                {
                    AccountId = g.Key.HasValue ? g.Key.Value : 0,
                    AccountName = g.Select(i => i.FullTaskAccount != null ? i.FullTaskAccount.Name : "Unknown").FirstOrDefault(),
                    TotalNodes = (long)g.Sum(i => (float)i.FullTaskNps * (i.FullTaskFinishedAt - i.FullTaskStartedAt)),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FullTaskFinishedAt - i.FullTaskStartedAt),  // Total time in seconds across all tasks
                    CompletedTasks = g.Count(),  // Number of tasks completed
                    TasksPerMinute = g.Count(i => i.FullTaskFinishedAt >= oneHourAgo) / 60.0f,  // Tasks completed in last hour / 60
                    NodesPerSecond = g.Where(i => i.FullTaskFinishedAt >= oneHourAgo).Sum(i => (float)i.FullTaskNps * (i.FullTaskFinishedAt - i.FullTaskStartedAt)) / 3600.0f
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
                .SqlQueryRaw<PerftResultV3>(@"
            SELECT 
                SUM(t.full_task_nodes * t.occurrences) AS nodes,
                SUM(t.captures * t.occurrences) AS captures,
                SUM(t.enpassants * t.occurrences) AS enpassants,
                SUM(t.castles * t.occurrences) AS castles,
                SUM(t.promotions * t.occurrences) AS promotions,
                SUM(t.direct_checks * t.occurrences) AS direct_checks,
                SUM(t.single_discovered_checks * t.occurrences) AS single_discovered_checks,
                SUM(t.direct_discovered_checks * t.occurrences) AS direct_discovered_checks,
                SUM(t.double_discovered_checks * t.occurrences) AS double_discovered_checks,
                SUM(t.direct_mates * t.occurrences) AS direct_mates,
                SUM(t.single_discovered_mates * t.occurrences) AS single_discovered_mates,
                SUM(t.direct_discovered_mates * t.occurrences) AS direct_discovered_mates,
                SUM(t.double_discovered_mates * t.occurrences) AS double_discovered_mates
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
