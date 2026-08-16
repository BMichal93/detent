using System.CommandLine;
using Detent.Core.Capture;
using Detent.Core.Contracts;
using Detent.Core.Diff;
using Detent.Core.Policy;
using Detent.Core.Security;
using Detent.Transport;

namespace Detent.Cli;

/// <summary>
/// <c>detent verify</c>: diffs a baseline against a target, then narrows and
/// checks the result against a consumer's contract before gating.
/// </summary>
/// <remarks>
/// The pipeline is <c>diff</c>'s, with three contract-specific stages layered
/// on in the order <c>docs/arch/diff-rules.md</c> §8 and §12 specify: narrow
/// and promote via <see cref="ContractScope.Apply"/>, add whatever
/// <see cref="ContractScope.CheckAssumptions"/> finds directly against the
/// candidate, then drop what an active <see cref="ContractScope.ApplySuppressions"/>
/// entry covers. Policy evaluation happens last, against whichever policy the
/// contract and the CLI flags resolve to.
/// </remarks>
internal static class VerifyCommand
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

        var contract = new Option<string>("--contract")
        {
            Description = "Path to the contract YAML file to verify against.",
            Required = true,
        };

        var format = new Option<string>("--format")
        {
            Description = "Output format: human, json, sarif, or markdown.",
            DefaultValueFactory = _ => "human",
        };

        var failOn = new Option<string[]>("--fail-on")
        {
            Description = "Severities that fail the build. Repeatable. Overrides the contract's own policy.",
            AllowMultipleArgumentsPerToken = true,
        };

        var warnOn = new Option<string[]>("--warn-on")
        {
            Description = "Severities that warn without failing. Repeatable. Overrides the contract's own policy.",
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

        var command = new Command("verify", "Verify a target against a consumer contract and gate on the result.")
        {
            baseline,
            target,
            contract,
            format,
            failOn,
            warnOn,
            allowHost,
            insecure,
        };

        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(
            parseResult.GetValue(baseline)!,
            parseResult.GetValue(target)!,
            parseResult.GetValue(contract)!,
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
        string contractPath,
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

        Contract contract;

        try
        {
            var text = await File.ReadAllTextAsync(Path.GetFullPath(contractPath), cancellationToken).ConfigureAwait(false);
            contract = ContractYamlReader.Read(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ContractFormatException)
        {
            return CliOutput.Fail(ExitCode.UsageError, $"Cannot read contract {Sanitizer.SanitizeForMessage(contractPath)}: {ex.Message}");
        }

        GatePolicy policy;

        try
        {
            var contractDefault = new GatePolicy
            {
                FailOn = contract.Policy?.FailOn ?? GatePolicy.Default.FailOn,
                WarnOn = contract.Policy?.WarnOn ?? GatePolicy.Default.WarnOn,
            };

            policy = PolicyOptions.Resolve(failOnNames, warnOnNames, contractDefault);
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

        var findings = new List<Finding>();
        findings.AddRange(ContractScope.Apply(DiffEngine.Diff(before, after), contract));
        findings.AddRange(ContractScope.CheckAssumptions(after, contract));

        // The one place Detent.Cli reads the clock on Detent.Core's behalf:
        // Detent.Core takes no clock of its own, so "today" is decided here
        // and passed in, never read inside the pure suppression logic itself.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var suppressed = ContractScope.ApplySuppressions(findings, contract.Policy, today);

        var result = PolicyEvaluator.Evaluate(suppressed, policy);

        Console.Out.Write(OutputFormat.Render(format, result));

        return (int)result.ExitCode;
    }
}
