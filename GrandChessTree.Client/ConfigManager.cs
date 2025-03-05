using System.Text.Json.Serialization;

namespace GrandChessTree.Client
{
    public class Config
    {
        [JsonPropertyName("api_url")]
        public string ApiUrl { get; set; } = "";

        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("workers")]
        public int Workers { get; set; } = 4;

        [JsonPropertyName("worker_id")]
        public int WorkerId { get; set; } = 0;

        [JsonPropertyName("task_type")]
        public int TaskType { get; set; } = 0;

        [JsonPropertyName("mb_hash")]
        public int MbHash { get; set; } = 1024;

        [JsonPropertyName("sub_task_cache_size")]
        public int SubTaskCacheSize { get; set; } = 1024;

        [JsonPropertyName("sub_task_launch_depth")]
        public int SubTaskLaunchDepth { get; set; } = 3;
    }

    public static class ConfigManager
    {
        public static bool IsValidConfig(Config config)
        {
            bool isValid = true;

            if (string.IsNullOrWhiteSpace(config.ApiUrl))
            {
                Console.WriteLine("Error: API URL cannot be empty.");
                isValid = false;
            }
            else if (!Uri.TryCreate(config.ApiUrl, UriKind.Absolute, out _))
            {
                Console.WriteLine("Error: API URL is not a valid URI.");
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Console.WriteLine("Error: API Key cannot be empty.");
                isValid = false;
            }

            if (config.Workers <= 0)
            {
                Console.WriteLine("Error: Workers must be greater than zero.");
                isValid = false;
            }

            if (config.WorkerId < 0)
            {
                Console.WriteLine("Error: Worker Id must be >= 0.");
                isValid = false;
            }

            if (config.MbHash < 256)
            {
                Console.WriteLine("Error: Mb Hash must be >= 256.");
                isValid = false;
            }

            if (config.SubTaskCacheSize < 256)
            {
                Console.WriteLine("Error: Sub task cache size must be at least 256.");
                isValid = false;
            }

            return isValid;
        }


        public static Config LoadOrCreateConfig()
        {
            var config = WorkerPersistence.LoadConfig();
            if (config != null)
            {
                return config;
            }
            return CreateNewConfig();
        }



        private static Config CreateNewConfig()
        {
            var config = new Config();

            Console.Write("Enter API URL: ");
            config.ApiUrl = Console.ReadLine()?.Trim() ?? "";

            Console.Write("Enter API Key: ");
            config.ApiKey = Console.ReadLine()?.Trim() ?? "";

            Console.Write("Enter number of workers: ");
            if (int.TryParse(Console.ReadLine(), out int workers))
            {
                config.Workers = workers;
            }

            Console.Write("Enter number the worker id: ");
            if (int.TryParse(Console.ReadLine(), out int workerId))
            {
                config.WorkerId = workerId;
            }


            Console.Write("Enter the task type: ");
            if (int.TryParse(Console.ReadLine(), out int taskType))
            {
                config.TaskType = taskType;
            }

            Console.Write("Enter the amount of ram to allocate to each worker (in MB): ");
            if (int.TryParse(Console.ReadLine(), out int mbHash))
            {
                config.MbHash = mbHash;
            }

            Console.Write("Enter the amount of ram to allocate to the subtask cache (in MB): ");
            if (int.TryParse(Console.ReadLine(), out int subTaskCache))
            {
                config.SubTaskCacheSize = subTaskCache;
            }

            WorkerPersistence.SaveConfig(config);
            return config;
        }

    }

}
