using System.Text.Json.Serialization;
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

        public PerftFullTaskResult? ToSubmission()
        {
            if (!IsCompleted())
            {
                return null;
            }

            var request = new PerftFullTaskResult()
            {
                TaskId = PerftTaskId,
                Nodes = 0,
                Captures = 0,
                Enpassants = 0,
                Castles = 0,
                Promotions = 0,
                DirectChecks = 0,
                SingleDiscoveredChecks = 0,
                DirectDiscoveredChecks = 0,
                DoubleDiscoveredChecks = 0,
                DirectMates = 0,
                SingleDiscoveredMates = 0,
                DirectDiscoverdMates = 0,
                DoubleDiscoverdMates = 0,
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
                request.Enpassants += results[2] * (ulong)occurrences;
                request.Castles += results[3] * (ulong)occurrences;
                request.Promotions += results[4] * (ulong)occurrences;
                request.DirectChecks += results[5] * (ulong)occurrences;
                request.SingleDiscoveredChecks += results[6] * (ulong)occurrences;
                request.DirectDiscoveredChecks += results[7] * (ulong)occurrences;
                request.DoubleDiscoveredChecks += results[8] * (ulong)occurrences;
                request.DirectMates += results[9] * (ulong)occurrences;
                request.SingleDiscoveredMates += results[10] * (ulong)occurrences;
                request.DirectDiscoverdMates += results[11] * (ulong)occurrences;
                request.DoubleDiscoverdMates += results[12] * (ulong)occurrences;
            }

            return request;
        }
    }
}
