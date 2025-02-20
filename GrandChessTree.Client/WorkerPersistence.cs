using System.Text;
using System.Text.Json;
using GrandChessTree.Client.Stats;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client
{
    public static class WorkerPersistence
    {
        private static readonly string StoragePath = "data";
        private static readonly string NodesStoragePath = "data/nodes";

        private static readonly string PartiallyCompletedTasksFilePath;
        private static readonly string PendingTasksFilePath;

        private static readonly string PartiallyCompletedNodesTasksFilePath;
        private static readonly string PendingNodesTasksFilePath;
        private static readonly string ConfigFilePath;
        private static readonly object _fileLock = new();

        static WorkerPersistence()
        {
            // Ensure storage directory exists
            Directory.CreateDirectory(StoragePath);
            Directory.CreateDirectory(NodesStoragePath);

            PartiallyCompletedTasksFilePath = Path.Combine(StoragePath, $"partially_completed_tasks.json");
            PendingTasksFilePath = Path.Combine(StoragePath, $"pending_tasks.json");


            PartiallyCompletedNodesTasksFilePath = Path.Combine(NodesStoragePath, $"partially_completed_tasks.json");
            PendingNodesTasksFilePath = Path.Combine(NodesStoragePath, $"pending_tasks.json");

            ConfigFilePath = Path.Combine(StoragePath, $"config.json");
        }


        public static Config? LoadConfig()
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.Config) ?? new Config();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading config file: {ex.Message}");
                }
            }

            return null;
        }

        public static void SaveConfig(Config config)
        {
            string json = JsonSerializer.Serialize(config, SourceGenerationContext.Default.Config);
            File.WriteAllText(ConfigFilePath, json);
        }


        #region Stats Tasks
        public static PerftTask[]? LoadPartiallyCompletedTasks()
        {
            if (!File.Exists(PartiallyCompletedTasksFilePath))
                return null;

            byte[] data = File.ReadAllBytes(PartiallyCompletedTasksFilePath);
            return JsonSerializer.Deserialize(Encoding.UTF8.GetString(data), SourceGenerationContext.Default.PerftTaskArray);
        }

        public static void SavePartiallyCompletedTasks(PerftTask[] tasks)
        {
            string json = JsonSerializer.Serialize(tasks, SourceGenerationContext.Default.PerftTaskArray);

            byte[] data = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(PartiallyCompletedTasksFilePath, data);
        }

        public static PerftTaskResponse[]? LoadPendingTasks()
        {
            try
            {
                if (!File.Exists(PendingTasksFilePath))
                {
                    return null;
                }

                byte[] data = File.ReadAllBytes(PendingTasksFilePath);
                return JsonSerializer.Deserialize(Encoding.UTF8.GetString(data), SourceGenerationContext.Default.PerftTaskResponseArray);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

            return null;
        }

        public static void SavePendingTasks(PerftTaskResponse[] tasks)
        {
            lock (_fileLock)
            {
                string json = JsonSerializer.Serialize(tasks, SourceGenerationContext.Default.PerftTaskResponseArray);
                byte[] data = Encoding.UTF8.GetBytes(json);
                File.WriteAllBytes(PendingTasksFilePath, data);
            }
        }
        #endregion

        #region Nodes Tasks
        public static PerftNodesTask[]? LoadPartiallyCompletedNodesTasks()
        {
            if (!File.Exists(PartiallyCompletedNodesTasksFilePath))
                return null;

            byte[] data = File.ReadAllBytes(PartiallyCompletedNodesTasksFilePath);
            return JsonSerializer.Deserialize(Encoding.UTF8.GetString(data), SourceGenerationContext.Default.PerftNodesTaskArray);
        }

        public static void SavePartiallyCompletedNodesTasks(PerftNodesTask[] tasks)
        {
            string json = JsonSerializer.Serialize(tasks, SourceGenerationContext.Default.PerftNodesTaskArray);

            byte[] data = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(PartiallyCompletedNodesTasksFilePath, data);
        }

        public static PerftNodesTaskResponse[]? LoadPendingNodesTasks()
        {
            try
            {
                if (!File.Exists(PendingNodesTasksFilePath))
                {
                    return null;
                }

                byte[] data = File.ReadAllBytes(PendingNodesTasksFilePath);
                return JsonSerializer.Deserialize(Encoding.UTF8.GetString(data), SourceGenerationContext.Default.PerftNodesTaskResponseArray);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

            return null;
        }

        public static void SavePendingNodesTasks(PerftNodesTaskResponse[] tasks)
        {
            lock (_fileLock)
            {
                string json = JsonSerializer.Serialize(tasks, SourceGenerationContext.Default.PerftNodesTaskResponseArray);
                byte[] data = Encoding.UTF8.GetBytes(json);
                File.WriteAllBytes(PendingNodesTasksFilePath, data);
            }
        }
        #endregion

    }
}
