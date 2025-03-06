using System.Diagnostics;

namespace GrandChessTree.Client
{
    public static class Benchmarks
    {
        private static double PerformBenchmark(int iterations)
        {
            // Use stackalloc to avoid heap allocation and GC interference.
            Span<int> arr = stackalloc int[1000];

            // Initialize integer and floating-point values.
            int a = 1, b = 2, c = 3, d = 4;
            double x = 1.1, y = 2.2, z = 3.3;

            // Counters for each operation type.
            // These counters will be weighted to reflect their relative cost.
            long integerOps = 0, floatOps = 0, memoryOps = 0;

            for (int i = 0; i < iterations; i++)
            {
                // --- Integer Operations ---
                // Executed every iteration; these serve as our baseline.
                a = b + c;
                b = a * d;
                c = (b % 7) + a;
                d = c ^ b;
                a += (d % 3); // Incorporate conditional change to avoid complete optimization.
                integerOps += 5; // We count 5 basic integer operations per iteration.

                // --- Floating-Point Operations ---
                // Executed less frequently to balance their higher individual cost.
                if ((i & 0b111111) == 0) // This is equivalent to i % 50 == 0.
                {
                    x = Math.Sqrt(y) + Math.Sin(z);
                    y = Math.Pow(x, 2.0) - Math.Log(y + 1.0);
                    z = Math.Cos(y) * x;
                    floatOps += 3; // Count 3 floating-point operations.
                }

                // --- Memory Operations ---
                // Also executed less frequently; note that we reuse the array to avoid GC overhead.
                if ((i & 0b11111111) == 0) // This is equivalent to i % 250 == 0.
                {
                    for (int j = 0; j < arr.Length; j++)
                        arr[j] = j * i;
                    memoryOps += 2; // Count 2 memory-related operations.
                }
            }

            // --- Weighting ---
            // We assign a weight to each operation type to approximate its relative cost:
            // - Integer operations are our baseline (weight = 1).
            // - Floating-point operations are assumed to be roughly 10x more costly.
            // - Memory operations (cache-bound) are assumed to be roughly 5x cost.
            double weightedOps = integerOps + (floatOps * 10) + (memoryOps * 5);

            // Convert the weighted operation count to a "MIPS" metric (Million Instructions Per Second).
            return weightedOps / 1_000_000.0;
        }


        // Synchronization barrier to start all tasks at once.
        private static Barrier _barrier;

        public static float Mips = 0;
        public static double RunBenchmark(int threadCount, int iterations)
        {
            // Warm-up run: execute the benchmark once to let JIT compile the code.
            PerformBenchmark(iterations);

            _barrier = new Barrier(threadCount);

            var tasks = new Task<double>[threadCount];
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    // Wait until all threads are ready to start.
                    _barrier.SignalAndWait();
                    return PerformBenchmark(iterations);
                });
            }

            Task.WaitAll(tasks);
            sw.Stop();

            double totalScore = tasks.Sum(t => t.Result);
            double elapsedSeconds = sw.Elapsed.TotalSeconds;
            double finalMIPS = totalScore / elapsedSeconds; // Higher is better

            Mips = (float)finalMIPS;
            return finalMIPS;
        }
    }
}
