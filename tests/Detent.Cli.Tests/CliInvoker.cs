using System.CommandLine;

namespace Detent.Cli.Tests;

/// <summary>
/// Runs a command in-process and captures what it would have printed.
/// </summary>
/// <remarks>
/// A command's <c>Create()</c> method is its only entry point besides
/// <c>Main</c> itself - there is no other public surface to test CLI logic
/// against, the way <c>DiffEngine.Diff</c> lets the rest of this project avoid
/// <c>InternalsVisibleTo</c> entirely. Running in-process rather than spawning
/// a real <c>detent.exe</c> subprocess per test avoids depending on a prior
/// build step and working-directory assumptions, at the cost of redirecting
/// <see cref="Console"/>, which is process-wide mutable state. Safe here only
/// because test methods within one xUnit class run sequentially by default;
/// a second Console-touching test class in this project would need a
/// collection attribute to keep that true.
/// </remarks>
internal static class CliInvoker
{
    public static async Task<CliResult> RunAsync(Command command, params string[] args)
    {
        // Parsed against the command itself, not a RootCommand wrapping it -
        // wrapping would make "diff" a subcommand token the caller has to pass
        // first, which is not how Program.cs's own root is shaped for a single
        // command under test.
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var exitCode = await command.Parse(args).InvokeAsync().ConfigureAwait(false);
            return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

internal sealed record CliResult(int ExitCode, string StdOut, string StdErr);
