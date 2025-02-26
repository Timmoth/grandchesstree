using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;

public class PerftFullTaskResultBatch
{
    [JsonPropertyName("worker_id")]
    public int WorkerId { get; set; } = 0;

    [JsonPropertyName("results")]
    public required PerftFullTaskResult[] Results { get; set; }
}
public class PerftFullTaskResult
{
    [JsonPropertyName("task_id")]
    public required long TaskId { get; set; }

    [JsonPropertyName("nodes")]
    public required ulong Nodes { get; set; }
    [JsonPropertyName("captures")]
    public required ulong Captures { get; set; }
    [JsonPropertyName("enpassants")]
    public required ulong Enpassants { get; set; }
    [JsonPropertyName("castles")]
    public required ulong Castles { get; set; }
    [JsonPropertyName("promotions")]
    public required ulong Promotions { get; set; }
    [JsonPropertyName("direct_checks")]
    public required ulong DirectChecks { get; set; }
    [JsonPropertyName("single_discovered_checks")]
    public required ulong SingleDiscoveredChecks { get; set; }
    [JsonPropertyName("direct_discovered_checks")]
    public required ulong DirectDiscoveredChecks { get; set; }
    [JsonPropertyName("double_discovered_checks")]
    public required ulong DoubleDiscoveredChecks { get; set; }
    [JsonPropertyName("direct_mates")]
    public required ulong DirectMates { get; set; }
    [JsonPropertyName("single_discovered_mates")]
    public required ulong SingleDiscoveredMates { get; set; }
    [JsonPropertyName("direct_discovered_mates")]
    public required ulong DirectDiscoverdMates { get; set; }
    [JsonPropertyName("double_discovered_mates")]
    public required ulong DoubleDiscoverdMates { get; set; }


}
