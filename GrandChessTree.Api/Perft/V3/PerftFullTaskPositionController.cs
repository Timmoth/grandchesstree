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
            var job = await _dbContext.PerftJobs.FirstOrDefaultAsync(j => j.RootPositionId == positionId && j.Depth == depth);
            if (job == null)
            {
                return NotFound();
            }

            var (tpm, nps) = await _perftReadings.GetTaskPerformance(PerftTaskType.Full, positionId, depth, cancellationToken);

            var completedTasks = job.CompletedFullTasks;
            var totalNodes = job.FullTaskNodes;
            var taskCount = job.TotalTasks;
            return Ok(new PerftStatsResponse()
            {
                Nps = nps,
                Tpm = tpm,
                CompletedTasks = (ulong)completedTasks,
                TotalNodes = (ulong)totalNodes,
                PercentCompletedTasks = taskCount > 0 ? (float)completedTasks / taskCount * 100 : 0,
                TotalTasks = (int)taskCount
            });
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
            if (response.Count >= 2)
            {
                // Hack, since the last element is only partially complete
                response.RemoveAt(response.Count - 1);
            }
            return Ok(response);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        [OutputCache(Duration = 120, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetLeaderboard(int positionId, int depth, CancellationToken cancellationToken)
        {
            var contributors = await _dbContext.PerftContributions.Include(c => c.Account).Where(c => c.RootPositionId == positionId && c.Depth == depth).ToListAsync(cancellationToken);

            var results = await _perftReadings.GetLeaderboard(PerftTaskType.Full, cancellationToken);

            var leaderboard = new List<PerftLeaderboardResponse>();

            foreach (var contributor in contributors)
            {
                if (contributor.Account == null)
                {
                    continue;
                }

                results.TryGetValue(contributor.Account.Id, out var stats);

                leaderboard.Add(new PerftLeaderboardResponse()
                {
                    AccountId = contributor.Account.Id,
                    AccountName = contributor.Account.Name,
                    CompletedTasks = contributor.CompletedFullTasks,
                    TotalTasks = contributor.CompletedFullTasks,
                    NodesPerSecond = stats.nps,
                    TasksPerMinute = stats.tpm,
                    TotalNodes = (long)contributor.FullTaskNodes,
                    TotalTimeSeconds = 0,
                });
            }

            return Ok(leaderboard.Where(r => r.TotalTasks > 0));
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
