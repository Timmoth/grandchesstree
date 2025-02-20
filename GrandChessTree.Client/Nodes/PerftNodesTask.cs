using System.Text.Json.Serialization;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client
{

    public class RemainingNodesSubTask
    {
        [JsonPropertyName("occurrences")]
        public required int Occurrences { get; set; }

        [JsonPropertyName("fen")]
        public required string Fen { get; set; } = "";
    }

    public class CompletedNodesSubTask
    {
        [JsonPropertyName("occurrences")]
        public required int Occurrences { get; set; }

        [JsonPropertyName("results")]
        public required ulong Nodes { get; set; }
    }
    public class PerftNodesTask
    {
        [JsonPropertyName("task_id")]
        public required long TaskId { get; set; }

        [JsonPropertyName("fen")]
        public required string Fen { get; set; } = "";

        [JsonPropertyName("sub_task_depth")]
        public required int SubTaskDepth { get; set; }

        [JsonPropertyName("sub_task_count")]
        public required int SubTaskCount { get; set; }

        [JsonPropertyName("remaining_sub_tasks")]
        public required List<RemainingNodesSubTask> RemainingSubTasks { get; set; }

        [JsonPropertyName("completed_sub_tasks")]
        public List<CompletedNodesSubTask> CompletedSubTaskResults { get; set; } = new List<CompletedNodesSubTask>();

        public RemainingNodesSubTask? WorkingTask { get; set; }


        [JsonPropertyName("cached_sub_tasks")]
        public required int CachedSubTaskCount { get; set; }
        public bool IsCompleted()
        {
            return RemainingSubTasks.Count == 0 && SubTaskCount == CompletedSubTaskResults.Count;
        }

        public RemainingNodesSubTask? GetNextSubTask()
        {
            if(RemainingSubTasks.Count == 0) return null;
            WorkingTask = RemainingSubTasks[0];
            RemainingSubTasks.RemoveAt(0);
            return WorkingTask;
        }

        public bool CompleteSubTask(ulong nodes, int occurrences)
        {
            CompletedSubTaskResults.Add(new CompletedNodesSubTask()
            {
                Nodes = nodes,
                Occurrences = occurrences
            });
            WorkingTask = null;

            return true;
        }

        public PerftNodesTaskResult? ToSubmission()
        {
            if (!IsCompleted())
            {
                return null;
            }

            var request = new PerftNodesTaskResult()
            {
                PerftNodesTaskId = TaskId,
                Nodes = 0,
            };

            foreach(var result in CompletedSubTaskResults)
            {
                request.Nodes += result.Nodes * (ulong)result.Occurrences;
            }

            return request;
        }
    }
 }
