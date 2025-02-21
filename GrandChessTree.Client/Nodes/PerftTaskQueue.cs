using System.Collections.Concurrent;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client.Stats
{
    public class PerftNodesTaskQueue
    {
        private readonly ConcurrentQueue<PerftNodesTaskResponse> _taskQueue = new();
        private readonly ConcurrentDictionary<long, PerftNodesTaskResponse> _pendingTasks = new();

        public PerftNodesTaskQueue()
        {

            var tasks = WorkerPersistence.LoadPendingNodesTasks();
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    _taskQueue.Enqueue(task);
                }
            }
        }

        public void Enqueue(PerftNodesTaskResponse[] tasks)
        {
            foreach (var newTask in tasks)
            {
                _taskQueue.Enqueue(newTask);
                _pendingTasks.TryAdd(newTask.TaskId, newTask);
            }

            WorkerPersistence.SavePendingNodesTasks(_pendingTasks.Values.ToArray());
        }

        public PerftNodesTaskResponse? Dequeue()
        {
            if (!_taskQueue.TryDequeue(out var task))
            {
                return null;
            }

            return task;
        }

        public int Count()
        {
            return _taskQueue.Count;
        }

        public void MarkCompleted(IEnumerable<long> taskIds)
        {
            foreach (var taskId in taskIds)
            {
                _pendingTasks.TryRemove(taskId, out _);
            }

            WorkerPersistence.SavePendingNodesTasks(_pendingTasks.Values.ToArray());
        }
    }
}
