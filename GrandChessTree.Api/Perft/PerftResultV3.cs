using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace GrandChessTree.Api.D10Search
{
    public class PerftResultV3
    {
        [Column("nodes")]
        [JsonPropertyName("nodes")]
        public ulong Nodes { get; set; }
        [Column("captures")]
        [JsonPropertyName("captures")]
        public ulong Captures { get; set; }
        [Column("enpassants")]
        [JsonPropertyName("enpassants")]
        public ulong Enpassants { get; set; }
        [Column("castles")]
        [JsonPropertyName("castles")]
        public ulong Castles { get; set; }
        [Column("promotions")]
        [JsonPropertyName("promotions")]
        public ulong Promotions { get; set; }
        [Column("direct_checks")]
        [JsonPropertyName("direct_checks")]
        public ulong DirectChecks { get; set; }
        [Column("single_discovered_checks")]
        [JsonPropertyName("single_discovered_checks")]
        public ulong SingleDiscoveredCheck { get; set; }
        [Column("direct_discovered_checks")]
        [JsonPropertyName("direct_discovered_checks")]
        public ulong DirectDiscoveredCheck { get; set; }
        [Column("double_discovered_checks")]
        [JsonPropertyName("double_discovered_checks")]
        public ulong DoubleDiscoveredCheck { get; set; }
        [Column("direct_mates")]
        [JsonPropertyName("direct_mates")]
        public ulong DirectMates { get; set; }
        [Column("single_discovered_mates")]
        [JsonPropertyName("single_discovered_mates")]
        public ulong SingleDiscoveredMates { get; set; }
        [Column("direct_discovered_mates")]
        [JsonPropertyName("direct_discovered_mates")]
        public ulong DirectDiscoverdMates { get; set; }
        [Column("double_discovered_mates")]
        [JsonPropertyName("double_discovered_mates")]
        public ulong DoubleDiscoverdMates { get; set; }
    }
}
