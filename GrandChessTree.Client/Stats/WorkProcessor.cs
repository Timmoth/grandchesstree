using System.Collections.Concurrent;
using ConsoleTables;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Stats
{
    public class WorkProcessor
    {
        private readonly Config _config;
        private readonly int _workerCount;
        private readonly SearchItemOrchistrator _searchItemOrchistrator;
        private bool KeepRequestingWork { get; set; } = true;

        public WorkerReport[] _workerReports;

        public bool HasRunningWorkers => _workerReports.Any(w => w.IsRunning);
        private bool OutputFullDetails = false;

        public WorkProcessor(SearchItemOrchistrator searchItemOrchistrator, Config config)
        {
            _config = config;
            _searchItemOrchistrator = searchItemOrchistrator;
            _workerCount = config.Workers;
            _workerReports = new WorkerReport[_workerCount];
            for (int i = 0; i < _workerReports.Length; i++) { _workerReports[i] = new WorkerReport(); }
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

        private void OutputStatsPeriodically()
        {
            ulong prevTotalNodes = 0;
            long prevTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            while (HasRunningWorkers)
            {
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

                    var tpm = (float)sumCompletedTasks / ((currentTime - startTime) / 60000f);

                    Console.CursorVisible = false;
                    Console.SetCursorPosition(0, 0);
                    for (int y = 0; y < Console.WindowHeight; y++)
                        Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, 0);

                    if (OutputFullDetails)
                    {
                        var table = new ConsoleTable("worker", "nps", "nodes", "sub_tasks", "tasks", "fen");
                        for (int i = 0; i < _workerReports.Length; i++)
                        {
                            var report = _workerReports[i];
                            table.AddRow($"{i}", report.Nps.FormatBigNumber(), report.WorkerComputedNodes.FormatBigNumber(), $"{report.CompletedSubtasks}/{report.TotalSubtasks}", report.TotalCompletedTasks, report.Fen);
                        }
                        table.Configure((c) =>
                        {
                            c.EnableCount = false;
                        });
                        table.Write(Format.MarkDown);
                    }

                    var cachHitPercent = sumCompletedSubTasks == 0 ? 0 : (float)subtaskCacheHits / sumCompletedSubTasks * 100;

                    Console.WriteLine($"completed {sumCompletedSubTasks.FormatBigNumber()} subtasks ({cachHitPercent.RoundToSignificantFigures(2)}% cache hits), submitted {_searchItemOrchistrator.Submitted} tasks ({_searchItemOrchistrator.PendingSubmission} pending)");
                    Console.WriteLine($"[computed stats] {totalComputedNodes.FormatBigNumber()} nodes at {sumNps.FormatBigNumber()}nps {tpm.RoundToSignificantFigures(2)}tpm");
                    
                    if (!KeepRequestingWork)
                    {
                        Console.WriteLine($"Will exit automatically when the current tasks are completed. {_workerReports.Count(w => !w.IsRunning)}/{_workerCount} ready");
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                    await Task.Delay(1000);
                }

            });
        }

        private unsafe void ThreadWork(int index)
        {
            Perft.AllocateHashTable(_config.MbHash);
            Summary summary = default;

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

                        // Report that work on this subtask has begun
                        workerReport.BeginSubTask(currentTask);

                        var board = subTask.Fen;
                        var whiteToMove = subTask.Wtm;
                        var hash = subTask.Hash;
                        // Clear the summary struct
                        summary = default;

                        if (_searchItemOrchistrator.SubTaskHashTable.TryGetValue(hash, currentTask.SubTaskDepth, out summary))
                        {
                            // This position has been found in the global cache! Use the cached summary
                            // And report the subtask as completed
                            workerReport.EndSubTaskFoundInCache(currentTask, summary.Nodes, subTaskOccurrences);
                        }
                        else
                        {
                            long subtaskStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            summary.Depth = (byte)currentTask.SubTaskDepth;

                            // Recursive perft
                            Perft.PerftRoot(ref board, ref summary, currentTask.SubTaskDepth, whiteToMove);

                            // Store the hash for this position in the global cache
                            _searchItemOrchistrator.CacheCompletedSubtask(hash, currentTask.SubTaskDepth, summary);

                            // Report the subtask as completed
                            workerReport.EndSubTaskWorkCompleted(currentTask, summary.Nodes, subTaskOccurrences);
                            var subTaskDurationSeconds = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - subtaskStart) / 1000.0f;

                            // Calculate the NPS
                            workerReport.Nps = summary.Nodes / subTaskDurationSeconds;
                        }

                        // Complete the subtask
                        currentTask.CompleteSubTask(summary, subTaskOccurrences);

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
    }
}
