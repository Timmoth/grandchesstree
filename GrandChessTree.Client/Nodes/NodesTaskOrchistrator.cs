using System.Collections.Concurrent;
using System.Net.Http.Json;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Stats
{
    public class NodesTaskOrchistrator
    {
        private readonly HttpClient _httpClient;
        private readonly Config _config;
        private readonly PerftNodesTaskQueue _perftTaskQueue = new PerftNodesTaskQueue();

        private readonly Dictionary<long, PerftNodesTask> _restoredTasks = new Dictionary<long, PerftNodesTask>();

        public NodesTaskOrchistrator(Config config)
        {
            _config = config;
            _httpClient = new HttpClient() { BaseAddress = new Uri(config.ApiUrl) };
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);

            var restoredTasks = WorkerPersistence.LoadPartiallyCompletedNodesTasks();
            if (restoredTasks != null)
            {
                foreach (var task in restoredTasks)
                {
                    _restoredTasks.Add(task.TaskId, task);
                }
            }
        }

        public int Submitted { get; set; }
        public int PendingSubmission => _completedResults.Count;

        public readonly NodesSubTaskHashTable SubTaskHashTable = new NodesSubTaskHashTable(1_000_000);

        public PerftNodesTask? GetNextTask()
        {
            var task = _perftTaskQueue.Dequeue();

            if (task == null)
            {
                return null;
            }

            if (_restoredTasks.TryGetValue(task.TaskId, out var restoredTask))
            {
                return restoredTask;
            }

            var (initialBoard, initialWhiteToMove) = FenParser.Parse(task.Fen);

            PerftNodesTask searchTask;
            if (task.LaunchDepth <= 5)
            {
                searchTask = new PerftNodesTask()
                {
                    TaskId = task.TaskId,
                    SubTaskDepth = task.LaunchDepth,
                    SubTaskCount = 1,
                    Fen = task.Fen,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingNodesSubTask>()
                };

                searchTask.RemainingSubTasks.Add(new RemainingNodesSubTask()
                {
                    Fen = task.Fen,
                    Occurrences = 1
                });
            }
            else
            {

                var subTaskSplitDepth = 2;
                var subTasks = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, subTaskSplitDepth, initialWhiteToMove);

                searchTask = new PerftNodesTask()
                {
                    TaskId = task.TaskId,
                    SubTaskDepth = task.LaunchDepth - subTaskSplitDepth,
                    SubTaskCount = subTasks.Count,
                    Fen = task.Fen,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingNodesSubTask>()
                };

                foreach (var (hash, fen, occurences) in subTasks)
                {
                    if (SubTaskHashTable.TryGetValue(fen, searchTask.SubTaskDepth, out var summary))
                    {
                        searchTask.CompleteSubTask(summary, occurences);
                        searchTask.CachedSubTaskCount++;
                    }
                    else
                    {
                        searchTask.RemainingSubTasks.Add(new RemainingNodesSubTask()
                        {
                            Fen = fen,
                            Occurrences = occurences
                        });
                    }
                }
            }


            return searchTask;
        }

        private readonly ConcurrentQueue<PerftNodesTaskResult> _completedResults = new();
        public void Submit(PerftNodesTaskResult results)
        {
            _completedResults.Enqueue(results);
        }

        // Ensures only one thread loads new tasks
        public async Task<bool> TryLoadNewTasks()
        {
            if (_perftTaskQueue.Count() >= _config.Workers)
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

        private static async Task<PerftNodesTaskResponse[]?> RequestNewTask(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync($"api/v2/perft/nodes/tasks", null);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync(jsonTypeInfo: SourceGenerationContext.Default.PerftNodesTaskResponseArray);
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
            var results = new List<PerftNodesTaskResult>();
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

            var response = await _httpClient.PostAsJsonAsync($"api/v2/perft/nodes/results", new PerftNodesTaskResultBatch { WorkerId = _config.WorkerId, Results = [.. results] }, SourceGenerationContext.Default.PerftNodesTaskResultBatch);

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
            else
            {
                _perftTaskQueue.MarkCompleted(results.Select(r => r.PerftNodesTaskId));
            }

            await Task.Delay(100);
            return true;
        }

        public void CacheCompletedSubtask(string fen, int depth, ulong nodes)
        {
            SubTaskHashTable.Add(fen, depth, nodes);
        }
    }
}
