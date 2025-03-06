using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

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
            var job = await _dbContext.PerftJobs.AsNoTracking().FirstOrDefaultAsync(j => j.RootPositionId == positionId && j.Depth == depth);
            if(job == null)
            {
                return NotFound();
            }

            var (tpm, nps) = await _perftReadings.GetTaskPerformance(PerftTaskType.Fast, positionId, depth, cancellationToken);

            var completedTasks = job.CompletedFastTasks;
            var totalNodes = job.FastTaskNodes;
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
          var response = await _perftReadings.GetPerformanceChart(PerftTaskType.Fast, positionId, depth, cancellationToken);
            if (response.Count >= 2)
            {
                // Hack, since the last element is only partially complete
                response.RemoveAt(response.Count - 1);
            }
            return Ok(response);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "positionId", "depth" })]
        [OutputCache(Duration = 300, VaryByQueryKeys = new[] { "positionId", "depth" })]
        public async Task<IActionResult> GetLeaderboard(int positionId, int depth, CancellationToken cancellationToken)
        {
            var contributors = await _dbContext.PerftContributions.AsNoTracking().Include(c => c.Account).Where(c => c.RootPositionId == positionId && c.Depth == depth).ToListAsync(cancellationToken);

            var results = await _perftReadings.GetLeaderboard(PerftTaskType.Fast, cancellationToken);

            var leaderboard = new List<PerftLeaderboardResponse>();

            foreach(var contributor in contributors)
            {
                if(contributor.Account == null)
                {
                    continue;
                }

                results.TryGetValue(contributor.Account.Id, out var stats);

                leaderboard.Add(new PerftLeaderboardResponse()
                {
                    AccountId = contributor.Account.Id,
                    AccountName = contributor.Account.Name,
                    CompletedTasks = contributor.CompletedFastTasks,
                    TotalTasks = contributor.CompletedFastTasks,
                    NodesPerSecond = stats.nps,
                    TasksPerMinute = stats.tpm,
                    TotalNodes = (long)contributor.FastTaskNodes,
                    TotalTimeSeconds = 0,
                });
            }

            return Ok(leaderboard.Where(r => r.TotalTasks > 0));
        }
    }
}
