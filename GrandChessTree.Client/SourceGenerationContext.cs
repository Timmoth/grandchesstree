using System.Text.Json.Serialization;
using GrandChessTree.Client.Stats;
using GrandChessTree.Shared.Api;

namespace GrandChessTree.Client;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, WriteIndented = true)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(PerftTask))]
[JsonSerializable(typeof(PerftTask[]))]
[JsonSerializable(typeof(PerftTaskResponse[]))]
[JsonSerializable(typeof(PerftTaskResultBatch))]
[JsonSerializable(typeof(PerftNodesTask))]
[JsonSerializable(typeof(PerftNodesTask[]))]
[JsonSerializable(typeof(PerftNodesTaskResponse[]))]
[JsonSerializable(typeof(PerftNodesTaskResultBatch))]

internal sealed partial class SourceGenerationContext : JsonSerializerContext;
