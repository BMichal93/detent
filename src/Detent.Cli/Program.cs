using System.CommandLine;

namespace Detent.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        var root = new RootCommand("Contract testing for MCP servers.")
        {
            VersionCommand.Create(),

            // capture (phase 1), diff (phase 2), verify and init (phase 3), and
            // explain (phase 5) are registered here as they land.
        };

        return root.Parse(args).Invoke();
    }
}
