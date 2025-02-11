using GrandChessTree.Client.Worker;
using GrandChessTree.Client.Worker.Kernels;


try
{
    Worker.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex}");
}
