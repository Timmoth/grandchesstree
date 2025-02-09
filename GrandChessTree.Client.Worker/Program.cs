using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
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

}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex}");
}
