using GrandChessTree.Client;
using GrandChessTree.Client.Stats;
using GrandChessTree.Shared.Precomputed;

class Program
{
    static async Task Main(string[] args)
    {
        // Subscribe to global exception handlers.
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        try
        {
            Console.WriteLine("-----TheGreatChessTree-----");
            var containerized = Environment.GetEnvironmentVariable("containerized");

            Config config;
            if (containerized != null && containerized == "true")
            {
                Console.WriteLine("Running in container");

                var workerEnvVar = Environment.GetEnvironmentVariable("workers");
                if (!int.TryParse(workerEnvVar, out var workerCount))
                {
                    Console.WriteLine("'worker' environment variable must be an integer > 0");
                    return;
                }

                var workerIdEnvVar = Environment.GetEnvironmentVariable("worker_id");
                if (!int.TryParse(workerIdEnvVar, out var workerId))
                {
                    Console.WriteLine("'worker_id' environment variable must be an integer >= 0");
                    return;
                }

                var taskTypeEnvVar = Environment.GetEnvironmentVariable("task_type");
                if (!int.TryParse(taskTypeEnvVar, out var taskType))
                {
                    Console.WriteLine("'task_type' environment variable must be an integer >= 0");
                    return;
                }

                var mbHashEnvVar = Environment.GetEnvironmentVariable("mb_hash");
                if (!int.TryParse(mbHashEnvVar, out var mbHash))
                {
                    mbHash = 1024;
                }

                var subTaskCacheEnvVar = Environment.GetEnvironmentVariable("sub_task_cache_size");
                if (!int.TryParse(subTaskCacheEnvVar, out var subTaskCacheSize))
                {
                    subTaskCacheSize = 1024;
                }

                config = new Config()
                {
                    ApiKey = Environment.GetEnvironmentVariable("api_key") ?? "",
                    ApiUrl = Environment.GetEnvironmentVariable("api_url") ?? "",
                    Workers = workerCount,
                    WorkerId = workerId,
                    TaskType = taskType,
                    MbHash = mbHash,
                    SubTaskCacheSize = subTaskCacheSize,
                };
            }
            else
            {
                config = ConfigManager.LoadOrCreateConfig();
            }

            if (!ConfigManager.IsValidConfig(config))
            {
                return;
            }

            if (config.TaskType == 0)
            {
                var searchOrchastrator = new SearchItemOrchistrator(config);
                var networkClient = new WorkProcessor(searchOrchastrator, config);
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
                _ = Task.Run(ReadCommands);

                networkClient.Run();

                if (searchOrchastrator.PendingSubmission == 0)
                {
                    Console.WriteLine("Nothing left to submit to server.");
                }
                else
                {
                    while (searchOrchastrator.PendingSubmission > 0)
                    {
                        Console.WriteLine($"Syncing with server... {searchOrchastrator.PendingSubmission} pending submissions.");
                        await searchOrchastrator.SubmitToApi();
                    }

                    Console.WriteLine("All tasks submitted to server.");
                }

                void CurrentDomain_ProcessExit(object? sender, EventArgs e)
                {
                    Console.WriteLine("Process exited");
                }

                void ReadCommands()
                {
                    while (true)
                    {
                        var command = Console.ReadLine();
                        if (string.IsNullOrEmpty(command))
                        {
                            continue; // Skip empty commands
                        }

                        command = command.Trim();
                        var loweredCommand = command.ToLower();
                        if (loweredCommand.StartsWith("q"))
                        {
                            networkClient.FinishTasksAndQuit();
                            break;
                        }
                        else if (loweredCommand.StartsWith("d"))
                        {
                            networkClient.ToggleOutputDetails();
                        }
                        else if (loweredCommand.StartsWith("s"))
                        {
                            networkClient.TogglePause();
                        }
                        else if (loweredCommand.StartsWith("r"))
                        {
                            networkClient.ResetStats();
                        }
                    }
                }
            }
            else if (config.TaskType == 1)
            {
                Zobrist.UseXorShift();
                var searchOrchastrator = new NodesTaskOrchistrator(config);
                var networkClient = new NodesWorkProcessor(searchOrchastrator, config);
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
                _ = Task.Run(ReadCommands);

                networkClient.Run();

                if (searchOrchastrator.PendingSubmission == 0)
                {
                    Console.WriteLine("Nothing left to submit to server.");
                }
                else
                {
                    while (searchOrchastrator.PendingSubmission > 0)
                    {
                        Console.WriteLine($"Syncing with server... {searchOrchastrator.PendingSubmission} pending submissions.");
                        await searchOrchastrator.SubmitToApi();
                    }

                    Console.WriteLine("All tasks submitted to server.");
                }

                void CurrentDomain_ProcessExit(object? sender, EventArgs e)
                {
                    Console.WriteLine("Process exited");
                }

                void ReadCommands()
                {
                    while (true)
                    {
                        var command = Console.ReadLine();
                        if (string.IsNullOrEmpty(command))
                        {
                            continue; // Skip empty commands
                        }

                        command = command.Trim();
                        var loweredCommand = command.ToLower();
                        if (loweredCommand.StartsWith("q"))
                        {
                            networkClient.FinishTasksAndQuit();
                            break;
                        }
                        else if (loweredCommand.StartsWith("d"))
                        {
                            networkClient.ToggleOutputDetails();
                        }
                        else if (loweredCommand.StartsWith("s"))
                        {
                            networkClient.TogglePause();
                        }
                        else if (loweredCommand.StartsWith("r"))
                        {
                            networkClient.ResetStats();
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"Invalid task type: '{config.TaskType}', must be either '0' for stats tasks or '1' for nodes tasks.");
            }
        }
        catch (Exception ex)
        {
            // This catch block might not catch AccessViolationException unless properly configured,
            // but it's useful for other exceptions.
            Console.WriteLine("Caught exception in Main: " + ex.ToString());
            throw;
        }
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // This handler is invoked for exceptions that are not caught anywhere else,
        // including corrupted state exceptions if allowed.
        Exception ex = e.ExceptionObject as Exception;
        string message = $"Global Unhandled Exception: {ex?.ToString() ?? "Unknown exception"}";
        Console.WriteLine(message);

        Environment.Exit(1);
    }

    private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        string message = $"Unobserved Task Exception: {e.Exception.ToString()}";
        Console.WriteLine(message);
        // Optionally log this as well
        e.SetObserved(); // Prevent the process from terminating.
    }
}
