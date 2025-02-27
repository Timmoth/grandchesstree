using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Database;
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
        public PerftFullTaskPositionController(ILogger<PerftFullTaskPositionController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
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
            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            var pastMinuteTimestamp = currentTimestamp - 60;

            var totalTaskCount = await _dbContext.PerftTasksV3.CountAsync(i => i.RootPositionId == positionId && i.Depth == depth);

            var realTimeStatsResult = await _dbContext.Database
                .SqlQueryRaw<RealTimeStatsModel>(@"
       SELECT
        COALESCE(COUNT(*), 0) / 60.0 AS tpm,
        COALESCE(SUM(nodes * occurrences), 0) / 3600.0 AS nps
        FROM public.perft_readings
        WHERE root_position_id = {0} AND depth = {1}
        AND time >= NOW() - INTERVAL '1 hour' AND task_type = 0", positionId, depth)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);


            var progressStatsResult = await _dbContext.Database
           .SqlQueryRaw<ProgressStatsModel>(@"
           SELECT
        COUNT(*) AS completed_tasks,
        COALESCE(SUM(nodes * occurrences), 0) AS total_nodes
        FROM public.perft_readings
        WHERE root_position_id = {0} AND depth = {1} AND task_type = 0", positionId, depth)
           .AsNoTracking()
           .FirstOrDefaultAsync(cancellationToken);


            var response = new PerftStatsResponse()
            {
                Nps = realTimeStatsResult?.nps ?? 0,
                Tpm = realTimeStatsResult?.tpm ?? 0,
                CompletedTasks = progressStatsResult?.completed_tasks ?? 0ul,
                TotalNodes = progressStatsResult?.total_nodes ?? 0ul,
                PercentCompletedTasks = (float)(progressStatsResult?.completed_tasks ?? 0ul) / totalTaskCount * 100,
                TotalTasks = totalTaskCount,
            };
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
            // Get the current Unix time in seconds.
            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

            // Calculate the last complete 15-minute interval.
            // (now / 900) * 900 gives the start of the current 15-minute block,
            // so subtract 900 seconds to get the last complete block.
            var end = ((now / 900) * 900) - 900;

            // Set start as 12 hours (43200 seconds) before the end of the last full interval.
            var start = end - 43200;

            var result = await _dbContext.Database
                .SqlQueryRaw<PerformanceChartEntry>(@"
       SELECT 
              time_bucket('15 minutes', time) AS timestamp,
              SUM(nodes * occurrences)::numeric / 900.0 AS nps
            FROM public.perft_readings
            WHERE time >= NOW() - INTERVAL '1 hour' AND task_type = 0
            AND root_position_id = {0} AND depth = {1}
            GROUP BY timestamp
            ORDER BY timestamp;
                ", positionId, depth)
                            .AsNoTracking()
                            .ToListAsync(cancellationToken);



            return Ok(result);
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
