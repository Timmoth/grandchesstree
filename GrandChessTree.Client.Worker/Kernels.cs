using GrandChessTree.Shared.Precomputed;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using System.Runtime.CompilerServices;
using static ILGPU.IntrinsicMath;
using GrandChessTree.Shared.Helpers;
using System.Diagnostics;
using ILGPU.Runtime.Cuda;

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

            Console.WriteLine("Creating accelerator");
            var context = Context.Create(b => {
                b.Default().EnableAlgorithms().Math(MathMode.Fast).Optimize(OptimizationLevel.Release);
            });

            //var accelerator = context.CreateCPUAccelerator(0);
            var accelerator = context.CreateCudaAccelerator(0);

            // count moves kernel is used to determine how many positions each input position generates
            var countMovesKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, GpuAttackTable>(CountKernel.CountMovesKernel);
            // expand layer kernel populates the output buffers with each position reachable from each input position
            var expandLayerKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, BoardLayerBuffers, GpuAttackTable, TotalStatsLayerBuffers>(ExpandKernel.ExpandLayerKernel);
            // classify node kernel populates the stats buffer with the node types of each position in the output buffer
            var classifyNodeKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, TotalStatsLayerBuffers, GpuAttackTable>(Classify.ClassifyNodeKernel);
            // accumulate stats kernel collects the counts in the stats buffer into ulongs to make the reduction more efficient
            var accumulateStatsKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, StatsLayerBuffers, TotalStatsLayerBuffers>(AccumulateStatsKernel);


            var attackTables = GpuAttackTablesGenerator.Allocate(accelerator);

            var inputLayerSize = 1000000;
            var outputLayerSize = 50000000;

            var input_layer = new HostBoardLayerBuffers(accelerator, inputLayerSize);
            input_layer.Buffers.MemSetZero();

            var output_layer = new HostBoardLayerBuffers(accelerator, outputLayerSize);
            output_layer.Buffers.MemSetZero();
            var stats_layer = new HostStatsLayerBuffers(accelerator, outputLayerSize);
            stats_layer.Buffers.MemSetZero();

            var total_stats_layer = new HostTotalStatsLayerBuffers(accelerator, outputLayerSize);
            total_stats_layer.Buffers.MemSetZero();

            var (board, whiteToMove) = FenParser.Parse("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - ");
            var pawnOccupancy = new ulong[inputLayerSize];
            var knightOccupancy =new ulong[inputLayerSize];
            var bishopOccupancy =new ulong[inputLayerSize];
            var rookOccupancy = new ulong[inputLayerSize];
            var queenOccupancy = new ulong[inputLayerSize];
            var whiteOccupancy = new ulong[inputLayerSize];
            var blackOccupancy = new ulong[inputLayerSize];
            var castleRights = new byte[inputLayerSize];
            var enPassantFile = new byte[inputLayerSize];
            var whiteKingPos = new byte[inputLayerSize];
            var blackKingPos = new byte[inputLayerSize];
            var positionIndexes = new int[inputLayerSize];

            for(int i = 0; i < inputLayerSize; i++)
            {
                pawnOccupancy[i] = board.Pawn;
                knightOccupancy[i] = board.Knight;
                bishopOccupancy[i] = board.Bishop;
                rookOccupancy[i] = board.Rook;
                queenOccupancy[i] = board.Queen;
                whiteOccupancy[i] = board.White;
                blackOccupancy[i] = board.Black;
                castleRights[i] = (byte)board.CastleRights;
                enPassantFile[i] = board.EnPassantFile;
                whiteKingPos[i] = board.WhiteKingPos;
                blackKingPos[i] = board.BlackKingPos;
            }


            input_layer.Buffers.PawnOccupancy.CopyFromCPU(pawnOccupancy);
            input_layer.Buffers.KnightOccupancy.CopyFromCPU(knightOccupancy);
            input_layer.Buffers.BishopOccupancy.CopyFromCPU(bishopOccupancy);
            input_layer.Buffers.RookOccupancy.CopyFromCPU(rookOccupancy);
            input_layer.Buffers.QueenOccupancy.CopyFromCPU(queenOccupancy);
            input_layer.Buffers.WhiteOccupancy.CopyFromCPU(whiteOccupancy);
            input_layer.Buffers.BlackOccupancy.CopyFromCPU(blackOccupancy);
            input_layer.Buffers.CastleRights.CopyFromCPU(castleRights);
            input_layer.Buffers.EnPassantFile.CopyFromCPU(enPassantFile);
            input_layer.Buffers.WhiteKingPos.CopyFromCPU(whiteKingPos);
            input_layer.Buffers.BlackKingPos.CopyFromCPU(blackKingPos);
            input_layer.Buffers.PositionIndexes.CopyFromCPU(positionIndexes);

            var tempMemSize = accelerator.ComputeScanTempStorageSize<int>(input_layer.Buffers.PositionIndexes.Length);
            var tempBuffer = accelerator.Allocate1D<int>(tempMemSize);
            // Accumulate the number of moves for each position, producing the correct offset for each position in the output buffer
            var scan = accelerator.CreateScan<
                       int,
                       Stride1D.Dense,
                       Stride1D.Dense,
                       AddInt32>(ScanKind.Exclusive);

            for (int i = 0; i < 100; i++)
            {
                var sw = Stopwatch.StartNew();
                // First step is to populate the PositionIndexes with the number of moves possible for each position in the input
                countMovesKernel(inputLayerSize, input_layer.Buffers, attackTables);
                accelerator.Synchronize();
                var outputPositionCount = accelerator.Reduce<int, AddInt32>(accelerator.DefaultStream, input_layer.Buffers.PositionIndexes);
                Console.WriteLine($" count: {sw.ElapsedMilliseconds}ms {outputPositionCount} moves");
                sw.Restart();
                // Count the number of positions that will be populated in the output buffer

                scan(
                    accelerator.DefaultStream,
                    input_layer.Buffers.PositionIndexes,
                    input_layer.Buffers.PositionIndexes,
                    tempBuffer.View);
                accelerator.Synchronize();
                Console.WriteLine($"scan: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                // Generate each position reachable from the input position and insert into the output buffer at the correct offset
                expandLayerKernel(inputLayerSize, input_layer.Buffers, output_layer.Buffers, attackTables, total_stats_layer.Buffers);
                accelerator.Synchronize();
                Console.WriteLine($"expand {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                classifyNodeKernel(outputPositionCount, output_layer.Buffers, total_stats_layer.Buffers, attackTables);
                accelerator.Synchronize();
                Console.WriteLine($"classify {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                //// Accumulate the stats buffer into a condensed ulong buffer for each type of move that can be reduced easily into the total sum
                //accumulateStatsKernel(outputPositionCount, stats_layer.Buffers, total_stats_layer.Buffers);
      
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
                Console.WriteLine($"final {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                Console.WriteLine($"nodes: {totalNodes} captures: {totalCaptures} promotions: {totalPromotions} castles: {totalCastles}");
                Console.WriteLine($"totalEnpassant: {totalEnpassant} totalDirectCheck: {totalDirectCheck}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopLSB(this ref ulong b)
        {
            int i = IntrinsicMath.TrailingZeroCount(b);
            b &= b - 1;

            return i;
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
            outputStats.Nodes[idx] = (ulong)IntrinsicMath.PopCount(nodesPackedValue);
            outputStats.Captures[idx] = (ulong)IntrinsicMath.PopCount(capturesPackedValue);
            outputStats.Enpassant[idx] = (ulong)IntrinsicMath.PopCount(enpassantPackedValue);
            outputStats.Castles[idx] = (ulong)IntrinsicMath.PopCount(castlesPackedValue);
            outputStats.Promotions[idx] = (ulong)IntrinsicMath.PopCount(promotionsPackedValue);
            outputStats.DirectCheck[idx] = (ulong)IntrinsicMath.PopCount(directCheckPackedValue);
            outputStats.SingleDiscoveredCheck[idx] = (ulong)IntrinsicMath.PopCount(singleDiscoveredCheckPackedValue);
            outputStats.DirectDiscoveredCheck[idx] = (ulong)IntrinsicMath.PopCount(directDiscoveredCheckPackedValue);
            outputStats.DoubleDiscoveredCheck[idx] = (ulong)IntrinsicMath.PopCount(doubleDiscoveredCheckPackedValue);
            outputStats.DirectCheckmate[idx] = (ulong)IntrinsicMath.PopCount(directCheckmatePackedValue);
            outputStats.SingleDiscoveredCheckmate[idx] = (ulong)IntrinsicMath.PopCount(singleDiscoveredCheckmatePackedValue);
            outputStats.DirectDiscoverdCheckmate[idx] = (ulong)IntrinsicMath.PopCount(directDiscoverdCheckmatePackedValue);
            outputStats.DoubleDiscoverdCheckmate[idx] = (ulong)IntrinsicMath.PopCount(doubleDiscoverdCheckmatePackedValue);
        }
    }
}
