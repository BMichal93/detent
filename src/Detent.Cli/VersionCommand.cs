using System.CommandLine;
using System.Reflection;
using Detent.Core.Policy;

namespace Detent.Cli;

internal static class VersionCommand
{
    public static Command Create()
    {
        var command = new Command("version", "Print the detent version and exit.");

        command.SetAction(_ =>
        {
            Console.WriteLine(GetInformationalVersion());
            return (int)ExitCode.Pass;
        });

        return command;
    }

    private static string GetInformationalVersion()
    {
        var attribute = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attribute is null)
        {
            return "unknown";
        }

        // The SDK appends "+<commit sha>" to the informational version. Useful in
        // a bug report, noise on a version line, so split it off.
        var version = attribute.InformationalVersion;
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
