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
using GrandChessTree.Api.timescale;
using GrandChessTree.Api.Migrations;

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

        [JsonPropertyName("workers")]
        public required List<WorkerStats> Workers { get; set; }
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
        private readonly PerftReadings _perftReadings;
        public AccountsController(ILogger<AccountsController> logger, ApplicationDbContext dbContext, TimeProvider timeProvider, PerftReadings perftReadings)
        {
            _logger = logger;
            _dbContext = dbContext;
            _timeProvider = timeProvider;
            _perftReadings = perftReadings;
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
            _dbContext.ApiKeys.Add(apiKey);

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
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "id" })]
        [OutputCache(Duration = 300, VaryByQueryKeys = new[] { "id" })]
        public async Task<IActionResult> GetAccount(long id, CancellationToken cancellationToken)
        {
            var account = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (account == null)
            {
                return NotFound();
            }

            var contributions = await _dbContext.PerftContributions.AsNoTracking().Where(c => c.AccountId == account.Id).ToListAsync(cancellationToken);
            var fastResults = await _perftReadings.GetLeaderboard(PerftTaskType.Fast, (int)account.Id, cancellationToken);
            var fullResults = await _perftReadings.GetLeaderboard(PerftTaskType.Full, (int)account.Id, cancellationToken);
            var workerResults = await _perftReadings.GetWorkerStats((int)account.Id, cancellationToken);

            var response = new AccountResponse()
            {
                Id = account.Id,
                Name = account.Name,
                Task0 = new AccountTaskStatsResponse()
                {
                    CompletedTasks = contributions.Sum(c => c.CompletedFullTasks),
                    NodesPerSecond = fullResults.nps,
                    TasksPerMinute = fullResults.tpm,
                    TotalNodes = (long)contributions.Sum(c => c.FullTaskNodes),
                    TotalTimeSeconds = 0,
                },
                Task1 = new AccountTaskStatsResponse()
                {
                    CompletedTasks = contributions.Sum(c => c.CompletedFastTasks),
                    NodesPerSecond = fastResults.nps,
                    TasksPerMinute = fastResults.tpm,
                    TotalNodes = (long)contributions.Sum(c => c.FastTaskNodes),
                    TotalTimeSeconds = 0,
                },
                Workers = workerResults,
            };

            return Ok(response);
        }
    }
}
