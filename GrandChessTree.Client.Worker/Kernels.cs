using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;

namespace GrandChessTree.Client.Worker
{
    public static class Kernels
    {
        public static void Run()
        {
            // Assuming a branching factor of 40, each position expands to the following number of positions:
            //1 ->  40
            //2 ->  1,600
            //3 ->  64,000
            //4 ->  2,560,000
            //5 ->  102,400,000

            // If we've searched any given position on the CPU to a depth of 3 we can safely allocate around 500k positions to the input buffer and 50m positions to the output buffer
            // At around 100 bytes per position all in that gives us: 5GB of vram ram used.
            // not including the 13 bytes per board in the output used for stats

            // Additionally we could load the first 40 moves into vram and keep switching which is the input / output
            // therefore searching multiple iterations on the GPU and reducing the overhead to transfer memory from the CPU -> GPU
            
            // Setup ILGPU
            var context = Context.Create(b => { b.Default().EnableAlgorithms().Math(MathMode.Fast); });
            var accelerator = context.CreateCPUAccelerator(0);

            // count moves kernel is used to determine how many positions each input position generates
            var countMovesKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers>(CountMovesKernel);
            // expand layer kernel populates the output buffers with each position reachable from each input position
            var expandLayerKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, BoardLayerBuffers>(ExpandLayerKernel);
            // classify node kernel populates the stats buffer with the node types of each position in the output buffer
            var classifyNodeKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, StatsLayerBuffers>(ClassifyNodeKernel);
            // accumulate stats kernel collects the counts in the stats buffer into ulongs to make the reduction more efficient
            var accumulateStatsKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, StatsLayerBuffers, TotalStatsLayerBuffers>(AccumulateStatsKernel);

            var input_layer = new HostBoardLayerBuffers(accelerator, 500_000);
            var output_layer = new HostBoardLayerBuffers(accelerator, 50_000_000);
            var stats_layer = new HostStatsLayerBuffers(accelerator, 50_000_000);
            var total_stats_layer = new HostTotalStatsLayerBuffers(accelerator, 50_000_000);

            // Placeholder, how many positions are loaded into the input buffer
            var inputPositionCount = 400;

            // First step is to populate the PositionIndexes with the number of moves possible for each position in the input
            countMovesKernel(inputPositionCount, input_layer.Buffers);

            // Count the number of positions that will be populated in the output buffer
            var outputPositionCount = accelerator.Reduce<int, AddInt32>(accelerator.DefaultStream, input_layer.Buffers.PositionIndexes);

            // Accumulate the number of moves for each position, producing the correct offset for each position in the output buffer
            var scan = accelerator.CreateScan<
                       int,
                       Stride1D.Dense,
                       Stride1D.Dense,
                       AddInt32>(ScanKind.Exclusive);

            var tempMemSize = accelerator.ComputeScanTempStorageSize<int>(input_layer.Buffers.PositionIndexes.Length);
            using (var tempBuffer = accelerator.Allocate1D<int>(tempMemSize))
            {
                scan(
                    accelerator.DefaultStream,
                    input_layer.Buffers.PositionIndexes,
                    input_layer.Buffers.PositionIndexes,
                    tempBuffer.View);
            }

            // Generate each position reachable from the input position and insert into the output buffer at the correct offset
            expandLayerKernel(inputPositionCount, input_layer.Buffers, output_layer.Buffers);
            // Fill the stats buffer by classifying the type of each position in the output buffer
            classifyNodeKernel(outputPositionCount, output_layer.Buffers, stats_layer.Buffers);
            // Accumulate the stats buffer into a condensed ulong buffer for each type of move that can be reduced easily into the total sum
            accumulateStatsKernel(outputPositionCount / 64, stats_layer.Buffers, total_stats_layer.Buffers);

            // Reduce the accumulated stats buffer into a single value for each node type
            ulong totalNodes = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.Nodes);
            ulong totalCaptures = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.Captures);
            ulong totalEnpassant = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.Enpassant);
            ulong totalCastles = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.Castles);
            ulong totalPromotions = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.Promotions);
            ulong totalDirectCheck = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.DirectCheck);
            ulong totalSingleDiscoveredCheck = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.SingleDiscoveredCheck);
            ulong totalDirectDiscoveredCheck = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.DirectDiscoveredCheck);
            ulong totalDoubleDiscoveredCheck = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.DoubleDiscoveredCheck);
            ulong totalDirectCheckmate = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.DirectCheckmate);
            ulong totalSingleDiscoveredCheckmate = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.SingleDiscoveredCheckmate);
            ulong totalDirectDiscoverdCheckmate = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.DirectDiscoverdCheckmate);
            ulong totalDoubleDiscoverdCheckmate = accelerator.Reduce<ulong, AddUInt64>(accelerator.DefaultStream, total_stats_layer.Buffers.DoubleDiscoverdCheckmate);

        }

        public static void CountMovesKernel(Index1D index, BoardLayerBuffers layer)
        {
            byte moveCount = 0;

            // count the number of possible moves

            layer.PositionIndexes[index] = moveCount;
        }


        public static void ExpandLayerKernel(Index1D idx, BoardLayerBuffers inputLayer, BoardLayerBuffers outputLayer)
        {
            int outputIndex = inputLayer.PositionIndexes[idx];

            // Generate all possible moves and write them to the output
        }

        public static void ClassifyNodeKernel(Index1D idx, BoardLayerBuffers boards, StatsLayerBuffers stats)
        {
            // Determine the node type and increment the board for each.
        }

        static void AccumulateStatsKernel(
                    Index1D idx,
                    StatsLayerBuffers inputStats,
                    TotalStatsLayerBuffers outputStats)
        {
            // Each thread processes 64 bytes at a time
            int baseIdx = idx * 64;

            ulong nodesPackedValue = 0;
            ulong capturesPackedValue = 0;
            ulong enpassantPackedValue = 0;
            ulong castlesPackedValue = 0;
            ulong promotionsPackedValue = 0;
            ulong directCheckPackedValue = 0;
            ulong singleDiscoveredCheckPackedValue = 0;
            ulong directDiscoveredCheckPackedValue = 0;
            ulong doubleDiscoveredCheckPackedValue = 0;
            ulong directCheckmatePackedValue = 0;
            ulong singleDiscoveredCheckmatePackedValue = 0;
            ulong directDiscoverdCheckmatePackedValue = 0;
            ulong doubleDiscoverdCheckmatePackedValue = 0;
           
            for (int i = 0; i < 64 && (baseIdx + i) < inputStats.BoardCount; i++)
            {
                nodesPackedValue |= (ulong)(inputStats.Nodes[baseIdx + i] & 1) << i;
                capturesPackedValue |= (ulong)(inputStats.Captures[baseIdx + i] & 1) << i;
                enpassantPackedValue |= (ulong)(inputStats.Enpassant[baseIdx + i] & 1) << i;
                castlesPackedValue |= (ulong)(inputStats.Castles[baseIdx + i] & 1) << i;
                promotionsPackedValue |= (ulong)(inputStats.Promotions[baseIdx + i] & 1) << i;
                directCheckPackedValue |= (ulong)(inputStats.DirectCheck[baseIdx + i] & 1) << i;
                singleDiscoveredCheckPackedValue |= (ulong)(inputStats.SingleDiscoveredCheck[baseIdx + i] & 1) << i;
                directDiscoveredCheckPackedValue |= (ulong)(inputStats.DirectDiscoveredCheck[baseIdx + i] & 1) << i;
                doubleDiscoveredCheckPackedValue |= (ulong)(inputStats.DoubleDiscoveredCheck[baseIdx + i] & 1) << i;
                directCheckmatePackedValue |= (ulong)(inputStats.DirectCheckmate[baseIdx + i] & 1) << i;
                singleDiscoveredCheckmatePackedValue |= (ulong)(inputStats.SingleDiscoveredCheckmate[baseIdx + i] & 1) << i;
                directDiscoverdCheckmatePackedValue |= (ulong)(inputStats.DirectDiscoverdCheckmate[baseIdx + i] & 1) << i;
                doubleDiscoverdCheckmatePackedValue |= (ulong)(inputStats.DoubleDiscoverdCheckmate[baseIdx + i] & 1) << i;
            }

            // Store packed value (each ulong represents 64 bytes)
            outputStats.Nodes[idx] = (ulong)XMath.PopCount(nodesPackedValue);
            outputStats.Captures[idx] = (ulong)XMath.PopCount(capturesPackedValue);
            outputStats.Enpassant[idx] = (ulong)XMath.PopCount(enpassantPackedValue);
            outputStats.Castles[idx] = (ulong)XMath.PopCount(castlesPackedValue);
            outputStats.Promotions[idx] = (ulong)XMath.PopCount(promotionsPackedValue);
            outputStats.DirectCheck[idx] = (ulong)XMath.PopCount(directCheckPackedValue);
            outputStats.SingleDiscoveredCheck[idx] = (ulong)XMath.PopCount(singleDiscoveredCheckPackedValue);
            outputStats.DirectDiscoveredCheck[idx] = (ulong)XMath.PopCount(directDiscoveredCheckPackedValue);
            outputStats.DoubleDiscoveredCheck[idx] = (ulong)XMath.PopCount(doubleDiscoveredCheckPackedValue);
            outputStats.DirectCheckmate[idx] = (ulong)XMath.PopCount(directCheckmatePackedValue);
            outputStats.SingleDiscoveredCheckmate[idx] = (ulong)XMath.PopCount(singleDiscoveredCheckmatePackedValue);
            outputStats.DirectDiscoverdCheckmate[idx] = (ulong)XMath.PopCount(directDiscoverdCheckmatePackedValue);
            outputStats.DoubleDiscoverdCheckmate[idx] = (ulong)XMath.PopCount(doubleDiscoverdCheckmatePackedValue);
        }
    }
}
