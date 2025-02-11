using GrandChessTree.Shared.Precomputed;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using System.Diagnostics;
using ILGPU.Runtime.Cuda;

namespace GrandChessTree.Client.Worker.Kernels
{
    public static class Worker
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

            var countL1MovesKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, GpuAttackTable>(CountMovesWhite.CountL1MovesKernel);
            var countL1MovesKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, GpuAttackTable>(CountMovesBlack.CountL1MovesKernel);
            var countL2MovesKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D<ulong, Stride1D.Dense>, GpuAttackTable>(CountMovesWhite.CountL2MovesKernel);
            var countL2MovesKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D<ulong, Stride1D.Dense>, GpuAttackTable>(CountMovesBlack.CountL2MovesKernel);
            var countL3MovesKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D<ulong, Stride1D.Dense>, GpuAttackTable>(CountMovesWhite.CountL3MovesKernel);
            var countL3MovesKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D<ulong, Stride1D.Dense>, GpuAttackTable>(CountMovesBlack.CountL3MovesKernel);

            var generateL1MovesKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, GpuAttackTable, ArrayView1D<ulong, Stride1D.Dense>>(GenerateMovesWhite.GenerateL1Moves);
            var generateL1MovesKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, GpuAttackTable, ArrayView1D<ulong, Stride1D.Dense>>(GenerateMovesBlack.GenerateL1Moves);
            var generateL2MovesKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D< ulong, Stride1D.Dense >,  GpuAttackTable, ArrayView1D<ulong, Stride1D.Dense>>(GenerateMovesWhite.GenerateL2Moves);
            var generateL2MovesKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D< ulong, Stride1D.Dense >,  GpuAttackTable, ArrayView1D<ulong, Stride1D.Dense>>(GenerateMovesBlack.GenerateL2Moves);
            var generateL3MovesKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D< ulong, Stride1D.Dense >, GpuAttackTable, ArrayView1D<ulong, Stride1D.Dense>>(GenerateMovesWhite.GenerateL3Moves);
            var generateL3MovesKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D< ulong, Stride1D.Dense >, GpuAttackTable, ArrayView1D<ulong, Stride1D.Dense>>(GenerateMovesBlack.GenerateL3Moves);

            var expandLayerKernelWhite = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D<ulong, Stride1D.Dense >, GpuAttackTable, TotalStatsLayerBuffers>(ExpandKernelWhite.ExpandLayerKernel);
            var expandLayerKernelBlack = accelerator.LoadAutoGroupedStreamKernel<Index1D, BoardLayerBuffers, ArrayView1D<ulong, Stride1D.Dense >, GpuAttackTable, TotalStatsLayerBuffers>(ExpandKernelBlack.ExpandLayerKernel);


            var attackTables = GpuAttackTablesGenerator.Allocate(accelerator);

            var inputLayerSize = 500;
            var l1Size = 50000;
            var l2Size = 1000000;
            var l3Size = 1600_0000;

            var input_layer = new HostBoardLayerBuffers(accelerator, inputLayerSize, l1Size, l2Size, l3Size);
            input_layer.Buffers.MemSetZero();

            ArrayView1D<ulong, Stride1D.Dense> layer1Moves = accelerator.Allocate1D<ulong>(l1Size);
            ArrayView1D<ulong, Stride1D.Dense> layer2Moves = accelerator.Allocate1D<ulong>(l2Size);
            ArrayView1D<ulong, Stride1D.Dense> layer3Moves = accelerator.Allocate1D<ulong>(l3Size);
            layer1Moves.MemSetToZero();
            layer2Moves.MemSetToZero();
            layer3Moves.MemSetToZero();

            var total_stats_layer = new HostTotalStatsLayerBuffers(accelerator, l3Size);
            total_stats_layer.Buffers.MemSetZero();

            var pawnOccupancy = new ulong[inputLayerSize];
            var knightOccupancy =new ulong[inputLayerSize];
            var bishopOccupancy =new ulong[inputLayerSize];
            var rookOccupancy = new ulong[inputLayerSize];
            var queenOccupancy = new ulong[inputLayerSize];
            var whiteOccupancy = new ulong[inputLayerSize];
            var blackOccupancy = new ulong[inputLayerSize];
            var nonOccupancyState = new uint[inputLayerSize];
  
        //    var (initialBoard, whiteToMove) = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            var (initialBoard, whiteToMove) = FenParser.Parse("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -");
            var boards = LeafNodeGenerator.GenerateLeafNodesBoards(ref initialBoard, 1, whiteToMove);
            for (int i = 0; i < boards.Count; i++)
            {
                var board = boards[i];
                pawnOccupancy[i] = board.Pawn;
                knightOccupancy[i] = board.Knight;
                bishopOccupancy[i] = board.Bishop;
                rookOccupancy[i] = board.Rook;
                queenOccupancy[i] = board.Queen;
                whiteOccupancy[i] = board.White;
                blackOccupancy[i] = board.Black;
                nonOccupancyState[i] = ((uint)board.CastleRights << 24) |
                                   ((uint)board.EnPassantFile << 16) |
                                   ((uint)board.WhiteKingPos << 8) |
                                   ((uint)board.BlackKingPos);
            }


            input_layer.Buffers.PawnOccupancy.CopyFromCPU(pawnOccupancy);
            input_layer.Buffers.KnightOccupancy.CopyFromCPU(knightOccupancy);
            input_layer.Buffers.BishopOccupancy.CopyFromCPU(bishopOccupancy);
            input_layer.Buffers.RookOccupancy.CopyFromCPU(rookOccupancy);
            input_layer.Buffers.QueenOccupancy.CopyFromCPU(queenOccupancy);
            input_layer.Buffers.WhiteOccupancy.CopyFromCPU(whiteOccupancy);
            input_layer.Buffers.BlackOccupancy.CopyFromCPU(blackOccupancy);
            input_layer.Buffers.NonOccupancyState.CopyFromCPU(nonOccupancyState);

            var tempMemSize = accelerator.ComputeScanTempStorageSize<int>(input_layer.Buffers.L1MoveIndexes.Length);
            var tempBufferL1 = accelerator.Allocate1D<int>(tempMemSize);
            // Accumulate the number of moves for each position, producing the correct offset for each position in the output buffer
            var scanL1 = accelerator.CreateScan<
                       int,
                       Stride1D.Dense,
                       Stride1D.Dense,
                       AddInt32>(ScanKind.Exclusive);

            var tempMemSizel2 = accelerator.ComputeScanTempStorageSize<int>(input_layer.Buffers.L2MoveIndexes.Length);
            var tempBufferL2 = accelerator.Allocate1D<int>(tempMemSizel2);
            // Accumulate the number of moves for each position, producing the correct offset for each position in the output buffer
            var scanL2 = accelerator.CreateScan<
                       int,
                       Stride1D.Dense,
                       Stride1D.Dense,
                       AddInt32>(ScanKind.Exclusive);

            var tempMemSizel3 = accelerator.ComputeScanTempStorageSize<int>(input_layer.Buffers.L3MoveIndexes.Length);
            var tempBufferL3 = accelerator.Allocate1D<int>(tempMemSizel3);
            // Accumulate the number of moves for each position, producing the correct offset for each position in the output buffer
            var scanL3 = accelerator.CreateScan<
                       int,
                       Stride1D.Dense,
                       Stride1D.Dense,
                       AddInt32>(ScanKind.Exclusive);

            for (int i = 0; i < 100; i++)
            {
                tempBufferL1.MemSetToZero();
                tempBufferL2.MemSetToZero();
                tempBufferL3.MemSetToZero();
                input_layer.Buffers.MemSetZero2();

                var sw = Stopwatch.StartNew();
                var swTotal = Stopwatch.StartNew();
                // First step is to populate the PositionIndexes with the number of moves possible for each position in the input
                countL1MovesKernelBlack(inputLayerSize, input_layer.Buffers, attackTables);
                accelerator.Synchronize();
                Console.WriteLine($"l1 count: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                var layer1MoveCount = accelerator.Reduce<int, AddInt32>(accelerator.DefaultStream, input_layer.Buffers.L1MoveIndexes);
                accelerator.Synchronize();
                Console.WriteLine($"l1 reduce: {sw.ElapsedMilliseconds}ms {layer1MoveCount} moves");
                sw.Restart();
                // Count the number of positions that will be populated in the output buffer

                scanL1(
                    accelerator.DefaultStream,
                    input_layer.Buffers.L1MoveIndexes,
                    input_layer.Buffers.L1MoveIndexes,
                    tempBufferL1.View);
                accelerator.Synchronize();
                Console.WriteLine($"l1 scan: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                // Generate each position reachable from the input position and insert into the output buffer at the correct offset
                generateL1MovesKernelBlack(inputLayerSize, input_layer.Buffers, attackTables, layer1Moves);
                accelerator.Synchronize();
                Console.WriteLine($"l1 generate: {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // First step is to populate the PositionIndexes with the number of moves possible for each position in the input
                countL2MovesKernelWhite(layer1MoveCount, input_layer.Buffers, layer1Moves, attackTables);
                accelerator.Synchronize();
                Console.WriteLine($"l2 count: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                var layer2MoveCount = accelerator.Reduce<int, AddInt32>(accelerator.DefaultStream, input_layer.Buffers.L2MoveIndexes);
                accelerator.Synchronize();
                Console.WriteLine($"l2 reduce: {sw.ElapsedMilliseconds}ms {layer2MoveCount} moves");
                sw.Restart();
                // Count the number of positions that will be populated in the output buffer

                scanL2(
                    accelerator.DefaultStream,
                    input_layer.Buffers.L2MoveIndexes,
                    input_layer.Buffers.L2MoveIndexes,
                    tempBufferL2.View);
                accelerator.Synchronize();
                Console.WriteLine($"l2 scan: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                // Generate each position reachable from the input position and insert into the output buffer at the correct offset
                generateL2MovesKernelWhite(layer1MoveCount, input_layer.Buffers, layer1Moves, attackTables, layer2Moves);
                accelerator.Synchronize();
                Console.WriteLine($"l2 generate: {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // First step is to populate the PositionIndexes with the number of moves possible for each position in the input
                countL3MovesKernelBlack(layer2MoveCount, input_layer.Buffers, layer2Moves, attackTables);
                accelerator.Synchronize();
                Console.WriteLine($"l3 count: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                var layer3MoveCount = accelerator.Reduce<int, AddInt32>(accelerator.DefaultStream, input_layer.Buffers.L3MoveIndexes);
                accelerator.Synchronize();
                Console.WriteLine($"l3 reduce: {sw.ElapsedMilliseconds}ms {layer3MoveCount} moves");
                sw.Restart();
                // Count the number of positions that will be populated in the output buffer

                scanL3(
                    accelerator.DefaultStream,
                    input_layer.Buffers.L3MoveIndexes,
                    input_layer.Buffers.L3MoveIndexes,
                    tempBufferL3.View);
                accelerator.Synchronize();
                Console.WriteLine($"l3 scan: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
                // Generate each position reachable from the input position and insert into the output buffer at the correct offset
                generateL3MovesKernelBlack(layer2MoveCount, input_layer.Buffers, layer2Moves, attackTables, layer3Moves);
                accelerator.Synchronize();
                Console.WriteLine($"l3 generate: {sw.ElapsedMilliseconds}ms");
                sw.Restart();


                expandLayerKernelWhite(layer3MoveCount, input_layer.Buffers, layer3Moves, attackTables, total_stats_layer.Buffers);
                accelerator.Synchronize();
                Console.WriteLine($"l3 expand: {sw.ElapsedMilliseconds}ms");
                sw.Restart();

 
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
                Console.WriteLine($"combine {sw.ElapsedMilliseconds}ms");
                Console.WriteLine($"total: {swTotal.ElapsedMilliseconds}ms");
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

    }
}
