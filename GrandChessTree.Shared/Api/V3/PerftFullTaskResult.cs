using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace GrandChessTree.Shared.Api;
public class PerftFullTaskResultBatchOld
{
    [JsonPropertyName("worker_id")]
    public int WorkerId { get; set; } = 0;

    [JsonPropertyName("results")]
    public required PerftFullTaskResult[] Results { get; set; }
}

public class PerftFullTaskResultBatch
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
    public required ulong[][] Results { get; set; }
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

public class PerftFullTaskResultDecompressed
{
    public static ulong[] Compress(long taskId,
     ulong nodes,
     ulong captures,
     ulong enpassants,
     ulong castles,
     ulong promotions,
     ulong directChecks,
     ulong singleDiscoveredChecks,
     ulong directDiscoveredChecks,
     ulong doubleDiscoveredChecks,
     ulong directMates,
     ulong singleDiscoveredMates,
     ulong directDiscoverdMates,
     ulong doubleDiscoverdMates)
    {
        // Create an array with 15 elements.
        var result = new ulong[15];

        // Store taskId as the first element (ensure taskId is non-negative or handle conversion appropriately)
        result[0] = (ulong)taskId;

        // Store the rest of the values in the expected order.
        result[1] = nodes;
        result[2] = captures;
        result[3] = enpassants;
        result[4] = castles;
        result[5] = promotions;
        result[6] = directChecks;
        result[7] = singleDiscoveredChecks;
        result[8] = directDiscoveredChecks;
        result[9] = doubleDiscoveredChecks;
        result[10] = directMates;
        result[11] = singleDiscoveredMates;
        result[12] = directDiscoverdMates;
        result[13] = doubleDiscoverdMates;

        // Compute checksum from indexes 1 to 13.
        ulong checkSum = 0;
        for (int i = 1; i <= 13; i++)
        {
            checkSum ^= result[i];
        }
        result[14] = checkSum;

        return result;
    }

    public static PerftFullTaskResult? Decompress(ulong[] data)
    {
        // Verify the array length.
        if (data.Length != 15)
        {
            return null;
        }

        // Calculate the checksum from data[1] to data[13].
        var checkSum = data[1] ^ data[2] ^ data[3] ^ data[4] ^ data[5]
            ^ data[6] ^ data[7] ^ data[8] ^ data[9]
            ^ data[10] ^ data[11] ^ data[12] ^ data[13];

        // Validate checksum.
        if (checkSum != data[14])
        {
            return null;
        }

        return new PerftFullTaskResult()
        {
            TaskId = (long)data[0],
            Nodes= data[1],
            Captures = data[2],
            Enpassants = data[3],
            Castles = data[4],
            Promotions = data[5],
            DirectChecks = data[6],
            SingleDiscoveredChecks = data[7],
            DirectDiscoveredChecks = data[8],
            DoubleDiscoveredChecks = data[9],
            DirectMates = data[10],
            SingleDiscoveredMates = data[11],
            DirectDiscoverdMates = data[12],
            DoubleDiscoverdMates = data[13]
        };
    }

}
