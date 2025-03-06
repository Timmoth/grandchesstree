using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using GrandChessTree.Api.timescale;
using Newtonsoft.Json;

namespace GrandChessTree.Api.Performance
{
    public class WorkerStats
    {
        public long AccountId { get; set; }
        public int WorkerId { get; set; }
        public int Threads { get; set; }
        public int AllocatedMb { get; set; }
        public float Mips { get; set; }
        public PerftTaskType TaskType { get; set; }
        public long LastOnline { get; set; }
    }

    public class PerformanceTotal
    {
        [JsonPropertyName("workers")]
        public int Workers { get; set; }
        [JsonPropertyName("threads")]
        public int Threads { get; set; }

        [JsonPropertyName("allocated_mb")]
        public int AllocatedMb { get; set; }
        [JsonPropertyName("mips")]
        public float Mips { get; set; }
    }

    public static class PerformanceStatsService
    {
        private static readonly ConcurrentDictionary<(long accountId, int workerId), WorkerStats> _stats = new();

        public static PerformanceTotal GetTotals(long unixTimeSeconds)
        {
            var timeout = unixTimeSeconds - 600;
            var activeWorkerStats = _stats.Values.Where(s => s.LastOnline > timeout).ToList();
            var total = new PerformanceTotal();
            total.Workers = activeWorkerStats.Count();
            total.Threads = activeWorkerStats.Sum(s => s.Threads);
            total.AllocatedMb = activeWorkerStats.Sum(s => s.AllocatedMb);
            total.Mips = activeWorkerStats.Sum(s => s.Mips);
            return total;
        }

        public static void Update(long accountId,
            int workerId,
            int threads,
            int allocatedMb,
            float mips,
            PerftTaskType taskType,
            long unixSeconds)
        {
            var id = (accountId, workerId);
            _stats.AddOrUpdate(id, new WorkerStats()
            {
                AccountId = accountId,
                WorkerId = workerId,
                Threads = threads,
                AllocatedMb = allocatedMb,
                Mips = mips,
                TaskType = taskType,
                LastOnline = unixSeconds
            }, (i, o) =>
            {
                return new WorkerStats()
                {
                    AccountId = accountId,
                    WorkerId = workerId,
                    Threads = threads,
                    AllocatedMb = allocatedMb,
                    Mips = mips,
                    TaskType = taskType,
                    LastOnline = unixSeconds
                };
            });
        }
    }
}
