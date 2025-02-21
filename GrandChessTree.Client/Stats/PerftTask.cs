using System.Text.Json.Serialization;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client.Stats
{

    public class RemainingSubTask
    {
        [JsonPropertyName("occurrences")]
        public required int Occurrences { get; set; }

        [JsonPropertyName("fen")]
        public required string Fen { get; set; } = "";
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

        [JsonPropertyName("fen")]
        public required string Fen { get; set; } = "";

        [JsonPropertyName("perft_item_hash")]
        public required ulong PerftItemHash { get; set; }

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

        public PerftTaskResult? ToSubmission()
        {
            if (!IsCompleted())
            {
                return null;
            }

            var request = new PerftTaskResult()
            {
                PerftTaskId = PerftTaskId,
                PerftItemHash = PerftItemHash,
                Nodes = 0,
                Captures = 0,
                Enpassant = 0,
                Castles = 0,
                Promotions = 0,
                DirectCheck = 0,
                SingleDiscoveredCheck = 0,
                DirectDiscoveredCheck = 0,
                DoubleDiscoveredCheck = 0,
                DirectCheckmate = 0,
                SingleDiscoveredCheckmate = 0,
                DirectDiscoverdCheckmate = 0,
                DoubleDiscoverdCheckmate = 0,
            };

            foreach (var result in CompletedSubTaskResults)
            {
                var results = result.Results;
                var occurrences = result.Occurrences;
                if (results.Length != 13)
                {
                    return null;
                }

                request.Nodes += results[0] * (ulong)occurrences;
                request.Captures += results[1] * (ulong)occurrences;
                request.Enpassant += results[2] * (ulong)occurrences;
                request.Castles += results[3] * (ulong)occurrences;
                request.Promotions += results[4] * (ulong)occurrences;
                request.DirectCheck += results[5] * (ulong)occurrences;
                request.SingleDiscoveredCheck += results[6] * (ulong)occurrences;
                request.DirectDiscoveredCheck += results[7] * (ulong)occurrences;
                request.DoubleDiscoveredCheck += results[8] * (ulong)occurrences;
                request.DirectCheckmate += results[9] * (ulong)occurrences;
                request.SingleDiscoveredCheckmate += results[10] * (ulong)occurrences;
                request.DirectDiscoverdCheckmate += results[11] * (ulong)occurrences;
                request.DoubleDiscoverdCheckmate += results[12] * (ulong)occurrences;
            }

            return request;
        }
    }
}
