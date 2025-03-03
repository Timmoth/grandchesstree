using System.Threading.Tasks;
using System.Xml.Linq;

namespace GrandChessTree.Client.Stats
{
    public class NodesWorkerReport
    {
        public int TotalSubtasks { get; set; }
        public int CompletedSubtasks { get; set; }
        public float Nps { get; set; }
        public int TotalCompletedTasks { get; set; }
        public int TotalCompletedSubTasks { get; set; }
        public int TotalCachedSubTasks { get; set; }
        public long WorkerCpuTime { get; set; }
        public ulong WorkerComputedNodes { get; set; }
        public ulong TotalComputedNodes { get; set; }
        public ulong TotalNodes { get; set; }

        public bool IsRunning { get; set; }

        public void BeginTask(PerftNodesTask task)
        {
            TotalSubtasks = task.SubTaskCount;
            CompletedSubtasks = task.CompletedSubTaskResults.Count;
            TotalCompletedSubTasks += task.CachedSubTaskCount;
            TotalCachedSubTasks += task.CachedSubTaskCount;
            TotalNodes += (ulong)task.CompletedSubTaskResults.Sum(t => (float)t.Nodes * t.Occurrences);
            WorkerComputedNodes = 0;
        }

        public void BeginSubTask(PerftNodesTask task)
        {
            TotalSubtasks = task.SubTaskCount;
            CompletedSubtasks = task.CompletedSubTaskResults.Count;
        }

        public void EndSubTaskWorkCompleted(PerftNodesTask task, ulong nodes, int subTaskOccurrences)
        {
            TotalSubtasks = task.SubTaskCount;
            CompletedSubtasks = task.CompletedSubTaskResults.Count;
            TotalCompletedSubTasks++;
            TotalComputedNodes += nodes;
            WorkerComputedNodes += nodes;
        }

        public void EndSubTaskFoundInCache(PerftNodesTask task, ulong nodes, int subTaskOccurrences)
        {
            TotalSubtasks = task.SubTaskCount;
            CompletedSubtasks = task.CompletedSubTaskResults.Count;
            TotalCompletedSubTasks++;
            TotalCachedSubTasks++;
        }

        public void CompleteTask(PerftNodesTask task, long duration)
        {
            TotalSubtasks = task.SubTaskCount;
            CompletedSubtasks = task.CompletedSubTaskResults.Count;
            TotalCompletedTasks++;
            WorkerCpuTime += duration;
        }

        internal void ResetStats()
        {
            TotalSubtasks = 0;
            CompletedSubtasks = 0;
            Nps = 0;
            TotalCompletedTasks = 0;
            TotalCompletedSubTasks = 0;
            TotalCachedSubTasks = 0;
            WorkerCpuTime = 0; 
            WorkerComputedNodes = 0;
            TotalComputedNodes = 0;
            TotalNodes = 0;
        }
    }
}
