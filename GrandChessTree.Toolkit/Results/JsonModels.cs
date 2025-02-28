using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandChessTree.Toolkit.Results
{
    public class ContributorSummary
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("full_task_nodes")]
        public float FullTaskNodes { get; set; }

        [JsonPropertyName("full_tasks_completed")]
        public float CompletedFullTasks { get; set; }

        [JsonPropertyName("fast_task_nodes")]
        public float FastTaskNodes { get; set; }

        [JsonPropertyName("fast_tasks_completed")]
        public float CompletedFastTasks { get; set; }

        [JsonPropertyName("compute_time_seconds")]
        public float ComputeTime { get; set; }
    }

    public static class ContributorSummaryTools
    {
        public static void CreateContributorSummary()
        {
            // Read json files
            var startPos = JsonSerializer.Deserialize<Root>(File.ReadAllText("./perft_p0_results.json")) ?? throw new Exception("./perft_p0_results.json not found");
            var kiwipete = JsonSerializer.Deserialize<Root>(File.ReadAllText("./perft_p1_results.json")) ?? throw new Exception("./perft_p1_results.json not found");
            var sje = JsonSerializer.Deserialize<Root>(File.ReadAllText("./perft_p2_results.json")) ?? throw new Exception("./perft_p2_results.json not found");

            List<Contribution> contributions =
            [
                .. startPos.Results.SelectMany(r => r.Contributors),
                .. kiwipete.Results.SelectMany(r => r.Contributors),
                .. sje.Results.SelectMany(r => r.Contributors),
            ];

            var contributorSummary = new List<ContributorSummary>();
            foreach (var group in contributions.GroupBy(c => c.Id))
            {
                contributorSummary.Add(new ContributorSummary()
                {
                    Id = group.Key,
                    Name = group.First().Name,
                    FullTaskNodes = group.Sum(g => (float)g.Nodes),
                    CompletedFullTasks = group.Sum(g => (float)g.Tasks),
                    FastTaskNodes = 0,
                    CompletedFastTasks = 0,
                    ComputeTime = group.Sum(g => (float)g.ComputeTime)
                });
            }

            File.WriteAllText("./contributor_summary.json", JsonSerializer.Serialize(contributorSummary));
        }
    }
    public class Contribution
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("nodes")]
        public long Nodes { get; set; }

        [JsonPropertyName("tasks")]
        public int Tasks { get; set; }

        [JsonPropertyName("compute_time")]
        public int ComputeTime { get; set; }
    }

    public class Result
    {
        [JsonPropertyName("position")]
        public long Position { get; set; }

        [JsonPropertyName("depth")]
        public long Depth { get; set; }

        [JsonPropertyName("nodes")]
        public long Nodes { get; set; }

        [JsonPropertyName("captures")]
        public long Captures { get; set; }

        [JsonPropertyName("enpassants")]
        public long Enpassants { get; set; }

        [JsonPropertyName("castles")]
        public long Castles { get; set; }

        [JsonPropertyName("promotions")]
        public long Promotions { get; set; }

        [JsonPropertyName("direct_checks")]
        public long DirectChecks { get; set; }

        [JsonPropertyName("single_discovered_checks")]
        public long SingleDiscoveredChecks { get; set; }

        [JsonPropertyName("direct_discovered_checks")]
        public long DirectDiscoveredChecks { get; set; }

        [JsonPropertyName("double_discovered_checks")]
        public long DoubleDiscoveredChecks { get; set; }

        [JsonPropertyName("total_checks")]
        public long TotalChecks { get; set; }

        [JsonPropertyName("direct_mates")]
        public long DirectMates { get; set; }

        [JsonPropertyName("single_discovered_mates")]
        public long SingleDiscoveredMates { get; set; }

        [JsonPropertyName("direct_discovered_mates")]
        public long DirectDiscoveredMates { get; set; }

        [JsonPropertyName("double_discovered_mates")]
        public long DoubleDiscoveredMates { get; set; }

        [JsonPropertyName("total_mates")]
        public long TotalMates { get; set; }

        [JsonPropertyName("total_tasks")]
        public long TotalTasks { get; set; }

        [JsonPropertyName("started_at")]
        public long StartedAt { get; set; }

        [JsonPropertyName("finished_at")]
        public long FinishedAt { get; set; }

        [JsonPropertyName("contributors")]
        public List<Contribution> Contributors { get; set; }
    }

    public class Root
    {
        [JsonPropertyName("position_name")]
        public string PositionName { get; set; }

        [JsonPropertyName("position_fen")]
        public string PositionFen { get; set; }

        [JsonPropertyName("position_description")]
        public string PositionDescription { get; set; }

        [JsonPropertyName("results")]
        public List<Result> Results { get; set; }
    }


}
