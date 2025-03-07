﻿using System.Text.Json.Serialization;

namespace GrandChessTree.Shared.Api;
public class PerftFastTaskResponse
{
    [JsonPropertyName("task_id")]
    public required long TaskId { get; set; }

    [JsonPropertyName("board")]
    public required string Board { get; set; }

    [JsonPropertyName("depth")]
    public required int Depth { get; set; }

    [JsonPropertyName("launch_depth")]
    public required int LaunchDepth { get; set; }
}

public class PerftTaskResponse
{
    [JsonPropertyName("task_id")]
    public required long TaskId { get; set; }

    [JsonPropertyName("board")]
    public required string Board { get; set; }

    [JsonPropertyName("depth")]
    public required int Depth { get; set; }

    [JsonPropertyName("launch_depth")]
    public required int LaunchDepth { get; set; }
}
