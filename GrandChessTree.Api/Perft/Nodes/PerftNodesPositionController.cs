using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Database;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.PerftNodes
{
    [ApiController]
    [Route("api/v2/perft/nodes/{positionId}/{depth}")]
    public class PerftNodesPositionController : ControllerBase
    {
        private readonly ILogger<PerftNodesPositionController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        public PerftNodesPositionController(ILogger<PerftNodesPositionController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider)
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
        [ResponseCache(Duration = 10, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetStats(int positionId, int depth, CancellationToken cancellationToken)
        {
            var currentTimestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            var pastMinuteTimestamp = currentTimestamp - 60;

            var totalTaskCount = await _dbContext.PerftNodesTask.CountAsync(i => i.RootPositionId == positionId && i.Depth == depth);

            var realTimeStatsResult = await _dbContext.Database
                .SqlQueryRaw<RealTimeStatsModel>(@"
        SELECT
        COALESCE(COUNT(*), 0) / 60.0 AS tpm,
        COALESCE(SUM(t.nodes * t.occurrences), 0) / 3600.0 AS nps
        FROM public.perft_nodes_tasks t
        WHERE t.root_position_id = {0} AND t.depth = {1}
        AND t.finished_at >= EXTRACT(EPOCH FROM NOW()) - 3600", positionId, depth)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);


            var progressStatsResult = await _dbContext.Database
           .SqlQueryRaw<ProgressStatsModel>(@"
        SELECT
        COUNT(*) AS completed_tasks,
        COALESCE(SUM(t.nodes * t.occurrences), 0) AS total_nodes
        FROM public.perft_nodes_tasks t
        WHERE t.root_position_id = {0} AND t.depth = {1} AND t.finished_at > 0", positionId, depth)
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
        public async Task<IActionResult> GetPerformanceChart(int positionId, int depth, CancellationToken cancellationToken)
        {
            var result = await _dbContext.Database
                .SqlQueryRaw<PerformanceChartEntry>(@"
                    WITH time_buckets AS (
                        SELECT generate_series(
                            EXTRACT(EPOCH FROM NOW()) - 43200,  -- 3 hours ago
                            EXTRACT(EPOCH FROM NOW()),         -- Now
                            900                                -- 15-minute intervals (900 seconds)
                        ) AS bucket_start
                    )
                    SELECT 
                        tb.bucket_start AS timestamp,
                        COUNT(t.id) / 15.0 AS tpm,  -- Tasks per minute (since interval is 15 min)
                        COALESCE(SUM(t.nodes * t.occurrences) / (15 * 60), 0) AS nps  -- Nodes per second
                    FROM time_buckets tb
                    LEFT JOIN public.perft_nodes_tasks t 
                        ON t.finished_at >= tb.bucket_start 
                        AND t.finished_at < tb.bucket_start + 900  -- 15-minute window
                    WHERE t.root_position_id = {0} AND t.depth = {1}
                    GROUP BY tb.bucket_start
                    ORDER BY timestamp
                    ", positionId, depth)
                .AsNoTracking()
                .ToListAsync(cancellationToken);


            return Ok(result);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetLeaderboard(int positionId, int depth, CancellationToken cancellationToken)
        {
            var oneHourAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600; // Get timestamp for one hour ago

            var stats = await _dbContext.PerftNodesTask
                .AsNoTracking()
                .Include(i => i.Account)
                .Where(i => i.FinishedAt > 0 && i.RootPositionId == positionId && i.Depth == depth)
                .GroupBy(i => i.AccountId)
                .Select(g => new PerftLeaderboardResponse()
                {
                    AccountName = g.Select(i => i.Account != null ? i.Account.Name : "Unknown").FirstOrDefault(),
                    TotalNodes = (long)g.Sum(i => (float)i.Nps * (i.FinishedAt - i.StartedAt)),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FinishedAt - i.StartedAt),  // Total time in seconds across all tasks
                    CompletedTasks = g.Count(),  // Number of tasks completed
                    TasksPerMinute = g.Count(i => i.FinishedAt >= oneHourAgo) / 60.0f,  // Tasks completed in last hour / 60
                    NodesPerSecond = g.Where(i => i.FinishedAt >= oneHourAgo).Sum(i => (float)i.Nps * (i.FinishedAt - i.StartedAt)) / 3600.0f
                })
                .ToArrayAsync(cancellationToken);


            return Ok(stats);
        }


        [HttpGet("results")]
        [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetResults(int positionId, int depth,
           CancellationToken cancellationToken)
        {
            var result = await _dbContext.Database
                .SqlQueryRaw<PerftNodesResult>(@"
            SELECT 
                SUM(t.nodes * t.occurrences) AS nodes
            FROM public.perft_nodes_tasks t
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
