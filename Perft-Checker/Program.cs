using PerftChecker.Cli;
using Spectre.Console.Cli;

var app = new CommandApp<RunCommand>();
app.Configure(c =>
{
    c.SetApplicationName("perftcheck");
    c.SetApplicationVersion("0.1.0");
});
return await app.RunAsync(args);
