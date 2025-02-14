using System.Formats.Asn1;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleTables;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using GrandChessTree.Shared.Helpers;
using Npgsql;

namespace GrandChessTree.Toolkit
{
    internal class DbDumper
    {
        public static async Task Dump(int positionId, int depth)
        {
            Console.WriteLine("Enter pgsql connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = "Host=localhost;Port=4675;Database=application;Username=postgres;Password=chessrulz";
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            try
            {
                string filePath = $"perft_p{positionId}_d{depth}_dump.csv";
                await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
                var copySql = @$"COPY (SELECT 
nodes, captures, enpassants, castles, promotions, 
direct_checks, single_discovered_check, direct_discovered_check,
double_discovered_check, direct_checkmate, single_discovered_checkmate,
direct_discoverd_checkmate, double_discoverd_checkmate,
started_at, finished_at, nps, occurrences, hash, fen, account_id, worker_id, name
FROM public.perft_tasks t
JOIN public.perft_items i ON t.perft_item_id = i.id
JOIN public.accounts a ON a.id = t.account_id
WHERE t.depth = {depth} AND t.root_position_id = {positionId} AND finished_at > 0) TO STDOUT WITH CSV HEADER";

                var reader = await conn.BeginTextExportAsync(copySql);
                string csvData = await reader.ReadToEndAsync();

                // Write the entire CSV content to the file.
                await writer.WriteAsync(csvData);

                Console.WriteLine($"Data successfully exported to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during db dump: {ex.Message}");
            }
        }

        public class PerfTask
        {
            [Name("nodes")]
            public long Nodes { get; set; }
            [Name("captures")]
            public long Captures { get; set; }
            [Name("enpassants")]
            public long Enpassants { get; set; }
            [Name("castles")]
            public long Castles { get; set; }
            [Name("promotions")]
            public long Promotions { get; set; }
            [Name("direct_checks")]
            public long DirectChecks { get; set; }
            [Name("single_discovered_check")]
            public long SingleDiscoveredCheck { get; set; }
            [Name("direct_discovered_check")]
            public long DirectDiscoveredCheck { get; set; }
            [Name("double_discovered_check")]
            public long DoubleDiscoveredCheck { get; set; }
            [Name("direct_checkmate")]
            public long DirectCheckmate { get; set; }
            [Name("single_discovered_checkmate")]
            public long SingleDiscoveredCheckmate { get; set; }
            [Name("direct_discoverd_checkmate")]
            public long DirectDiscoverdCheckmate { get; set; }
            [Name("double_discoverd_checkmate")]
            public long DoubleDiscoverdCheckmate { get; set; }
            [Name("started_at")]
            public long StartedAt { get; set; }
            [Name("finished_at")]
            public long FinishedAt { get; set; }
            [Name("nps")]
            public double Nps { get; set; }
            [Name("occurrences")]
            public int Occurrences { get; set; }
            [Name("hash")]
            public ulong Hash { get; set; }
            [Name("fen")]
            public string Fen { get; set; }
            [Name("account_id")]
            public int AccountId { get; set; }
            [Name("worker_id")]
            public int WorkerId { get; set; }
            [Name("name")]
            public string Name { get; set; }
        }

        public class PerfTaskSummary
        {
            [Name("nodes")]
            [JsonPropertyName("nodes")]
            public long Nodes { get; set; } 
            [Name("captures")]
            [JsonPropertyName("captures")]
            public long Captures { get; set; }
            [Name("enpassants")]
            [JsonPropertyName("enpassants")]
            public long Enpassants { get; set; }
            [Name("castles")]
            [JsonPropertyName("castles")]
            public long Castles { get; set; }
            [Name("promotions")]
            [JsonPropertyName("promotions")]
            public long Promotions { get; set; }
            [Name("direct_checks")]
            [JsonPropertyName("direct_checks")]
            public long DirectChecks { get; set; }
            [Name("single_discovered_check")]
            [JsonPropertyName("single_discovered_check")]
            public long SingleDiscoveredCheck { get; set; }
            [Name("direct_discovered_check")]
            [JsonPropertyName("direct_discovered_check")]
            public long DirectDiscoveredCheck { get; set; }
            [Name("double_discovered_check")]
            [JsonPropertyName("double_discovered_check")]
            public long DoubleDiscoveredCheck { get; set; }
            [Name("total_checks")]
            [JsonPropertyName("total_checks")]
            public long TotalChecks { get; set; }
            [Name("direct_checkmate")]
            [JsonPropertyName("direct_checkmate")]
            public long DirectCheckmate { get; set; }
            [Name("single_discovered_checkmate")]
            [JsonPropertyName("single_discovered_checkmate")]
            public long SingleDiscoveredCheckmate { get; set; }
            [Name("direct_discoverd_checkmate")]
            [JsonPropertyName("direct_discoverd_checkmate")]
            public long DirectDiscoverdCheckmate { get; set; }
            [Name("double_discoverd_checkmate")]
            [JsonPropertyName("double_discoverd_checkmate")]
            public long DoubleDiscoverdCheckmate { get; set; }
            [Name("total_mates")]
            [JsonPropertyName("total_mates")]
            public long TotalMates { get; set; }
            public void Add(PerfTaskSummary other)
            {
                Nodes += other.Nodes;
                Captures += other.Captures;
                Enpassants += other.Enpassants;
                Castles += other.Castles;
                Promotions += other.Promotions;
                DirectChecks += other.DirectChecks;
                SingleDiscoveredCheck += other.SingleDiscoveredCheck;
                DirectDiscoveredCheck += other.DirectDiscoveredCheck;
                DoubleDiscoveredCheck += other.DoubleDiscoveredCheck;
                DirectCheckmate += other.DirectCheckmate;
                SingleDiscoveredCheckmate += other.SingleDiscoveredCheckmate;
                DirectDiscoverdCheckmate += other.DirectDiscoverdCheckmate;
                DoubleDiscoverdCheckmate += other.DoubleDiscoverdCheckmate;


                TotalChecks += other.DirectChecks;
                TotalChecks += other.SingleDiscoveredCheck;
                TotalChecks += other.DirectDiscoveredCheck;
                TotalChecks += other.DoubleDiscoveredCheck;

                TotalMates += other.DirectCheckmate;
                TotalMates += other.SingleDiscoveredCheckmate;
                TotalMates += other.DirectDiscoverdCheckmate;
                TotalMates += other.DoubleDiscoverdCheckmate;
            }
            public void Add(PerfTask other)
            {
                Nodes += other.Nodes;
                Captures += other.Captures;
                Enpassants += other.Enpassants;
                Castles += other.Castles;
                Promotions += other.Promotions;
                DirectChecks += other.DirectChecks;
                SingleDiscoveredCheck += other.SingleDiscoveredCheck;
                DirectDiscoveredCheck += other.DirectDiscoveredCheck;
                DoubleDiscoveredCheck += other.DoubleDiscoveredCheck;
                DirectCheckmate += other.DirectCheckmate;
                SingleDiscoveredCheckmate += other.SingleDiscoveredCheckmate;
                DirectDiscoverdCheckmate += other.DirectDiscoverdCheckmate;
                DoubleDiscoverdCheckmate += other.DoubleDiscoverdCheckmate;

                TotalChecks += other.DirectChecks;
                TotalChecks += other.SingleDiscoveredCheck;
                TotalChecks += other.DirectDiscoveredCheck;
                TotalChecks += other.DoubleDiscoveredCheck;

                TotalMates += other.DirectCheckmate;
                TotalMates += other.SingleDiscoveredCheckmate;
                TotalMates += other.DirectDiscoverdCheckmate;
                TotalMates += other.DoubleDiscoverdCheckmate;
            }
        }

        public class PerfTaskTotalSummary
        {
            [Name("nodes")]
            [JsonPropertyName("nodes")]
            public long Nodes { get; set; }
            [Name("captures")]
            [JsonPropertyName("captures")]
            public long Captures { get; set; }
            [Name("enpassants")]
            [JsonPropertyName("enpassants")]
            public long Enpassants { get; set; }
            [Name("castles")]
            [JsonPropertyName("castles")]
            public long Castles { get; set; }
            [Name("promotions")]
            [JsonPropertyName("promotions")]
            public long Promotions { get; set; }
            [Name("direct_checks")]
            [JsonPropertyName("direct_checks")]
            public long DirectChecks { get; set; }
            [Name("single_discovered_check")]
            [JsonPropertyName("single_discovered_check")]
            public long SingleDiscoveredCheck { get; set; }
            [Name("direct_discovered_check")]
            [JsonPropertyName("direct_discovered_check")]
            public long DirectDiscoveredCheck { get; set; }
            [Name("double_discovered_check")]
            [JsonPropertyName("double_discovered_check")]
            public long DoubleDiscoveredCheck { get; set; }
            [Name("total_checks")]
            [JsonPropertyName("total_checks")]
            public long TotalChecks { get; set; }
            [Name("direct_checkmate")]
            [JsonPropertyName("direct_checkmate")]
            public long DirectCheckmate { get; set; }
            [Name("single_discovered_checkmate")]
            [JsonPropertyName("single_discovered_checkmate")]
            public long SingleDiscoveredCheckmate { get; set; }
            [Name("direct_discoverd_checkmate")]
            [JsonPropertyName("direct_discoverd_checkmate")]
            public long DirectDiscoverdCheckmate { get; set; }
            [Name("double_discoverd_checkmate")]
            [JsonPropertyName("double_discoverd_checkmate")]
            public long DoubleDiscoverdCheckmate { get; set; }
            [Name("total_mates")]
            [JsonPropertyName("total_mates")]
            public long TotalMates { get; set; }

            public void Add(PerfTaskSummary other)
            {
                Nodes += other.Nodes;
                Captures += other.Captures;
                Enpassants += other.Enpassants;
                Castles += other.Castles;
                Promotions += other.Promotions;
                DirectChecks += other.DirectChecks;
                SingleDiscoveredCheck += other.SingleDiscoveredCheck;
                DirectDiscoveredCheck += other.DirectDiscoveredCheck;
                DoubleDiscoveredCheck += other.DoubleDiscoveredCheck;
                DirectCheckmate += other.DirectCheckmate;
                SingleDiscoveredCheckmate += other.SingleDiscoveredCheckmate;
                DirectDiscoverdCheckmate += other.DirectDiscoverdCheckmate;
                DoubleDiscoverdCheckmate += other.DoubleDiscoverdCheckmate;

                TotalChecks += other.DirectChecks;
                TotalChecks += other.SingleDiscoveredCheck;
                TotalChecks += other.DirectDiscoveredCheck;
                TotalChecks += other.DoubleDiscoveredCheck;

                TotalMates += other.DirectCheckmate;
                TotalMates += other.SingleDiscoveredCheckmate;
                TotalMates += other.DirectDiscoverdCheckmate;
                TotalMates += other.DoubleDiscoverdCheckmate;
            }

            [Name("total_tasks")]
            [JsonPropertyName("total_tasks")]
            public long Tasks { get; set; }

            [Name("started_at")]
            [JsonPropertyName("started_at")]
            public long StartedAt { get; set; }

            [Name("finished_at")]
            [JsonPropertyName("finished_at")]
            public long FinishedAt { get; set; }

            [Name("contributors")]
            [JsonPropertyName("contributors")]
            public List<ContributorSummary> Contributors { get; set; }
        }


        public class ContributorSummary
        {
            [Name("id")]
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [Name("name")]
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [Name("nodes")]
            [JsonPropertyName("nodes")]
            public long Nodes { get; set; }

            [Name("tasks")]
            [JsonPropertyName("tasks")]
            public int Tasks { get; set; }

            [Name("compute_time")]
            [JsonPropertyName("compute_time")]
            public long ComputeTime { get; set; }
        }

        public static async Task ConstructSummary(int positionId, int depth, int launchDepth, string fen)
        {
            string filePath = $"./perft_p{positionId}_d{depth}_dump.csv";

            if (!File.Exists(filePath))
            {
                Console.WriteLine("CSV file not found: " + filePath);
                return;
            }

            // Open the CSV file and load its contents into a list of PerfTask objects.
            using (var reader = new StreamReader(filePath))

            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                try
                {
                    var results = csv.GetRecords<PerfTask>().ToDictionary(r => r.Hash, r => r);
                    var (initialBoard, whiteToMove) = FenParser.Parse(fen);
                    var divideResults = LeafNodeGenerator.GenerateLeafNodesIncludeDuplicates(ref initialBoard, 1, whiteToMove);

                    var totalSummary = new PerfTaskTotalSummary();
                    var divideSummaries = new List<PerfTaskSummary>();
                    foreach (var (d_hash, d_fen) in divideResults)
                    {
                        var (d_board, d_whiteToMove) = FenParser.Parse(d_fen);
                        var d_results = LeafNodeGenerator.GenerateLeafNodesIncludeDuplicates(ref d_board, launchDepth - 1, d_whiteToMove);
                        var summary = new PerfTaskSummary();
                        foreach(var (l_hash, l_fen) in d_results)
                        {
                            summary.Add(results[l_hash]);
                        }
                        totalSummary.Add(summary);
                        divideSummaries.Add(summary);
                        Console.WriteLine($"{summary.Nodes}");
                    }

                    totalSummary.Tasks = results.Values.Count();
                    totalSummary.StartedAt = results.Values.MinBy(t => t.StartedAt)!.StartedAt;
                    totalSummary.FinishedAt = results.Values.MaxBy(t => t.FinishedAt)!.FinishedAt;


                    totalSummary.Contributors = results.Values.GroupBy(v => v.AccountId).Select(g => {
                        return new ContributorSummary()
                        {
                            Id = g.Key,
                            Name = g.First().Name,
                            Nodes = g.Sum(g => g.Nodes),
                            Tasks = g.Count(),
                            ComputeTime = g.Sum(g => g.FinishedAt - g.StartedAt)
                        };
                    }).ToList(); 

                    Console.WriteLine($"final: {totalSummary.Nodes}");

                    using (var writer = new StreamWriter($"./perft_p{positionId}_d{depth}_divide.csv"))
                    using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csvWriter.WriteRecords(divideSummaries);
                    }

                    File.WriteAllText($"./perft_p{positionId}_d{depth}_total.json", JsonSerializer.Serialize(totalSummary, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading CSV: " + ex.Message);
                }
            }
        }
    }
}
