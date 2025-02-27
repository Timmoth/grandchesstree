using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GrandChessTree.Api.Accounts;
using System.Text.Json.Serialization;
using GrandChessTree.Shared.Api;
using System.Diagnostics.Metrics;

namespace GrandChessTree.Api.D10Search
{
    [Table("perft_tasks_v3")]
    public class PerftTaskV3
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-generation
        public long Id { get; set; }

        [Column("board")]
        public string Board { get; set; } = "";

        [Column("root_position_id")]
        public int RootPositionId { get; set; } = 0;

        [Column("depth")]
        public int Depth { get; set; } = 0;

        [Column("launch_depth")]
        public int LaunchDepth { get; set; } = 0;

        [Column("occurrences")]
        public int Occurrences { get; set; }


        #region FullTask

        [Column("full_task_worker_id")]
        public int FullTaskWorkerId { get; set; } = 0;

        [Column("full_task_started_at")]
        public long FullTaskStartedAt { get; set; } = 0;

        [Column("full_task_finished_at")]
        public long FullTaskFinishedAt { get; set; } = 0;

        [Column("full_task_nps")]
        public float FullTaskNps { get; set; }
        [Column("full_task_nodes")]
        public ulong FullTaskNodes { get; set; }
        [Column("captures")]
        public ulong Captures { get; set; }
        [Column("enpassants")]
        public ulong Enpassants { get; set; }
        [Column("castles")]
        public ulong Castles { get; set; }
        [Column("promotions")]
        public ulong Promotions { get; set; }
        [Column("direct_checks")]
        public ulong DirectChecks { get; set; }
        [Column("single_discovered_checks")]
        public ulong SingleDiscoveredChecks { get; set; }
        [Column("direct_discovered_checks")]
        public ulong DirectDiscoveredChecks { get; set; }
        [Column("double_discovered_checks")]
        public ulong DoubleDiscoveredChecks { get; set; }
        [Column("direct_mates")]
        public ulong DirectMates { get; set; }
        [Column("single_discovered_mates")]
        public ulong SingleDiscoveredMates { get; set; }
        [Column("direct_discovered_mates")]
        public ulong DirectDiscoveredMates { get; set; }
        [Column("double_discovered_mates")]
        public ulong DoubleDiscoveredMates { get; set; }

        [JsonPropertyName("full_task_account_id")]
        [ForeignKey("FullTaskAccount")]
        [Column("full_task_account_id")]
        public required long? FullTaskAccountId { get; set; }

        [JsonIgnore] public AccountModel? FullTaskAccount { get; set; } = default!;

        public void StartFullTask(long currentTimeStamp, long accountId)
        {
            FullTaskStartedAt = currentTimeStamp;
            FullTaskAccountId = accountId;
        }

        public void FinishFullTask(
            long currentTimeStamp,
            int workerId,
            PerftFullTaskResult result)
        {
            // Update the search item (parent)
            FullTaskFinishedAt = currentTimeStamp;
            FullTaskWorkerId = workerId;

            var finishedAt = currentTimeStamp == FullTaskStartedAt ? currentTimeStamp + 1 : currentTimeStamp;

            // Update search task properties
            var duration = (ulong)(finishedAt - FullTaskStartedAt);
            if (duration > 0)
            {
                FullTaskNps = result.Nodes * (ulong)Occurrences / duration;
            }
            else
            {
                FullTaskNps = result.Nodes * (ulong)Occurrences;
            }

            FullTaskNps = finishedAt;
            FullTaskNodes = result.Nodes;

            FullTaskFinishedAt = finishedAt;
            FullTaskNodes = result.Nodes;
            Captures = result.Captures;
            Enpassants = result.Enpassants;
            Castles = result.Castles;
            Promotions = result.Promotions;
            DirectChecks = result.DirectChecks;
            SingleDiscoveredChecks = result.SingleDiscoveredChecks;
            DirectDiscoveredChecks = result.DirectDiscoveredChecks;
            DoubleDiscoveredChecks = result.DoubleDiscoveredChecks;
            DirectMates = result.DirectMates;
            SingleDiscoveredMates = result.SingleDiscoveredMates;
            DirectDiscoveredMates = result.DirectDiscoverdMates;
            DoubleDiscoveredMates = result.DoubleDiscoverdMates;

        }


        #endregion

        #region Fast Task

        [Column("fast_task_worker_id")]
        public int FastTaskWorkerId { get; set; } = 0;

        [Column("fast_task_started_at")]
        public long FastTaskStartedAt { get; set; } = 0;

        [Column("fast_task_finished_at")]
        public long FastTaskFinishedAt { get; set; } = 0;

        [Column("fast_task_nps")]
        public float FastTaskNps { get; set; }
        [Column("fast_task_nodes")]
        public ulong FastTaskNodes { get; set; }
   
        [JsonPropertyName("fast_task_account_id")]
        [ForeignKey("FastTaskAccount")]
        [Column("fast_task_account_id")]
        public required long? FastTaskAccountId { get; set; }

        [JsonIgnore] public AccountModel? FastTaskAccount { get; set; } = default!;

        public void StartFastTask(long currentTimeStamp, long accountId)
        {
            FastTaskStartedAt = currentTimeStamp;
            FastTaskAccountId = accountId;
        }

        public void FinishFastTask(
            long currentTimeStamp, 
            int workerId,
            PerftFastTaskResult result)
        {
            // Update the search item (parent)
            FastTaskFinishedAt = currentTimeStamp;
            FastTaskWorkerId = workerId;

            var finishedAt = currentTimeStamp == FastTaskStartedAt ? currentTimeStamp + 1 : currentTimeStamp;

            // Update search task properties
            var duration = (ulong)(finishedAt - FastTaskStartedAt);
            if (duration > 0)
            {
                FastTaskNps = result.Nodes * (ulong)Occurrences / duration;
            }
            else
            {
                FastTaskNps = result.Nodes * (ulong)Occurrences;
            }

            FastTaskNps = finishedAt;
            FastTaskNodes = result.Nodes;
        }

        internal object[] ToFullTaskReading()
        {
            return new object[]
            {
                DateTimeOffset.FromUnixTimeSeconds(FullTaskFinishedAt),
                FullTaskAccountId ?? 0,
                (short)FullTaskWorkerId,
                (long)FullTaskNodes,
                (short)Occurrences,
                FullTaskFinishedAt - FullTaskStartedAt,
                (short)Depth,
                (short)RootPositionId,
                (short)0,
            };
        }

        internal object[] ToFastTaskReading()
        {
            return new object[]
            {
                DateTimeOffset.FromUnixTimeSeconds(FastTaskFinishedAt),
                FastTaskAccountId ?? 0,
                (short)FastTaskWorkerId,
                (long)FastTaskNodes,
                (short)Occurrences,
                FastTaskFinishedAt - FastTaskStartedAt,
                (short)Depth,
                (short)RootPositionId,
                (short)1,
            };
        }

        #endregion
    }
}
