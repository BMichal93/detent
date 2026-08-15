using Detent.Core.Capture;
using Detent.Core.Policy;
using Detent.Core.Security;
using Detent.Transport;

namespace Detent.Cli;

/// <summary>
/// The parts of resolving a diff or verify run that <c>detent diff</c> and
/// <c>detent verify</c> need identically: parsing <c>--fail-on</c>/
/// <c>--warn-on</c>, and reading a baseline or target snapshot.
/// </summary>
internal static class PolicyOptions
{
    /// <summary>
    /// CLI flags win when given; otherwise <paramref name="fallback"/> applies.
    /// For <c>diff</c> the fallback is always <see cref="GatePolicy.Default"/>;
    /// for <c>verify</c> it is the loaded contract's own policy, itself
    /// falling back to the same default field by field.
    /// </summary>
    /// <exception cref="FormatException">A severity name is not recognised.</exception>
    public static GatePolicy Resolve(string[] failOnNames, string[] warnOnNames, GatePolicy fallback)
    {
        var failOn = failOnNames.Length == 0 ? fallback.FailOn : ParseSeverities(failOnNames, "--fail-on");
        var warnOn = warnOnNames.Length == 0 ? fallback.WarnOn : ParseSeverities(warnOnNames, "--warn-on");

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
}

/// <summary>Reading a baseline or a live/file target the same way for every command that needs one.</summary>
internal static class SnapshotResolution
{
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
    public static async Task<Snapshot> ResolveTargetAsync(
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
    /// is not, and is used as <c>SnapshotWriter</c> just produced it.
    /// </summary>
    public static async Task<Snapshot> ReadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(Path.GetFullPath(path), cancellationToken).ConfigureAwait(false);
        return SnapshotReader.ReadVerified(bytes);
    }
}

internal static class CliOutput
{
    public static int Fail(ExitCode code, string message)
    {
        Console.Error.WriteLine($"detent: {Sanitizer.SanitizeForMessage(message, 500)}");
        return (int)code;
    }
}
