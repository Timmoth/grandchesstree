using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;

public class PerftTaskResultBatch
{
    [JsonPropertyName("worker_id")]
    public int WorkerId { get; set; } = 0;

    [JsonPropertyName("results")]
    public required PerftTaskResult[] Results { get; set; }
}
public class PerftTaskResult
{
    [JsonPropertyName("perft_task_id")]
    public required long PerftTaskId { get; set; }

    [JsonPropertyName("perft_item_hash")]
    public required ulong PerftItemHash { get; set; }

    [JsonPropertyName("nodes")]
    public required ulong Nodes { get; set; }
    [JsonPropertyName("captures")]
    public required ulong Captures { get; set; }
    [JsonPropertyName("enpassants")]
    public required ulong Enpassant { get; set; }
    [JsonPropertyName("castles")]
    public required ulong Castles { get; set; }
    [JsonPropertyName("promotions")]
    public required ulong Promotions { get; set; }
    [JsonPropertyName("direct_checks")]
    public required ulong DirectCheck { get; set; }
    [JsonPropertyName("single_discovered_check")]
    public required ulong SingleDiscoveredCheck { get; set; }
    [JsonPropertyName("direct_discovered_check")]
    public required ulong DirectDiscoveredCheck { get; set; }
    [JsonPropertyName("double_discovered_check")]
    public required ulong DoubleDiscoveredCheck { get; set; }
    [JsonPropertyName("direct_checkmate")]
    public required ulong DirectCheckmate { get; set; }
    [JsonPropertyName("single_discovered_checkmate")]
    public required ulong SingleDiscoveredCheckmate { get; set; }
    [JsonPropertyName("direct_discoverd_checkmate")]
    public required ulong DirectDiscoverdCheckmate { get; set; }
    [JsonPropertyName("double_discoverd_checkmate")]
    public required ulong DoubleDiscoverdCheckmate { get; set; }


}
