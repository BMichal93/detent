using System.CommandLine;
using Detent.Core.Capture;
using Detent.Core.Policy;
using Detent.Core.Security;
using Detent.Transport;

namespace Detent.Cli;

/// <summary>
/// <c>detent capture</c>: reads a server's surface and writes a snapshot.
/// </summary>
internal static class CaptureCommand
{
    private const string DefaultOutput = ".detent/snapshot.json";

    public static Command Create()
    {
        var target = new Argument<string>("target")
        {
            Description = "URL of the MCP endpoint to capture.",
        };

        var output = new Option<string>("--output", "-o")
        {
            Description = $"Where to write the snapshot, or - for stdout. Defaults to {DefaultOutput}.",
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

        var command = new Command("capture", "Capture an MCP server's surface as a snapshot.")
        {
            target,
            output,
            allowHost,
            insecure,
        };

        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(
            parseResult.GetValue(target)!,
            parseResult.GetValue(output)!,
            parseResult.GetValue(allowHost) ?? [],
            parseResult.GetValue(insecure),
            cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string target,
        string output,
        string[] allowedHosts,
        bool insecure,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return Fail(ExitCode.UsageError, $"'{Sanitizer.SanitizeForMessage(target)}' is not an absolute URL.");
        }

        var options = new TransportOptions
        {
            Target = uri,
            AllowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase),
            AllowInvalidCertificates = insecure,

            // Environment only. A token on the command line is visible in
            // /proc and in CI logs; see security-model.md §1.
            BearerToken = Environment.GetEnvironmentVariable(TransportOptions.TokenVariable),
        };

        try
        {
            using var probe = new StreamableHttpProbe(options);

            var snapshot = await probe.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var bytes = SnapshotWriter.Write(snapshot);

            await WriteAsync(output, bytes, cancellationToken).ConfigureAwait(false);

            return (int)ExitCode.Pass;
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
        catch (IOException ex)
        {
            return Fail(ExitCode.UsageError, $"Cannot write {Sanitizer.SanitizeForMessage(output)}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(ExitCode.UsageError, $"Cannot write {Sanitizer.SanitizeForMessage(output)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the snapshot bytes exactly as produced.
    /// </summary>
    /// <remarks>
    /// Byte-wise, never through a TextWriter. A writer would apply the host's
    /// encoding and line endings and undo the canonical form, which is the one
    /// property the whole format depends on.
    /// </remarks>
    private static async Task WriteAsync(string output, byte[] bytes, CancellationToken cancellationToken)
    {
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

    private static int Fail(ExitCode code, string message)
    {
        Console.Error.WriteLine($"detent: {Sanitizer.SanitizeForMessage(message, 500)}");
        return (int)code;
    }
}
