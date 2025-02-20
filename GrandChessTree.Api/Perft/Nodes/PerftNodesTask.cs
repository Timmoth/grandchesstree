using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.Accounts;

namespace GrandChessTree.Api.Perft.PerftNodes
{

    [Table("perft_nodes_tasks")]
    public class PerftNodesTask
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-generation
        public long Id { get; set; }

        [Column("hash")]
        public ulong Hash { get; set; }

        [Column("fen")]
        public string Fen { get; set; } = "";

        [Column("depth")]
        public required int Depth { get; set; }

        [Column("launch_depth")]
        public required int LaunchDepth { get; set; } = 0;

        [Column("available_at")]
        public long AvailableAt { get; set; } = 0;

        [Column("occurrences")]
        public required int Occurrences { get; set; }

        [Column("root_position_id")]
        public required int RootPositionId { get; set; } = 0;

        [Column("worker_id")]
        public int WorkerId { get; set; } = 0;

        [Column("started_at")]
        public long StartedAt { get; set; } = 0;

        [Column("finished_at")]
        public long FinishedAt { get; set; } = 0;

        [Column("nps")]
        public float Nps { get; set; }

        [Column("nodes")]
        public ulong Nodes { get; set; }

        [JsonPropertyName("account_id")]
        [ForeignKey("Account")]
        [Column("account_id")]
        public required long? AccountId { get; set; }

        [JsonIgnore] public AccountModel? Account { get; set; } = default!;
    }
}
