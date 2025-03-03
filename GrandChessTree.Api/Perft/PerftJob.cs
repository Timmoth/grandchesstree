using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GrandChessTree.Api.Perft.V3;

namespace GrandChessTree.Api.D10Search
{

    [Table("perft_jobs")]
    public class PerftJob
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-generation
        public long Id { get; set; }

        [Column("depth")]
        public required int Depth { get; set; }

        [Column("root_position_id")]
        public required int RootPositionId { get; set; }

        [Column("launch_depth")]
        public required int LaunchDepth { get; set; }

        [Column("total_tasks")]
        public required long TotalTasks { get; set; }

        [Column("verified_tasks")]
        public required long VerifiedTasks { get; set; }

        [Column("completed_full_tasks")]
        public required long CompletedFullTasks { get; set; }

        [Column("completed_fast_tasks")]
        public required long CompletedFastTasks { get; set; }

        [Column("fast_task_nodes")]
        public required decimal FastTaskNodes { get; set; }

        [Column("full_task_nodes")]
        public required decimal FullTaskNodes { get; set; }

        
        public void Add(IEnumerable<TaskUpdate> updates)
        {
            foreach (var update in updates)
            {
                if (update.TaskType == timescale.PerftTaskType.Fast)
                {
                    CompletedFastTasks++;
                    FastTaskNodes += update.ComputedNodes;
                }
                else
                {
                    CompletedFullTasks++;
                    FullTaskNodes += update.ComputedNodes;
                }

                if (update.IsVerified)
                {
                    VerifiedTasks++;
                }
            }
        }
    }
}
