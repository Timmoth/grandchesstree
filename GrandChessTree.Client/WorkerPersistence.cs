using System.Text.Json;

namespace GrandChessTree.Client
{
    public static class WorkerPersistence
    {
        private static readonly string StoragePath = "data";
        private static readonly string ConfigFilePath;
        private static readonly object _fileLock = new();

        static WorkerPersistence()
        {
            // Ensure storage directory exists
            Directory.CreateDirectory(StoragePath);
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
    }
}
