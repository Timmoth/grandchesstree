using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.Performance;
using GrandChessTree.Api.Perft.V3;
using GrandChessTree.Api.timescale;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace GrandChessTree.Api.Perft.PerftNodes
{
    [ApiController]
    [Route("api/v4/perft/fast")]
    public class PerftFastTaskControllerV4 : ControllerBase
    {
        private readonly ILogger<PerftFastTaskControllerV4> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly ApiKeyAuthenticator _apiKeyAuthenticator;
        private readonly PerftReadings _perftReadings;
        private readonly PerftFastTaskService _perftFastTaskService;
        public PerftFastTaskControllerV4(ILogger<PerftFastTaskControllerV4> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, ApiKeyAuthenticator apiKeyAuthenticator, PerftReadings perftReadings, PerftFastTaskService perftFastTaskService)
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
                    WHERE fast_task_started_at = 0 AND fast_task_finished_at = 0
                    ORDER BY depth ASC, id ASC
                    LIMIT 1000 FOR UPDATE SKIP LOCKED", expiredAtTimeStamp)
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

            // Prepare response using custom binary encoding
            var responses = tasks.Select(task => new PerftTaskResponse
            {
                TaskId = task.Id,
                Board = task.Board,
                LaunchDepth = task.LaunchDepth,
                Depth = task.Depth,
            }).ToList();

            var binaryData = PerftTasksBinaryConverter.Encode(responses);

            // Return binary data in response
            return File(binaryData, "application/octet-stream");

        }


        [ApiKeyAuthorize]
        [HttpPut("tasks")]
        public async Task<IActionResult> SubmitResults(
            CancellationToken cancellationToken)
        {
            var apiKey = await _apiKeyAuthenticator.GetApiKey(HttpContext, cancellationToken);
            if (apiKey == null)
            {
                return Unauthorized();
            }

            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, cancellationToken);
            var requestData = ms.ToArray();
            var result = PerftFastTaskResultBatchBinaryConverter.Decode(requestData);

            PerformanceStatsService.Update(apiKey.AccountId, result.WorkerId, result.Threads, result.AllocatedMb, result.Mips, PerftTaskType.Fast, _timeProvider.GetUtcNow().ToUnixTimeSeconds());

            _perftFastTaskService.Enqueue(result, apiKey.AccountId);

            return Ok();
        }

        [HttpGet("stats")]
        [ResponseCache(Duration = 300)]
        [OutputCache(Duration = 300)]
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
            if (results.Count >= 2)
            {
                // Hack, since the last element is only partially complete
                results.RemoveAt(results.Count - 1);
            }
            return Ok(results);
        }

        [HttpGet("leaderboard")]
        [ResponseCache(Duration = 300)]
        [OutputCache(Duration = 300)]
        public async Task<IActionResult> GetLeaderboard(CancellationToken cancellationToken)
        {
            var contributors = await _dbContext.PerftContributions.AsNoTracking().Include(c => c.Account).ToListAsync(cancellationToken);

            var results = await _perftReadings.GetLeaderboard(PerftTaskType.Fast, cancellationToken);

            var leaderboard = new List<PerftLeaderboardResponse>();

            var accounts = contributors.Select(c => c.Account).DistinctBy(a => a?.Id).ToDictionary(a => a?.Id ?? -1, a => a);
            var totals = PerformanceStatsService.GetFastTaskTotals(_timeProvider.GetUtcNow().ToUnixTimeSeconds());

            foreach (var contributor in contributors.GroupBy(c => c.AccountId))
            {
                if (contributor.Key == null)
                {
                    continue;
                }

                if(!accounts.TryGetValue(contributor.Key.Value, out var account) || account == null){
                    continue;
                }
                totals.TryGetValue(contributor.Key.Value, out var total);

                results.TryGetValue(contributor.Key.Value, out var stats);

                leaderboard.Add(new PerftLeaderboardResponse()
                {
                    AccountId = account.Id,
                    AccountName = account.Name,
                    CompletedTasks = contributor.Sum(c => c.CompletedFastTasks),
                    TotalTasks = contributor.Sum(c => c.CompletedFastTasks),
                    NodesPerSecond = stats.nps,
                    TasksPerMinute = stats.tpm,
                    TotalNodes = (long)contributor.Sum(c => c.FastTaskNodes),
                    TotalTimeSeconds = 0,
                    Threads = total?.Threads ?? 0,
                    Mips = total?.Mips ?? 0,
                    Workers = total?.Workers ?? 0,
                    AllocatedMb = total?.AllocatedMb ?? 0,
                });
            }

            return Ok(leaderboard.Where(r => r.TotalTasks > 0));
        }
    }
}
