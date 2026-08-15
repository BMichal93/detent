using System.CommandLine;
using System.Text;
using Detent.Core.Contracts;
using Detent.Core.Policy;
using Detent.Core.Security;
using Detent.Transport;

namespace Detent.Cli;

/// <summary>
/// <c>detent init</c>: observes a server and scaffolds a starter contract.
/// </summary>
internal static class InitCommand
{
    private const string DefaultOutput = ".detent/contract.yaml";

    public static Command Create()
    {
        var target = new Argument<string>("target")
        {
            Description = "URL of the MCP endpoint to observe, or a path to an existing snapshot.",
        };

        var consumer = new Option<string>("--consumer")
        {
            Description = "Who this contract is for. Not inferrable from the server, so it is required.",
            Required = true,
        };

        var output = new Option<string>("--output", "-o")
        {
            Description = $"Where to write the contract, or - for stdout. Defaults to {DefaultOutput}.",
            DefaultValueFactory = _ => DefaultOutput,
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

        var command = new Command("init", "Observe a server and scaffold a starter contract.")
        {
            target,
            consumer,
            output,
            allowHost,
            insecure,
        };

        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(
            parseResult.GetValue(target)!,
            parseResult.GetValue(consumer)!,
            parseResult.GetValue(output)!,
            parseResult.GetValue(allowHost) ?? [],
            parseResult.GetValue(insecure),
            cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string target,
        string consumer,
        string output,
        string[] allowedHosts,
        bool insecure,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumer))
        {
            return CliOutput.Fail(ExitCode.UsageError, "--consumer must not be blank.");
        }

        Core.Capture.Snapshot snapshot;
        string? providerUrl = null;

        try
        {
            snapshot = await SnapshotResolution.ResolveTargetAsync(target, allowedHosts, insecure, cancellationToken)
                .ConfigureAwait(false);

            if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                providerUrl = target;
            }
        }
        catch (TransportException ex)
        {
            return CliOutput.Fail(ExitCode.TransportFailure, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return CliOutput.Fail(ExitCode.TransportFailure, "Cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Core.Capture.SnapshotFormatException)
        {
            return CliOutput.Fail(ExitCode.UsageError, $"Cannot read {Sanitizer.SanitizeForMessage(target)}: {ex.Message}");
        }

        var contract = ContractScaffolder.FromSnapshot(snapshot, consumer, providerUrl);
        var yaml = ContractYamlWriter.Write(contract);

        try
        {
            await WriteAsync(output, yaml, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CliOutput.Fail(ExitCode.UsageError, $"Cannot write {Sanitizer.SanitizeForMessage(output)}: {ex.Message}");
        }

        if (output != "-")
        {
            Console.Out.Write(
                $"Wrote {contract.Tools.Count} tool{(contract.Tools.Count == 1 ? "" : "s")} to {output}. "
                + "Review sends/reads before committing - see the comments at the end of the file.\n");
        }

        return (int)ExitCode.Pass;
    }

    /// <summary>
    /// Mirrors <c>CaptureCommand</c>'s write path: create the parent directory
    /// if needed, support <c>-</c> for stdout.
    /// </summary>
    private static async Task WriteAsync(string output, string yaml, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(yaml);

        if (output == "-")
        {
            await Console.OpenStandardOutput().WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        var path = Path.GetFullPath(output);

        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }
}
