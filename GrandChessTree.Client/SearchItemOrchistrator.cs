using System.Collections.Concurrent;
using System.Net.Http.Json;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client
{
    public class SearchItemOrchistrator
    {
        private readonly HttpClient _httpClient;
        private readonly Config _config;
        private readonly PerftTaskQueue _perftTaskQueue = new PerftTaskQueue();

        private readonly Dictionary<long, PerftTask> _restoredTasks = new Dictionary<long, PerftTask>();

        public SearchItemOrchistrator(Config config)
        {
            _config = config;
            _httpClient = new HttpClient() { BaseAddress = new Uri(config.ApiUrl) };
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);

            var restoredTasks = WorkerPersistence.LoadPartiallyCompletedTasks();
            if (restoredTasks != null)
            {
                foreach (var task in restoredTasks)
                {
                    _restoredTasks.Add(task.PerftTaskId, task);
                }
            }
        }

        public int Submitted { get; set; }
        public int PendingSubmission => _completedResults.Count;

        public readonly SubTaskHashTable SubTaskHashTable = new SubTaskHashTable(1_000_000);

        public PerftTask? GetNextTask()
        {
            var task = _perftTaskQueue.Dequeue();

            if (task == null)
            {
                return null;
            }

            if (_restoredTasks.TryGetValue(task.PerftTaskId, out var restoredTask))
            {
                return restoredTask;
            }

            var (initialBoard, initialWhiteToMove) = FenParser.Parse(task.PerftItemFen);

            PerftTask searchTask;
            if (task.LaunchDepth <= 5)
            {
                searchTask = new PerftTask()
                {
                    PerftTaskId = task.PerftTaskId,
                    PerftItemHash = task.PerftItemHash,
                    SubTaskDepth = task.LaunchDepth,
                    SubTaskCount = 1,
                    Fen = task.PerftItemFen,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingSubTask>()
                };

                searchTask.RemainingSubTasks.Add(new RemainingSubTask()
                {
                    Fen = task.PerftItemFen,
                    Occurrences = 1
                });
            }
            else
            {

                var subTaskSplitDepth = 2;
                var subTasks = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, subTaskSplitDepth, initialWhiteToMove);

                searchTask = new PerftTask()
                {
                    PerftTaskId = task.PerftTaskId,
                    PerftItemHash = task.PerftItemHash,
                    SubTaskDepth = task.LaunchDepth - subTaskSplitDepth,
                    SubTaskCount = subTasks.Count,
                    Fen = task.PerftItemFen,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingSubTask>()
                };

                foreach (var (hash, fen, occurences) in subTasks)
                {
                    if (SubTaskHashTable.TryGetValue(hash, out var summary))
                    {
                        searchTask.CompleteSubTask(summary, occurences);
                        searchTask.CachedSubTaskCount++;
                    }
                    else
                    {
                        searchTask.RemainingSubTasks.Add(new RemainingSubTask()
                        {
                            Fen = fen,
                            Occurrences = occurences
                        });
                    }
                }
            }


            return searchTask;
        }

        private readonly ConcurrentQueue<PerftTaskResult> _completedResults = new();
        public void Submit(PerftTaskResult results)
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

        private static async Task<PerftTaskResponse[]?> RequestNewTask(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync($"api/v2/perft/tasks", null);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync(jsonTypeInfo: SourceGenerationContext.Default.PerftTaskResponseArray);
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
            var results = new List<PerftTaskResult>();
            while(_completedResults.Any() && results.Count < 200)
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

            var response = await _httpClient.PostAsJsonAsync($"api/v2/perft/results", new PerftTaskResultBatch{WorkerId = _config.WorkerId, Results = [.. results] }, SourceGenerationContext.Default.PerftTaskResultBatch);

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
                _perftTaskQueue.MarkCompleted(results.Select(r => r.PerftTaskId));
            }

            await Task.Delay(100);
            return true;
        }

        public void CacheCompletedSubtask(ulong hash, Summary summary)
        {
            SubTaskHashTable.Add(hash, summary);
        }
    }
 }
