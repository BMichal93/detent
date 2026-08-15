using System.CommandLine;
using Detent.Core.Policy;

namespace Detent.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("Contract testing for MCP servers.")
        {
            VersionCommand.Create(),
            CaptureCommand.Create(),
            DiffCommand.Create(),
            VerifyCommand.Create(),

            // init (phase 3) and explain (phase 5) are registered here as
            // they land.
        };

        try
        {
            return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Top-level handler: anything reaching here is a bug in detent.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Never a stack trace on stdout, and never mistaken for a finding.
            // Exit 4 means "report this", which is a different action for the
            // user than any other code this tool returns.
            Console.Error.WriteLine($"detent: internal error: {ex.Message}");
            return (int)ExitCode.InternalError;
        }
    }
}
