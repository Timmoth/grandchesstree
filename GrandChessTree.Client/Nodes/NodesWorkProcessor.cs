using ConsoleTables;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Stats
{
    public class NodesWorkProcessor
    {
        private readonly int _workerCount;
        private readonly NodesTaskOrchistrator _searchItemOrchistrator;
        private readonly Config _config;
        private bool KeepRequestingWork { get; set; } = true;

        public NodesWorkerReport[] _workerReports;

        public bool HasRunningWorkers => _workerReports.Any(w => w.IsRunning);

        private bool OutputFullDetails = false;

        public bool IsPaused = false;
        public NodesWorkProcessor(NodesTaskOrchistrator searchItemOrchistrator, Config config)
        {
            _config = config;
            _searchItemOrchistrator = searchItemOrchistrator;
            _workerCount = config.Workers;
            _workerReports = new NodesWorkerReport[_workerCount];
            for (int i = 0; i < _workerReports.Length; i++) { _workerReports[i] = new NodesWorkerReport(); }
        }

        public void Run()
        {
            Thread[] threads = new Thread[_workerCount + 3];

            for (int i = 0; i < _workerCount; i++)
            {
                var index = i;
                _workerReports[index].IsRunning = true;
                threads[index] = new Thread(() => ThreadWork(index));
                threads[index].Start();
            }

            threads[_workerCount] = new Thread(OutputStatsPeriodically);
            threads[_workerCount].Start();

            threads[_workerCount + 1] = new Thread(GetTasksPeriodically);
            threads[_workerCount + 1].Start();

            threads[_workerCount + 2] = new Thread(SubmitResultsPeriodically);
            threads[_workerCount + 2].Start();

            // Wait for all threads to complete
            foreach (Thread thread in threads)
            {
                thread.Join();
            }
        }

        public bool _resetStats = false;
        private void OutputStatsPeriodically()
        {
            ulong prevTotalNodes = 0;
            long prevTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var start = DateTimeOffset.UtcNow;
            while (HasRunningWorkers)
            {
                if (IsPaused)
                {
                    Thread.Sleep(500);
                    _resetStats = true;
                    Console.Clear();
                    Console.WriteLine("'s' + enter to start");

                    continue;
                }

                if (_resetStats)
                {
                    prevTotalNodes = 0;
                    prevTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    start = DateTimeOffset.UtcNow;

                    _searchItemOrchistrator.ResetStats();
                    foreach (var w in _workerReports)
                    {
                        w.ResetStats();
                    }
                    _resetStats = false;
                }


                try
                {
                    var sumCompletedTasks = 0;
                    ulong sumCompletedSubTasks = 0;
                    float sumNps = 0;
                    long subtaskCacheHits = 0;

                    ulong currentTotalNodes = 0;
                    ulong totalComputedNodes = 0;
                    for (int i = 0; i < _workerReports.Length; i++)
                    {
                        var report = _workerReports[i];
                        sumCompletedTasks += report.TotalCompletedTasks;
                        sumCompletedSubTasks += (ulong)report.TotalCompletedSubTasks;
                        subtaskCacheHits += report.TotalCachedSubTasks;
                        if (!float.IsNaN(report.Nps) && !float.IsInfinity(report.Nps))
                        {
                            sumNps += report.Nps;
                        }

                        currentTotalNodes += report.TotalNodes;
                        totalComputedNodes += report.TotalComputedNodes;
                    }

                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var realNps = (currentTotalNodes - prevTotalNodes) / ((currentTime - prevTime) / 1000f);

                    prevTotalNodes = currentTotalNodes;
                    prevTime = currentTime;
                    var deltaT = (currentTime - startTime);
                    var dt = DateTimeOffset.UtcNow - start;
                    var tpm = (float)sumCompletedTasks / (deltaT / 60000f);

                    Console.CursorVisible = false;
                    Console.SetCursorPosition(0, 0);
                    for (int y = 0; y < Console.WindowHeight; y++)
                        Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, 0);

                    if (OutputFullDetails)
                    {
                        var table = new ConsoleTable("worker", "nps", "nodes", "sub_tasks", "tasks");
                        for (int i = 0; i < _workerReports.Length; i++)
                        {
                            var report = _workerReports[i];
                            table.AddRow($"{i}", report.Nps.FormatBigNumber(), report.WorkerComputedNodes.FormatBigNumber(), $"{report.CompletedSubtasks}/{report.TotalSubtasks}", report.TotalCompletedTasks);
                        }
                        table.Configure((c) =>
                        {
                            c.EnableCount = false;
                        });
                        table.Write(Format.MarkDown);
                    }

                    var cachHitPercent = sumCompletedSubTasks == 0 ? 0 : (float)subtaskCacheHits / sumCompletedSubTasks * 100;

                    Console.WriteLine($"{sumCompletedSubTasks.FormatBigNumber()} subtasks ({cachHitPercent.RoundToSignificantFigures(2)}% cache hits)");
                    Console.WriteLine($"{_searchItemOrchistrator.Submitted} submitted tasks ({_searchItemOrchistrator.PendingSubmission} pending)");
                    Console.WriteLine($"{_searchItemOrchistrator.TaskQueueLength} queued tasks");
                    Console.WriteLine($"{_workerReports.Length} workers, avg {(_workerReports.Sum(w => (float)w.TotalComputedNodes) / _workerReports.Length / deltaT * 1000).FormatBigNumber()}nps");
                    Console.WriteLine($"[{totalComputedNodes.FormatBigNumber()} nodes] [{(totalComputedNodes / (float)deltaT * 1000).FormatBigNumber()}nps] [{tpm.RoundToSignificantFigures(2)}tpm]");
                    Console.WriteLine($"worker id: {_config.WorkerId} subtask cache: {NodesSubTaskHashTable.AllocatedMb}MB worker cache: {PerftBulk.AllocatedMb}MB");

                    string formattedTime = dt.TotalDays >= 1
                        ? dt.ToString(@"d\.hh\:mm\:ss")
                        : dt.ToString(@"hh\:mm\:ss");

                    Console.WriteLine($"{formattedTime}");

                    if (!KeepRequestingWork)
                    {
                        Console.WriteLine($"Will exit automatically when the current tasks are completed.");
                        Console.WriteLine($"{_workerReports.Count(w => !w.IsRunning)}/{_workerCount} ready");
                    }
                    else
                    {
                        Console.WriteLine("'q' + enter to quit");
                        Console.WriteLine("'d' + enter toggles worker details");
                        Console.WriteLine("'s' + enter to start/stop");
                        Console.WriteLine("'r' + enter to reset stats");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                Thread.Sleep(500);

            }
        }

        public void GetTasksPeriodically()
        {
            Task.Run(async () =>
            {
                while (KeepRequestingWork && HasRunningWorkers)
                {
                    try
                    {
                        await _searchItemOrchistrator.TryLoadNewTasks();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }
            });
        }

        public void SubmitResultsPeriodically()
        {
            Task.Run(async () =>
            {
                while (HasRunningWorkers)
                {
                    try
                    {
                        await _searchItemOrchistrator.SubmitToApi();
                        if (_searchItemOrchistrator.PendingSubmission > 100)
                        {
                            await Task.Delay(100);
                        }
                        else
                        {
                            await Task.Delay(5000);
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }
            });
        }

        private unsafe void ThreadWork(int index)
        {
            PerftBulk.AllocateHashTable(_config.MbHash);

            var workerReport = _workerReports[index];
            workerReport.IsRunning = true;
            while (KeepRequestingWork)
            {
                // Get the next task
                var currentTask = _searchItemOrchistrator.GetNextTask();
                if (currentTask == null)
                {
                    // No available task, wait and try again
                    Thread.Sleep(10);
                    continue;
                }

                // Report that work on this task has begun
                workerReport.BeginTask(currentTask);

                long taskStartTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                while (currentTask.RemainingSubTasks.Any())
                {
                    try
                    {
                        // Get the next sub task
                        var subTask = currentTask.GetNextSubTask();
                        if (subTask == null)
                        {
                            // No subtasks available, try again.
                            Thread.Sleep(10);
                            continue;
                        }

                        var subTaskOccurrences = subTask.Occurrences;
                        var board = subTask.Fen;
                        var whiteToMove = subTask.Wtm;
                        var hash = subTask.Hash;

                        // Report that work on this subtask has begun
                        workerReport.BeginSubTask(currentTask);

                        if (_searchItemOrchistrator.SubTaskHashTable.TryGetValue(hash, currentTask.SubTaskDepth, out var nodes))
                        {
                            // This position has been found in the global cache! Use the cached summary
                            // And report the subtask as completed
                            workerReport.EndSubTaskFoundInCache(currentTask, nodes, subTaskOccurrences);
                        }
                        else
                        {
                            long subtaskStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            
                            // Recursive perft
                            nodes = PerftBulk.PerftRootBulk(ref board, currentTask.SubTaskDepth, whiteToMove);

                            // Store the hash for this position in the global cache
                            _searchItemOrchistrator.CacheCompletedSubtask(hash, currentTask.SubTaskDepth, nodes);

                            // Report the subtask as completed
                            workerReport.EndSubTaskWorkCompleted(currentTask, nodes, subTaskOccurrences);
                            var subTaskDurationSeconds = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - subtaskStart) / 1000.0f;

                            // Calculate the NPS
                            workerReport.Nps = nodes / subTaskDurationSeconds;
                        }

                        // Complete the subtask
                        currentTask.CompleteSubTask(nodes, subTaskOccurrences);

                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error: {ex}");
                    }
                }

                if (currentTask.IsCompleted())
                {
                    // Completed subtask, push to queue ready for submission
                    var submission = currentTask.ToSubmission();
                    if (submission != null)
                    {
                        _searchItemOrchistrator.Submit(submission);
                        long duration = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - taskStartTime;
                        if (duration <= 0)
                        {
                            duration = 1;
                        }

                        workerReport.CompleteTask(currentTask, duration);
                    }
                    else
                    {
                        Console.Error.WriteLine($"Error: failed to parse submission...");
                    }
                }
            }

            // Worker has exited
            workerReport.IsRunning = false;
            Perft.FreeHashTable();
        }

        internal void FinishTasksAndQuit()
        {
            KeepRequestingWork = false;
        }

        internal void ToggleOutputDetails()
        {
            OutputFullDetails = !OutputFullDetails;
        }

        internal void TogglePause()
        {
            IsPaused = !IsPaused;
        }
        
        internal void ResetStats()
        {
            _resetStats = true;
        }
    }
}
