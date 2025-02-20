using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;

public class PerftNodesTaskResultBatch
{
    [JsonPropertyName("worker_id")]
    public int WorkerId { get; set; } = 0;

    [JsonPropertyName("results")]
    public required PerftNodesTaskResult[] Results { get; set; }
}
public class PerftNodesTaskResult
{
    [JsonPropertyName("perft_nodes_task_id")]
    public required long PerftNodesTaskId { get; set; }

    [JsonPropertyName("nodes")]
    public required ulong Nodes { get; set; }
}
