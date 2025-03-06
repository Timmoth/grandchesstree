using GrandChessTree.Api.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using GrandChessTree.Api.Performance;

namespace GrandChessTree.Api.Controllers
{
    [ApiController]
    [Route("api/v4/performance")]
    public class PerformanceController : ControllerBase
    {     
        private readonly ILogger<PerformanceController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        public PerformanceController(ILogger<PerformanceController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
        }

        [HttpGet]
        [ResponseCache(Duration = 300)]
        [OutputCache(Duration = 300)]
        public async Task<IActionResult> GetTotalPerformance(CancellationToken cancellationToken)
        {
            var currentUnixSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            var totals = PerformanceStatsService.GetTotals(currentUnixSeconds);
            return Ok(totals);
        }
    }
}
