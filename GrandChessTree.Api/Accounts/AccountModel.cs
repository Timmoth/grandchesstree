using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GrandChessTree.Api.D10Search;
using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Shared.Api;
using GrandChessTree.Api.Perft.PerftNodes;

namespace GrandChessTree.Api.Accounts
{

    [Table("accounts")]
    public class AccountModel
    {
        [Key]
        [JsonPropertyName("id")]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-generation
        public long Id { get; set; }

        [JsonPropertyName("name")]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Column("role")]
        public AccountRole Role { get; set; } = AccountRole.User;

        [JsonIgnore] public ICollection<ApiKeyModel> ApiKeys { get; set; } = default!;
        [JsonIgnore] public ICollection<PerftTask> SearchTasks { get; set; } = default!;
        [JsonIgnore] public ICollection<PerftNodesTask> PerftNodesTasks { get; set; } = default!;
        [JsonIgnore] public ICollection<PerftContribution> PerftContributions { get; set; } = default!;

        #region Versioning

        [Timestamp] public uint Version { get; set; }

        #endregion
    }
}
