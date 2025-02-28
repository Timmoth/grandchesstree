using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.Database;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SendGrid.Helpers.Mail;
using SendGrid;
using GrandChessTree.Api.Accounts;
using GrandChessTree.Shared.ApiKeys;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OutputCaching;

namespace GrandChessTree.Api.Controllers
{
    public class AccountResponse
    {
        [JsonPropertyName("id")]
        public required long Id { get; set; }

        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("task_0")]
        public required AccountTaskStatsResponse Task0 { get; set; }

        [JsonPropertyName("task_1")]
        public required AccountTaskStatsResponse Task1 { get; set; }
    }


    public class AccountTaskStatsResponse
    {
        [JsonPropertyName("total_nodes")]
        public long TotalNodes { get; set; }

        [JsonPropertyName("compute_time_seconds")]
        public long TotalTimeSeconds { get; set; }

        [JsonPropertyName("completed_tasks")]
        public long CompletedTasks { get; set; }

        [JsonPropertyName("tpm")]
        public float TasksPerMinute { get; set; }

        [JsonPropertyName("nps")]
        public float NodesPerSecond { get; set; }
    }

    [ApiController]
    [Route("api/v1/accounts")]
    public class AccountsController : ControllerBase
    {     
        private readonly ILogger<AccountsController> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        public AccountsController(ILogger<AccountsController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
        }

        [HttpPost]
        public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var loweredName = request.Name.ToLower();
            var loweredEmail = request.Email.ToLower();
            var alreadyExists = await _dbContext.Accounts.AnyAsync(a => a.Name.ToLower() == loweredName || a.Email.ToLower() == loweredEmail, cancellationToken);

            if (alreadyExists)
            {
                return Conflict();
            }

            var account = _dbContext.Accounts.Add(new AccountModel()
            {
                Name = request.Name,
                Email = request.Email,
                Role = AccountRole.User,
                ApiKeys = new List<ApiKeyModel>()
            });

            var (id, key, tail) = ApiKeyGenerator.Create();
            var apiKey = new ApiKeyModel()
            {

                Id = id,
                Role = AccountRole.User,
                AccountId = account.Entity.Id,
                Account = account.Entity,
                ApiKeyTail = tail,
                CreatedAt = _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            };

            await _dbContext.SaveChangesAsync(cancellationToken);

            var sendGridApiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
            var client = new SendGridClient(sendGridApiKey);
            var from_email = new EmailAddress("admin@grandchesstree.com", "Admin");
            var subject = "Welcome to the Grand Chess Tree!";
            var to_email = new EmailAddress(request.Email, request.Name);

            var plainTextContent = $"Thanks for signing up, here's your ApiKey: {key}";
            var htmlContent = $"<strong>{plainTextContent}</strong>";
            var msg = MailHelper.CreateSingleEmail(from_email, to_email, subject, plainTextContent, htmlContent);
            var response = await client.SendEmailAsync(msg).ConfigureAwait(false);
            return Ok();
        }


        [HttpGet("{id}")]
        [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "id" })]
        [OutputCache(Duration = 120, VaryByQueryKeys = new[] { "id" })]
        public async Task<IActionResult> GetAccount(long id, CancellationToken cancellationToken)
        {
            var account = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (account == null)
            {
                return NotFound();
            }
            var oneHourAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600; // Get timestamp for one hour ago

            var task0Stats = await _dbContext.PerftTasksV3
                         .AsNoTracking()
                         .Include(i => i.FullTaskAccount)
                         .Where(i => i.FullTaskFinishedAt > 0 && i.FullTaskAccountId == id)
                         .GroupBy(i => i.FullTaskAccountId)
                         .Select(g => new AccountTaskStatsResponse()
                         {
                             TotalNodes = (long)g.Sum(i => (float)i.FullTaskNps * (i.FullTaskFinishedAt - i.FullTaskStartedAt)),  // Total nodes produced
                             TotalTimeSeconds = g.Sum(i => i.FullTaskFinishedAt - i.FullTaskStartedAt),  // Total time in seconds across all tasks
                             CompletedTasks = g.Count(),  // Number of tasks completed
                             TasksPerMinute = g.Count(i => i.FullTaskFinishedAt >= oneHourAgo) / 60.0f,  // Tasks completed in last hour / 60
                             NodesPerSecond = g.Where(i => i.FullTaskFinishedAt >= oneHourAgo).Sum(i => (float)i.FullTaskNps * (i.FullTaskFinishedAt - i.FullTaskStartedAt)) / 3600.0f
                         })
                         .FirstOrDefaultAsync(cancellationToken);

            var task1Stats = await _dbContext.PerftTasksV3
                .AsNoTracking()
                .Include(i => i.FastTaskAccount)
                .Where(i => i.FastTaskFinishedAt > 0 && i.FastTaskAccountId == id)
                .GroupBy(i => i.FastTaskAccountId)
                .Select(g => new AccountTaskStatsResponse()
                {
                    TotalNodes = (long)g.Sum(i => (float)i.FastTaskNps * (i.FastTaskFinishedAt - i.FastTaskStartedAt)),  // Total nodes produced
                    TotalTimeSeconds = g.Sum(i => i.FastTaskFinishedAt - i.FastTaskStartedAt),  // Total time in seconds across all tasks
                    CompletedTasks = g.Count(),  // Number of tasks completed
                    TasksPerMinute = g.Count(i => i.FastTaskFinishedAt >= oneHourAgo) / 60.0f,  // Tasks completed in last hour / 60
                    NodesPerSecond = g.Where(i => i.FastTaskFinishedAt >= oneHourAgo).Sum(i => (float)i.FastTaskNps * (i.FastTaskFinishedAt - i.FastTaskStartedAt)) / 3600.0f
                })
                .FirstOrDefaultAsync(cancellationToken);


            var response = new AccountResponse()
            {
                Id = account.Id,
                Name = account.Name,
                Task0 = task0Stats ?? new AccountTaskStatsResponse(),
                Task1 = task1Stats ?? new AccountTaskStatsResponse(),
            };

            return Ok(response);
        }
    }
}
