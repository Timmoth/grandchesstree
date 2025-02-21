using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GrandChessTree.Api.D10Search
{
    [Table("perft_items")]
    public class PerftItem
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-generation
        public long Id { get; set; }

        [Column("hash")]
        public ulong Hash { get; set; }

        [Column("fen")]
        public string Fen { get; set; } = "";

        [Column("root_position_id")]
        public int RootPositionId { get; set; } = 0;

        [Column("depth")]
        public int Depth { get; set; } = 0;

        [Column("launch_depth")]
        public int LaunchDepth { get; set; } = 0;

        [Column("available_at")]
        public long AvailableAt { get; set; } = 0;

        [Column("pass_count")]
        public int PassCount { get; set; } = 0;

        [Column("confirmed")]
        public bool Confirmed { get; set; }

        [Column("occurrences")]
        public int Occurrences { get; set; }

        public virtual List<PerftTask> SearchTasks { get; set; } = new();
    }
}
