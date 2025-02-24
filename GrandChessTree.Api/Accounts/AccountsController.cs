using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.Database;
using GrandChessTree.Shared.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SendGrid.Helpers.Mail;
using SendGrid;
using GrandChessTree.Api.Accounts;
using GrandChessTree.Shared.ApiKeys;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GrandChessTree.Api.Controllers
{
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
    }
}
