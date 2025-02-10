using GrandChessTree.Client.Worker;


try
{
    Kernels.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex}");
}
