using System.Collections.Concurrent;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client.Stats
{
    public class PerftTaskQueue
    {
        private readonly ConcurrentQueue<PerftFullTaskResponse> _taskQueue = new();

        public PerftTaskQueue()
        {

        }

        public void Enqueue(PerftFullTaskResponse[] tasks)
        {
            foreach (var newTask in tasks)
            {
                _taskQueue.Enqueue(newTask);
            }
        }

        public PerftFullTaskResponse? Dequeue()
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
    }
}
