using System.CommandLine;
using Detent.Core.Policy;

namespace Detent.Cli;

internal static class VersionCommand
{
    public static Command Create()
    {
        var command = new Command("version", "Print the detent version and exit.");

        command.SetAction(_ =>
        {
            Console.WriteLine(ToolVersion.Current);
            return (int)ExitCode.Pass;
        });

        return command;
    }
}
