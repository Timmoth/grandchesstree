using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using Npgsql;

namespace GrandChessTree.Toolkit
{
    internal class DbDumper
    {

        public static async Task DeduplicateTasksWithIllegalEP(int positionId, int depth)
        {

            var (initialBoard, whiteToMove) = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            var divideResults = LeafNodeGenerator.GenerateLeafNodes(ref initialBoard, 4, whiteToMove);
            var hashes = divideResults.ToDictionary(d => d.hash, d => 1);
            var fens = divideResults.ToDictionary(d => d.fen, d => 1);


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
                await using var writer1 = new StreamWriter("keep.csv", false, Encoding.UTF8);
                await using var writer2 = new StreamWriter("remove.csv", false, Encoding.UTF8);
                await using var writer3 = new StreamWriter("add.csv", false, Encoding.UTF8);
                var copySql = @$"COPY (SELECT 
                                id, hash, fen
                                FROM public.perft_items
                                WHERE depth = {depth} AND root_position_id = {positionId}) TO STDOUT WITH CSV HEADER";

                var reader = await conn.BeginTextExportAsync(copySql);
                string? line;

                var tokeep = new HashSet<string>();
                var toRemove = new HashSet<string>();
                var unknown = new HashSet<string>();

                while((line = await reader.ReadLineAsync()) != null)
                {
                    var parts = line.Split(',');
                    if (ulong.TryParse(parts[0], out var id) && ulong.TryParse(parts[1], out var val))
                    {
                        if (fens.ContainsKey(parts[2]))
                        {
                            tokeep.Add(parts[2]);
                        }
                        else
                        {
                            toRemove.Add(parts[2]);
                        }
                    }
                }

                foreach(var fen in fens)
                {
                    if (!tokeep.Contains(fen.Key) && !toRemove.Contains(fen.Key))
                    {
                        unknown.Add(fen.Key);
                    }
                }

                foreach(var fen in tokeep)
                {
                    writer1.WriteLine(fen);
                }

                foreach (var fen in toRemove)
                {
                    writer2.WriteLine(fen);
                }
                foreach (var fen in unknown)
                {
                    writer3.WriteLine(fen);
                }




                Console.WriteLine($"Data successfully exported to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during db dump: {ex.Message}");
            }
        }

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
            public ulong Nodes { get; set; }
            [Name("captures")]
            public ulong Captures { get; set; }
            [Name("enpassants")]
            public ulong Enpassants { get; set; }
            [Name("castles")]
            public ulong Castles { get; set; }
            [Name("promotions")]
            public ulong Promotions { get; set; }
            [Name("direct_checks")]
            public ulong DirectChecks { get; set; }
            [Name("single_discovered_check")]
            public ulong SingleDiscoveredCheck { get; set; }
            [Name("direct_discovered_check")]
            public ulong DirectDiscoveredCheck { get; set; }
            [Name("double_discovered_check")]
            public ulong DoubleDiscoveredCheck { get; set; }
            [Name("direct_checkmate")]
            public ulong DirectCheckmate { get; set; }
            [Name("single_discovered_checkmate")]
            public ulong SingleDiscoveredCheckmate { get; set; }
            [Name("direct_discoverd_checkmate")]
            public ulong DirectDiscoverdCheckmate { get; set; }
            [Name("double_discoverd_checkmate")]
            public ulong DoubleDiscoverdCheckmate { get; set; }
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
            public ulong Nodes { get; set; } 
            [Name("captures")]
            [JsonPropertyName("captures")]
            public ulong Captures { get; set; }
            [Name("enpassants")]
            [JsonPropertyName("enpassants")]
            public ulong Enpassants { get; set; }
            [Name("castles")]
            [JsonPropertyName("castles")]
            public ulong Castles { get; set; }
            [Name("promotions")]
            [JsonPropertyName("promotions")]
            public ulong Promotions { get; set; }
            [Name("direct_checks")]
            [JsonPropertyName("direct_checks")]
            public ulong DirectChecks { get; set; }
            [Name("single_discovered_check")]
            [JsonPropertyName("single_discovered_check")]
            public ulong SingleDiscoveredCheck { get; set; }
            [Name("direct_discovered_check")]
            [JsonPropertyName("direct_discovered_check")]
            public ulong DirectDiscoveredCheck { get; set; }
            [Name("double_discovered_check")]
            [JsonPropertyName("double_discovered_check")]
            public ulong DoubleDiscoveredCheck { get; set; }
            [Name("total_checks")]
            [JsonPropertyName("total_checks")]
            public ulong TotalChecks { get; set; }
            [Name("direct_checkmate")]
            [JsonPropertyName("direct_checkmate")]
            public ulong DirectCheckmate { get; set; }
            [Name("single_discovered_checkmate")]
            [JsonPropertyName("single_discovered_checkmate")]
            public ulong SingleDiscoveredCheckmate { get; set; }
            [Name("direct_discoverd_checkmate")]
            [JsonPropertyName("direct_discoverd_checkmate")]
            public ulong DirectDiscoverdCheckmate { get; set; }
            [Name("double_discoverd_checkmate")]
            [JsonPropertyName("double_discoverd_checkmate")]
            public ulong DoubleDiscoverdCheckmate { get; set; }
            [Name("total_mates")]
            [JsonPropertyName("total_mates")]
            public ulong TotalMates { get; set; }
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
            [Name("position")]
            [JsonPropertyName("position")]
            public int Position { get; set; }

            [Name("depth")]
            [JsonPropertyName("depth")]
            public int Depth { get; set; }

            [Name("nodes")]
            [JsonPropertyName("nodes")]
            public ulong Nodes { get; set; }
            [Name("captures")]
            [JsonPropertyName("captures")]
            public ulong Captures { get; set; }
            [Name("enpassants")]
            [JsonPropertyName("enpassants")]
            public ulong Enpassants { get; set; }
            [Name("castles")]
            [JsonPropertyName("castles")]
            public ulong Castles { get; set; }
            [Name("promotions")]
            [JsonPropertyName("promotions")]
            public ulong Promotions { get; set; }
            [Name("direct_checks")]
            [JsonPropertyName("direct_checks")]
            public ulong DirectChecks { get; set; }
            [Name("single_discovered_checks")]
            [JsonPropertyName("single_discovered_checks")]
            public ulong SingleDiscoveredChecks { get; set; }
            [Name("direct_discovered_checks")]
            [JsonPropertyName("direct_discovered_checks")]
            public ulong DirectDiscoveredChecks { get; set; }
            [Name("double_discovered_checks")]
            [JsonPropertyName("double_discovered_checks")]
            public ulong DoubleDiscoveredChecks { get; set; }
            [Name("total_checks")]
            [JsonPropertyName("total_checks")]
            public ulong TotalChecks { get; set; }
            [Name("direct_mates")]
            [JsonPropertyName("direct_mates")]
            public ulong DirectMates { get; set; }
            [Name("single_discovered_mates")]
            [JsonPropertyName("single_discovered_mates")]
            public ulong SingleDiscoveredMates { get; set; }
            [Name("direct_discovered_mates")]
            [JsonPropertyName("direct_discovered_mates")]
            public ulong DirectDiscoverdMates { get; set; }
            [Name("double_discovered_mates")]
            [JsonPropertyName("double_discovered_mates")]
            public ulong DoubleDiscoverdMates { get; set; }
            [Name("total_mates")]
            [JsonPropertyName("total_mates")]
            public ulong TotalMates { get; set; }

            public void Add(PerfTaskSummary other)
            {
                Nodes += other.Nodes;
                Captures += other.Captures;
                Enpassants += other.Enpassants;
                Castles += other.Castles;
                Promotions += other.Promotions;
                DirectChecks += other.DirectChecks;
                SingleDiscoveredChecks += other.SingleDiscoveredCheck;
                DirectDiscoveredChecks += other.DirectDiscoveredCheck;
                DoubleDiscoveredChecks += other.DoubleDiscoveredCheck;
                DirectMates += other.DirectCheckmate;
                SingleDiscoveredMates += other.SingleDiscoveredCheckmate;
                DirectDiscoverdMates += other.DirectDiscoverdCheckmate;
                DoubleDiscoverdMates += other.DoubleDiscoverdCheckmate;

                TotalChecks += other.DirectChecks;
                TotalChecks += other.SingleDiscoveredCheck;
                TotalChecks += other.DirectDiscoveredCheck;
                TotalChecks += other.DoubleDiscoveredCheck;

                TotalMates += other.DirectCheckmate;
                TotalMates += other.SingleDiscoveredCheckmate;
                TotalMates += other.DirectDiscoverdCheckmate;
                TotalMates += other.DoubleDiscoverdCheckmate;
            }

            public void Add(Summary other)
            {
                Nodes += other.Nodes;
                Captures += other.Captures;
                Enpassants += other.Enpassant;
                Castles += other.Castles;
                Promotions += other.Promotions;
                DirectChecks += other.DirectCheck;
                SingleDiscoveredChecks += other.SingleDiscoveredCheck;
                DirectDiscoveredChecks += other.DirectDiscoveredCheck;
                DoubleDiscoveredChecks += other.DoubleDiscoveredCheck;
                DirectMates += other.DirectCheckmate;
                SingleDiscoveredMates += other.SingleDiscoveredCheckmate;
                DirectDiscoverdMates += other.DirectDiscoverdCheckmate;
                DoubleDiscoverdMates += other.DoubleDiscoverdCheckmate;

                TotalChecks += other.DirectCheck;
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
            public ulong Nodes { get; set; }

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
                    totalSummary.Position = positionId;
                    totalSummary.Depth = depth;

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
                            Nodes = (ulong)g.Sum(g => (decimal)g.Nodes),
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

        public static async Task QuickSearch(int positionId, int depth, string fen)
        {
            try
            {
                var (initialBoard, whiteToMove) = FenParser.Parse(fen);

                Summary summary = default;
                unsafe
                {
                    Perft.HashTable = Perft.AllocateHashTable(256);
                }

                var startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var sw = Stopwatch.StartNew();
                Perft.PerftRoot(ref initialBoard, ref summary, depth, whiteToMove);

                var ms = sw.ElapsedMilliseconds;
                var finishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var s = (float)ms / 1000;
                var nps = summary.Nodes / s;

                var totalSummary = new PerfTaskTotalSummary()
                {
                    Position = positionId,
                    Depth = depth,
                    Tasks = 1,
                    StartedAt = startedAt,
                    FinishedAt = finishedAt,
                    Contributors = new List<ContributorSummary>()
                    {
                        new ContributorSummary()
                        {
                            Id = 1,
                            Name = "Timmoth",
                            Nodes = summary.Nodes,
                            Tasks = 1,
                            ComputeTime = finishedAt - startedAt
                        }
                    }
                };
                totalSummary.Add(summary);


                Console.WriteLine($"final: {totalSummary.Nodes}");

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
