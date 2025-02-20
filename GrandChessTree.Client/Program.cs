using GrandChessTree.Client;
using GrandChessTree.Client.Stats;
using GrandChessTree.Shared.Precomputed;

Console.WriteLine("-----TheGreatChessTree-----");
var containerized = Environment.GetEnvironmentVariable("containerized");

Config config;
// Check if it's set and print it out (or use it in your logic)
if (containerized != null && containerized == "true")
{
    Console.WriteLine($"Running in container");

    var workerEnvVar = Environment.GetEnvironmentVariable("workers");
    if(!int.TryParse(workerEnvVar, out var workerCount))
    {
        Console.WriteLine("'worker' environment variable must be an integer > 0");
        return;
    }

    var workerIdEnvVar = Environment.GetEnvironmentVariable("worker_id");
    if (!int.TryParse(workerEnvVar, out var workerId))
    {
        Console.WriteLine("'worker_id' environment variable must be an integer >= 0");
        return;
    }

    var taskTypeEnvVar = Environment.GetEnvironmentVariable("task_type");
    if (!int.TryParse(workerEnvVar, out var taskType))
    {
        Console.WriteLine("'task_type' environment variable must be an integer >= 0");
        return;
    }


    config = new Config()
    {
        ApiKey = Environment.GetEnvironmentVariable("api_key") ?? "",
        ApiUrl = Environment.GetEnvironmentVariable("api_url") ?? "",
        Workers = workerCount,
        WorkerId = workerId,
        TaskType = taskType
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
        Console.WriteLine("process exited");
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
                if (loweredCommand.Contains("g"))
                {
                    networkClient.FinishTasksAndQuit();
                    break;
                }
                else
                {
                    networkClient.SaveAndQuit();
                    break;
                }
            }
            else if (loweredCommand.StartsWith("d"))
            {
                networkClient.ToggleOutputDetails();
            }

        }
    }
}else if (config.TaskType == 1)
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
        Console.WriteLine("process exited");
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
                if (loweredCommand.Contains("g"))
                {
                    networkClient.FinishTasksAndQuit();
                    break;
                }
                else
                {
                    networkClient.SaveAndQuit();
                    break;
                }
            }
            else if (loweredCommand.StartsWith("d"))
            {
                networkClient.ToggleOutputDetails();
            }

        }
    }
}
else
{
    Console.WriteLine($"Invalid task type: '{config.TaskType}', must be either '0' for stats tasks or '1' for nodes tasks.");
}



