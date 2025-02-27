using System.Collections.Concurrent;
using System.Net.Http.Json;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Client.Stats
{
    public class NodesTaskOrchistrator
    {
        private readonly HttpClient _httpClient;
        private readonly Config _config;
        private readonly PerftNodesTaskQueue _perftTaskQueue = new PerftNodesTaskQueue();
        public int TaskQueueLength => _perftTaskQueue.Count();

        public NodesTaskOrchistrator(Config config)
        {
            _config = config;
            _httpClient = new HttpClient() { BaseAddress = new Uri(config.ApiUrl) };
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
            SubTaskHashTable = new NodesSubTaskHashTable(_config.SubTaskCacheSize);
        }

        public int Submitted { get; set; }
        public int PendingSubmission => _completedResults.Count;

        public readonly NodesSubTaskHashTable SubTaskHashTable;

        public PerftNodesTask? GetNextTask()
        {
            var task = _perftTaskQueue.Dequeue();

            if (task == null)
            {
                return null;
            }

            var (board, wtm) = BoardStateSerialization.Deserialize(task.Board);

            PerftNodesTask searchTask;
            if (task.LaunchDepth < 5)
            {
                searchTask = new PerftNodesTask()
                {
                    TaskId = task.TaskId,
                    SubTaskDepth = task.LaunchDepth,
                    SubTaskCount = 1,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingNodesSubTask>()
                };

                searchTask.RemainingSubTasks.Add(new RemainingNodesSubTask()
                {
                    Fen = board,
                    Wtm = wtm,
                    Hash = Zobrist.CalculateZobristKeyWithoutInvalidEp(ref board, wtm),
                    Occurrences = 1
                });
            }
            else
            {
                var subTaskSplitDepth = 2;
                UniqueLeafNodeGenerator.PerftRootUniqueLeafNodes(ref board, subTaskSplitDepth, wtm);
                var leafNodeWhiteToMove = subTaskSplitDepth % 2 == 0 ? wtm : !wtm;
                var subTasks = UniqueLeafNodeGenerator.boards;

                searchTask = new PerftNodesTask()
                {
                    TaskId = task.TaskId,
                    SubTaskDepth = task.LaunchDepth - subTaskSplitDepth,
                    SubTaskCount = subTasks.Count,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingNodesSubTask>()
                };

                foreach (var kvp in subTasks)
                {
                    searchTask.RemainingSubTasks.Add(new RemainingNodesSubTask()
                    {
                        Fen = kvp.Value.board,
                        Wtm = leafNodeWhiteToMove,
                        Hash = kvp.Key,
                        Occurrences = kvp.Value.occurrences
                    });
                }
            }


            return searchTask;
        }

        private readonly ConcurrentQueue<PerftFastTaskResult> _completedResults = new();
        public void Submit(PerftFastTaskResult results)
        {
            _completedResults.Enqueue(results);
        }

        // Ensures only one thread loads new tasks
        public async Task<bool> TryLoadNewTasks()
        {
            if (_perftTaskQueue.Count() >= (2*_config.Workers))
            {
                await Task.Delay(100);
                return false;
            }

            var tasks = await RequestNewTask(_httpClient);
            if (tasks == null || tasks.Length == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return false; // No new tasks available
            }

            _perftTaskQueue.Enqueue(tasks);

            return true;
        }

        private static async Task<PerftFastTaskResponse[]?> RequestNewTask(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync($"api/v3/perft/fast/tasks", null);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync(jsonTypeInfo: SourceGenerationContext.Default.PerftFastTaskResponseArray);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.Error.WriteLine("No available tasks at the moment.");
                await Task.Delay(TimeSpan.FromSeconds(10));
                return null;
            }

            Console.Error.WriteLine($"Failed to request new task. Status Code: {response.StatusCode}");
            return null;
        }

        public async Task<bool> SubmitToApi()
        {
            var results = new List<PerftFastTaskResult>();
            while (_completedResults.Any() && results.Count < 200)
            {
                if (_completedResults.TryDequeue(out var res))
                {
                    results.Add(res);
                }
            }

            Submitted += results.Count();

            if (results.Count == 0)
            {
                return false;
            }

            var response = await _httpClient.PostAsJsonAsync($"api/v3/perft/fast/results", new PerftFastTaskResultBatch { WorkerId = _config.WorkerId, Results = [.. results] }, SourceGenerationContext.Default.PerftFastTaskResultBatch);

            if (!response.IsSuccessStatusCode)
            {
                // Push back into completed task queue
                foreach (var result in results)
                {
                    _completedResults.Enqueue(result);
                }
                await Task.Delay(TimeSpan.FromSeconds(10));
                return false;
            }

            await Task.Delay(100);
            return true;
        }

        public void CacheCompletedSubtask(ulong hash, int depth, ulong nodes)
        {
            SubTaskHashTable.Add(hash, depth, nodes);
        }
    }
}
