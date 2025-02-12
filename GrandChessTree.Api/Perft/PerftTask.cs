using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GrandChessTree.Api.Accounts;

namespace GrandChessTree.Api.D10Search
{

    [Table("perft_tasks")]
    public class PerftTask
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-generation
        public long Id { get; set; }

        [Column("depth")]
        public required int Depth { get; set; }

        [Column("root_position_id")]
        public int RootPositionId { get; set; } = 0;

        [Column("worker_id")]
        public int WorkerId { get; set; } = 0;

        [ForeignKey("PerftItem")]
        [Column("perft_item_id")]
        public required long PerftItemId { get; set; }

        public virtual PerftItem PerftItem { get; set; } = null!;

        [Column("started_at")]
        public long StartedAt { get; set; } = 0;

        [Column("finished_at")]
        public long FinishedAt { get; set; } = 0;

        [Column("nps")]
        public float Nps { get; set; }
        [Column("nodes")]
        public ulong Nodes { get; set; }
        [Column("captures")]
        public ulong Captures { get; set; }
        [Column("enpassants")]
        public ulong Enpassant { get; set; }
        [Column("castles")]
        public ulong Castles { get; set; }
        [Column("promotions")]
        public ulong Promotions { get; set; }
        [Column("direct_checks")]
        public ulong DirectCheck { get; set; }
        [Column("single_discovered_check")]
        public ulong SingleDiscoveredCheck { get; set; }
        [Column("direct_discovered_check")]
        public ulong DirectDiscoveredCheck { get; set; }
        [Column("double_discovered_check")]
        public ulong DoubleDiscoveredCheck { get; set; }
        [Column("direct_checkmate")]
        public ulong DirectCheckmate { get; set; }
        [Column("single_discovered_checkmate")]
        public ulong SingleDiscoveredCheckmate { get; set; }
        [Column("direct_discoverd_checkmate")]
        public ulong DirectDiscoverdCheckmate { get; set; }
        [Column("double_discoverd_checkmate")]
        public ulong DoubleDiscoverdCheckmate { get; set; }

        [JsonPropertyName("account_id")]
        [ForeignKey("Account")]
        [Column("account_id")]
        public required long? AccountId { get; set; }

        [JsonIgnore] public AccountModel? Account { get; set; } = default!;
    }
}
