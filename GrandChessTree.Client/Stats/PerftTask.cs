using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client.Stats
{

    public class RemainingSubTask
    {
        [JsonPropertyName("occurrences")]
        public required int Occurrences { get; set; }

        [JsonPropertyName("board")]
        public required Board Fen { get; set; }

        [JsonPropertyName("wtm")]
        public required bool Wtm { get; set; }

        [JsonPropertyName("hash")]
        public required ulong Hash { get; set; }
    }

    public class CompletedSubTask
    {
        [JsonPropertyName("occurrences")]
        public required int Occurrences { get; set; }

        [JsonPropertyName("results")]
        public required ulong[] Results { get; set; }
    }
    public class PerftTask
    {
        [JsonPropertyName("perft_task_id")]
        public required long PerftTaskId { get; set; }

        [JsonPropertyName("sub_task_depth")]
        public required int SubTaskDepth { get; set; }

        [JsonPropertyName("sub_task_count")]
        public required int SubTaskCount { get; set; }

        [JsonPropertyName("remaining_sub_tasks")]
        public required List<RemainingSubTask> RemainingSubTasks { get; set; }

        [JsonPropertyName("completed_sub_tasks")]
        public List<CompletedSubTask> CompletedSubTaskResults { get; set; } = new List<CompletedSubTask>();

        public RemainingSubTask? WorkingTask { get; set; }


        [JsonPropertyName("cached_sub_tasks")]
        public required int CachedSubTaskCount { get; set; }
        public bool IsCompleted()
        {
            return RemainingSubTasks.Count == 0 && SubTaskCount == CompletedSubTaskResults.Count;
        }

        public RemainingSubTask? GetNextSubTask()
        {
            if (RemainingSubTasks.Count == 0) return null;
            WorkingTask = RemainingSubTasks[0];
            RemainingSubTasks.RemoveAt(0);
            return WorkingTask;
        }

        public bool CompleteSubTask(Summary result, int occurrences)
        {
            CompletedSubTaskResults.Add(new CompletedSubTask()
            {
                Results = [
                result.Nodes,
                result.Captures,
                result.Enpassant,
                result.Castles,
                result.Promotions,
                result.DirectCheck,
                result.SingleDiscoveredCheck,
                result.DirectDiscoveredCheck,
                result.DoubleDiscoveredCheck,
                result.DirectCheckmate,
                result.SingleDiscoveredCheckmate,
                result.DirectDiscoverdCheckmate,
                result.DoubleDiscoverdCheckmate,
            ],
                Occurrences = occurrences
            });
            WorkingTask = null;

            return true;
        }

        public ulong[]? ToSubmission()
        {
            if (!IsCompleted())
            {
                return null;
            }

            var TaskId = PerftTaskId;
            var Nodes = 0ul;
            var Captures = 0ul;
            var Enpassants = 0ul;
            var Castles = 0ul;
            var Promotions = 0ul;
            var DirectChecks = 0ul;
            var SingleDiscoveredChecks = 0ul;
            var DirectDiscoveredChecks = 0ul;
            var DoubleDiscoveredChecks = 0ul;
            var DirectMates = 0ul;
            var SingleDiscoveredMates = 0ul;
            var DirectDiscoverdMates = 0ul;
            var DoubleDiscoverdMates = 0ul;

            foreach (var result in CompletedSubTaskResults)
            {
                var results = result.Results;
                var occurrences = result.Occurrences;
                if (results.Length != 13)
                {
                    return null;
                }

                Nodes += results[0] * (ulong)occurrences;
                Captures += results[1] * (ulong)occurrences;
                Enpassants += results[2] * (ulong)occurrences;
                Castles += results[3] * (ulong)occurrences;
                Promotions += results[4] * (ulong)occurrences;
                DirectChecks += results[5] * (ulong)occurrences;
                SingleDiscoveredChecks += results[6] * (ulong)occurrences;
                DirectDiscoveredChecks += results[7] * (ulong)occurrences;
                DoubleDiscoveredChecks += results[8] * (ulong)occurrences;
                DirectMates += results[9] * (ulong)occurrences;
                SingleDiscoveredMates += results[10] * (ulong)occurrences;
                DirectDiscoverdMates += results[11] * (ulong)occurrences;
                DoubleDiscoverdMates += results[12] * (ulong)occurrences;
            }

            return PerftFullTaskResultDecompressed.Compress(TaskId, 
                Nodes, Captures, Enpassants, Castles, Promotions, 
                DirectChecks, SingleDiscoveredChecks, DirectDiscoveredChecks, DoubleDiscoveredChecks, 
                DirectMates, SingleDiscoveredMates, DirectDiscoverdMates, DoubleDiscoverdMates);
        }
    }
}
