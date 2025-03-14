﻿using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;
public class PerftStatsResponse
{
    [JsonPropertyName("nps")]
    public float Nps { get; set; }

    [JsonPropertyName("tpm")]
    public float Tpm { get; set; }

    [JsonPropertyName("completed_tasks")]
    public ulong CompletedTasks { get; set; }

    [JsonPropertyName("total_nodes")]
    public ulong TotalNodes { get; set; }

    [JsonPropertyName("percent_completed_tasks")]
    public float PercentCompletedTasks { get; set; }

    [JsonPropertyName("total_tasks")]
    public int TotalTasks { get; set; }
}

public class PerftLeaderboardResponse
{
    [JsonPropertyName("account_id")]
    public long AccountId { get; set; }

    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = "Unknown";

    [JsonPropertyName("total_nodes")]
    public long TotalNodes { get; set; }

    [JsonPropertyName("total_tasks")]
    public long TotalTasks { get; set; }

    [JsonPropertyName("compute_time_seconds")]
    public long TotalTimeSeconds { get; set; }

    [JsonPropertyName("completed_tasks")]
    public long CompletedTasks { get; set; }   
    
    [JsonPropertyName("tpm")]
    public float TasksPerMinute { get; set; }

    [JsonPropertyName("nps")]
    public float NodesPerSecond { get; set; }

    [JsonPropertyName("workers")]
    public int Workers { get; set; }
    
    [JsonPropertyName("threads")]
    public int Threads { get; set; }
    
    [JsonPropertyName("allocated_mb")]
    public int AllocatedMb { get; set; }

    [JsonPropertyName("mips")]
    public float Mips { get; set; }
}