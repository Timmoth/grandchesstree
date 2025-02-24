using System.Text.Json.Serialization;
using SendGrid.Helpers.Mail;
using System.ComponentModel.DataAnnotations;

namespace GrandChessTree.Api.Controllers
{
    public class CreateAccountRequest
    {
        [JsonPropertyName("name")]
        [Required(ErrorMessage = "Name is required.")]
        [MinLength(3, ErrorMessage = "Name must be at least 3 characters long.")]
        [MaxLength(25, ErrorMessage = "Name cannot exceed 25 characters.")]
        [RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "Name can only contain alphanumeric characters, underscores, and hyphens.")]
        public required string Name { get; set; }

        [JsonPropertyName("email")]
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public required string Email { get; set; }
    }
}
