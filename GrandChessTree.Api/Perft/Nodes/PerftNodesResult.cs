using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace GrandChessTree.Api.D10Search
{
    public class PerftNodesResult
    {
        [Column("nodes")]
        [JsonPropertyName("nodes")]
        public ulong Nodes { get; set; }
   
    }
}
