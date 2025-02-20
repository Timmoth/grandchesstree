using System.Collections.Concurrent;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client.Stats
{
    public class PerftTaskQueue
    {
        private readonly ConcurrentQueue<PerftTaskResponse> _taskQueue = new();
        private readonly ConcurrentDictionary<long, PerftTaskResponse> _pendingTasks = new();

        public PerftTaskQueue()
        {

            var tasks = WorkerPersistence.LoadPendingTasks();
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    _taskQueue.Enqueue(task);
                }
            }
        }

        public void Enqueue(PerftTaskResponse[] tasks)
        {
            foreach (var newTask in tasks)
            {
                _taskQueue.Enqueue(newTask);
                _pendingTasks.TryAdd(newTask.PerftTaskId, newTask);
            }

            WorkerPersistence.SavePendingTasks(_pendingTasks.Values.ToArray());
        }

        public PerftTaskResponse? Dequeue()
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

            WorkerPersistence.SavePendingTasks(_pendingTasks.Values.ToArray());
        }
    }
}
