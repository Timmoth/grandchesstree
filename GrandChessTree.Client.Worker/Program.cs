using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;


try
{

    if (args.Length == 0)
    {
        //Console.WriteLine("No arguments passed.");
        //return;
    }

    RuntimeHelpers.RunClassConstructor(typeof(AttackTables).TypeHandle);
    RuntimeHelpers.RunClassConstructor(typeof(Perft).TypeHandle);
    
    Console.WriteLine("loading position");
    var (board, whiteToMove) = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
    Console.WriteLine("allocating hash table");

    unsafe
    {
        Perft.HashTable = Perft.AllocateHashTable();
    }
    
    var context = Context.Create(b => { b.Default().EnableAlgorithms().Math(MathMode.Fast); });
    var accelerator = context.CreateCPUAccelerator(0);

    var boardSizeBytes = 8 * 8;
    
    var inputBoards = new Board[5000];
    var inputLength = 10_000;
    
    var input_boards = accelerator.Allocate1D<Board>(inputLength); 
    var layer1_boards = accelerator.Allocate1D<Board>(inputLength * 100); 
    var expandLayerKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Board>, ArrayView<Board>>(ExpandLayer);
    var countLayerKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Board>>(CountLayer);

    input_boards.View.CopyFromCPU(inputBoards);
    expandLayerKernel(inputLength, input_boards.View, layer1_boards.View);
    countLayerKernel(inputLength, layer1_boards.View);
    
    static void ExpandLayer(
        Index1D index,
        ArrayView<Board> input_layer,
        ArrayView<Board> output_layer)
    {
        // This kernel takes a single board from the input array
        // And adds each reachable board into the output array
    }
    
    static void CountLayer(
        Index1D index,
        ArrayView<Board> input_layer)
    {
        // This kernel takes a single board from the input array
        // And counts all possible nodes that can be reached
    }

    }
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex}");
}
