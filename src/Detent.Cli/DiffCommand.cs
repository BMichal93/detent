using System.CommandLine;
using Detent.Core.Capture;
using Detent.Core.Diff;
using Detent.Core.Policy;
using Detent.Core.Security;
using Detent.Formats;
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
            Description = "Output format: human or json.",
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
        if (format is not ("human" or "json"))
        {
            return Fail(ExitCode.UsageError, $"Unknown --format '{Sanitizer.SanitizeForMessage(format)}'. Use human or json.");
        }

        GatePolicy policy;

        try
        {
            policy = ParsePolicy(failOnNames, warnOnNames);
        }
        catch (FormatException ex)
        {
            return Fail(ExitCode.UsageError, ex.Message);
        }

        Snapshot before;
        Snapshot after;

        try
        {
            before = await ReadSnapshotAsync(baselinePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnapshotFormatException)
        {
            return Fail(ExitCode.UsageError, $"Cannot read baseline {Sanitizer.SanitizeForMessage(baselinePath)}: {ex.Message}");
        }

        try
        {
            after = await ResolveTargetAsync(target, allowedHosts, insecure, cancellationToken).ConfigureAwait(false);
        }
        catch (TransportException ex)
        {
            // Distinct from a policy failure on purpose. A flaky network that
            // reads as a broken contract teaches people to ignore the gate.
            return Fail(ExitCode.TransportFailure, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Fail(ExitCode.TransportFailure, "Cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnapshotFormatException)
        {
            return Fail(ExitCode.UsageError, $"Cannot read target {Sanitizer.SanitizeForMessage(target)}: {ex.Message}");
        }

        var findings = DiffEngine.Diff(before, after);
        var result = PolicyEvaluator.Evaluate(findings, policy);

        Console.Out.Write(format == "json" ? JsonRenderer.Render(result) : HumanRenderer.Render(result));

        return (int)result.ExitCode;
    }

    /// <summary>
    /// A target is a URL if it parses as absolute with an http or https
    /// scheme; anything else is read as a snapshot file. There is no flag to
    /// force one or the other.
    /// </summary>
    /// <remarks>
    /// Checking only <c>UriKind.Absolute</c> is not enough: on Windows,
    /// <c>Uri.TryCreate</c> happily parses an absolute path like
    /// <c>C:\snapshots\after.json</c> as a valid URI with scheme <c>file</c>,
    /// which would route a local file straight into the HTTP transport and
    /// fail with a confusing scheme error. The scheme check is what actually
    /// answers "should this be fetched over HTTP," which is the only question
    /// that matters here.
    /// </remarks>
    private static async Task<Snapshot> ResolveTargetAsync(
        string target,
        string[] allowedHosts,
        bool insecure,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return await ReadSnapshotAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var options = new TransportOptions
        {
            Target = uri,
            AllowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase),
            AllowInvalidCertificates = insecure,
            BearerToken = Environment.GetEnvironmentVariable(TransportOptions.TokenVariable),
        };

        using var probe = new StreamableHttpProbe(options);
        return await probe.CaptureAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads and digest-verifies a snapshot from disk. A file on disk is a
    /// committed artefact someone could have hand-edited; a fresh live capture
    /// is not, and is used as SnapshotWriter just produced it.
    /// </summary>
    private static async Task<Snapshot> ReadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(Path.GetFullPath(path), cancellationToken).ConfigureAwait(false);
        return SnapshotReader.ReadVerified(bytes);
    }

    private static GatePolicy ParsePolicy(string[] failOnNames, string[] warnOnNames)
    {
        var failOn = failOnNames.Length == 0
            ? GatePolicy.Default.FailOn
            : ParseSeverities(failOnNames, "--fail-on");

        var warnOn = warnOnNames.Length == 0
            ? GatePolicy.Default.WarnOn
            : ParseSeverities(warnOnNames, "--warn-on");

        return new GatePolicy { FailOn = failOn, WarnOn = warnOn };
    }

    private static HashSet<Severity> ParseSeverities(string[] names, string option)
    {
        var severities = new HashSet<Severity>();

        foreach (var name in names)
        {
            if (!Enum.TryParse<Severity>(name, ignoreCase: true, out var severity))
            {
                throw new FormatException(
                    $"Unknown severity '{Sanitizer.SanitizeForMessage(name)}' for {option}. "
                    + $"Valid values: {string.Join(", ", Enum.GetNames<Severity>()).ToLowerInvariant()}.");
            }

            severities.Add(severity);
        }

        return severities;
    }

    private static int Fail(ExitCode code, string message)
    {
        Console.Error.WriteLine($"detent: {Sanitizer.SanitizeForMessage(message, 500)}");
        return (int)code;
    }
}
