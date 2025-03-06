using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;
public class PerftFastTaskResultBatch
{
    [JsonPropertyName("worker_id")]
    public int WorkerId { get; set; } = 0;

    [JsonPropertyName("allocated_mb")]
    public int AllocatedMb { get; set; } = 0;

    [JsonPropertyName("threads")]
    public int Threads { get; set; } = 0;

    [JsonPropertyName("mips")]
    public float Mips { get; set; } = 0;

    [JsonPropertyName("results")]
    public required ulong[] Results { get; set; }
}

public class PerftFastTaskResultBatchOld
{
    [JsonPropertyName("worker_id")]
    public int WorkerId { get; set; } = 0;

    [JsonPropertyName("results")]
    public required PerftFastTaskResult[] Results { get; set; }
}
public class PerftFastTaskResult
{
    [JsonPropertyName("task_id")]
    public required long TaskId { get; set; }

    [JsonPropertyName("nodes")]
    public required ulong Nodes { get; set; }
}
