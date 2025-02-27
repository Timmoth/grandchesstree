using System.Collections.Concurrent;
using System.Net.Http.Json;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Api;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Client.Stats
{
    public class SearchItemOrchistrator
    {
        private readonly HttpClient _httpClient;
        private readonly Config _config;
        private readonly PerftTaskQueue _perftTaskQueue = new PerftTaskQueue();

        public SearchItemOrchistrator(Config config)
        {
            _config = config;
            _httpClient = new HttpClient() { BaseAddress = new Uri(config.ApiUrl) };
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
            SubTaskHashTable = new SubTaskHashTable(_config.SubTaskCacheSize);
        }

        public int Submitted { get; set; }
        public int PendingSubmission => _completedResults.Count;

        public readonly SubTaskHashTable SubTaskHashTable;

        public PerftTask? GetNextTask()
        {
            var task = _perftTaskQueue.Dequeue();

            if (task == null)
            {
                return null;
            }

            var (board, wtm) = BoardStateSerialization.Deserialize(task.Board);
            var fen = board.ToFen(wtm, 0, 1);

            PerftTask searchTask;
            if (task.LaunchDepth < 5)
            {
                searchTask = new PerftTask()
                {
                    PerftTaskId = task.TaskId,
                    SubTaskDepth = task.LaunchDepth,
                    SubTaskCount = 1,
                    Fen = fen,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingSubTask>()
                };

                searchTask.RemainingSubTasks.Add(new RemainingSubTask()
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
                var subTasks = SubTaskGenerator.GenerateLeafNodes(ref board, subTaskSplitDepth, wtm);
                var leafNodeWhiteToMove = subTaskSplitDepth % 2 == 0 ? wtm : !wtm;

                searchTask = new PerftTask()
                {
                    PerftTaskId = task.TaskId,
                    SubTaskDepth = task.LaunchDepth - subTaskSplitDepth,
                    SubTaskCount = subTasks.Count,
                    Fen = fen,
                    CachedSubTaskCount = 0,
                    RemainingSubTasks = new List<RemainingSubTask>()
                };

                foreach (var kvp in subTasks)
                {
                    if (SubTaskHashTable.TryGetValue(kvp.Key, searchTask.SubTaskDepth, out var summary))
                    {
                        searchTask.CompleteSubTask(summary, kvp.Value.occurrences);
                        searchTask.CachedSubTaskCount++;
                    }
                    else
                    {
                        searchTask.RemainingSubTasks.Add(new RemainingSubTask()
                        {
                            Fen = kvp.Value.board,
                            Wtm = leafNodeWhiteToMove,
                            Hash = kvp.Key,
                            Occurrences = kvp.Value.occurrences
                        });
                    }
                }
            }


            return searchTask;
        }

        private readonly ConcurrentQueue<PerftFullTaskResult> _completedResults = new();
        public void Submit(PerftFullTaskResult results)
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

        private static async Task<PerftFullTaskResponse[]?> RequestNewTask(HttpClient httpClient)
        {
            var response = await httpClient.PostAsync($"api/v3/perft/full/tasks", null);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync(jsonTypeInfo: SourceGenerationContext.Default.PerftFullTaskResponseArray);
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
            var results = new List<PerftFullTaskResult>();
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

            var response = await _httpClient.PostAsJsonAsync($"api/v3/perft/full/results", new PerftFullTaskResultBatch { WorkerId = _config.WorkerId, Results = [.. results] }, SourceGenerationContext.Default.PerftFullTaskResultBatch);

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

        public void CacheCompletedSubtask(ulong hash, int depth, Summary summary)
        {
            SubTaskHashTable.Add(hash, depth, summary);
        }
    }
}
