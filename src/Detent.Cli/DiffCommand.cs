using System.CommandLine;
using Detent.Core.Capture;
using Detent.Core.Diff;
using Detent.Core.Policy;
using Detent.Core.Security;
using Detent.Transport;

namespace Detent.Cli;

/// <summary>
/// <c>detent diff</c>: compares a baseline snapshot against a target and gates
/// on the result.
/// </summary>
/// <remarks>
/// MCPC402 is not evaluated; see the remarks on <see cref="DiffEngine"/> and
/// ADR-0008. Every other row in <c>docs/arch/diff-rules.md</c> is.
/// </remarks>
internal static class DiffCommand
{
    public static Command Create()
    {
        var baseline = new Argument<string>("baseline")
        {
            Description = "Path to the baseline snapshot to compare from.",
        };

        var target = new Argument<string>("target")
        {
            Description = "URL of a live MCP endpoint, or a path to another snapshot, to compare to.",
        };

        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, sarif, or markdown.",
            DefaultValueFactory = _ => "human",
        };

        var failOn = new Option<string[]>("--fail-on")
        {
            Description = "Severities that fail the build. Repeatable. Defaults to breaking,security.",
            AllowMultipleArgumentsPerToken = true,
        };

        var warnOn = new Option<string[]>("--warn-on")
        {
            Description = "Severities that warn without failing. Repeatable. Defaults to behavioural,notice,unanalysable.",
            AllowMultipleArgumentsPerToken = true,
        };

        var allowHost = new Option<string[]>("--allow-host")
        {
            Description = "Permit a host the address guard would otherwise refuse. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };

        var insecure = new Option<bool>("--insecure")
        {
            Description = "Skip certificate validation. Loopback targets only, never in CI.",
        };

        var command = new Command("diff", "Compare a baseline snapshot against a target and gate on the result.")
        {
            baseline,
            target,
            format,
            failOn,
            warnOn,
            allowHost,
            insecure,
        };

        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(
            parseResult.GetValue(baseline)!,
            parseResult.GetValue(target)!,
            parseResult.GetValue(format)!,
            parseResult.GetValue(failOn) ?? [],
            parseResult.GetValue(warnOn) ?? [],
            parseResult.GetValue(allowHost) ?? [],
            parseResult.GetValue(insecure),
            cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string baselinePath,
        string target,
        string format,
        string[] failOnNames,
        string[] warnOnNames,
        string[] allowedHosts,
        bool insecure,
        CancellationToken cancellationToken)
    {
        if (!OutputFormat.IsKnown(format))
        {
            return CliOutput.Fail(ExitCode.UsageError, $"Unknown --format '{Sanitizer.SanitizeForMessage(format)}'. Use {OutputFormat.Known}.");
        }

        GatePolicy policy;

        try
        {
            policy = PolicyOptions.Resolve(failOnNames, warnOnNames, GatePolicy.Default);
        }
        catch (FormatException ex)
        {
            return CliOutput.Fail(ExitCode.UsageError, ex.Message);
        }

        Snapshot before;
        Snapshot after;

        try
        {
            before = await SnapshotResolution.ReadSnapshotAsync(baselinePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnapshotFormatException)
        {
            return CliOutput.Fail(ExitCode.UsageError, $"Cannot read baseline {Sanitizer.SanitizeForMessage(baselinePath)}: {ex.Message}");
        }

        try
        {
            after = await SnapshotResolution.ResolveTargetAsync(target, allowedHosts, insecure, cancellationToken).ConfigureAwait(false);
        }
        catch (TransportException ex)
        {
            // Distinct from a policy failure on purpose. A flaky network that
            // reads as a broken contract teaches people to ignore the gate.
            return CliOutput.Fail(ExitCode.TransportFailure, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return CliOutput.Fail(ExitCode.TransportFailure, "Cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnapshotFormatException)
        {
            return CliOutput.Fail(ExitCode.UsageError, $"Cannot read target {Sanitizer.SanitizeForMessage(target)}: {ex.Message}");
        }

        var findings = DiffEngine.Diff(before, after);
        var result = PolicyEvaluator.Evaluate(findings, policy);

        Console.Out.Write(OutputFormat.Render(format, result));

        return (int)result.ExitCode;
    }
}
