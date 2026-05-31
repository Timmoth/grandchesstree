using PerftChecker.Cli;
using Spectre.Console.Cli;

var app = new CommandApp<RunCommand>();
app.Configure(c =>
{
    c.SetApplicationName("perftcheck");
    // Keep in lockstep with the Version in PerftSuite.csproj and the
    // default 'version' input in publish-perftcheck.yml.
    c.SetApplicationVersion("0.3.0");
});
return await app.RunAsync(args);
