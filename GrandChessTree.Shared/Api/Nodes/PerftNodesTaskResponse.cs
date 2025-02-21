using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;
public class PerftNodesTaskResponse
{
    [JsonPropertyName("task_id")]
    public required long TaskId { get; set; }

    [JsonPropertyName("fem")]
    public required string Fen { get; set; }

    [JsonPropertyName("depth")]
    public required int Depth { get; set; }

    [JsonPropertyName("launch_depth")]
    public required int LaunchDepth { get; set; }
}
