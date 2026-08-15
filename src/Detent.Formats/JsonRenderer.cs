using System.Text.Json;
using System.Text.Json.Serialization;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Formats;

/// <summary>
/// Renders a policy outcome as JSON for machine consumers: other tools, a CI
/// step parsing results, a dashboard.
/// </summary>
/// <remarks>
/// A deliberately separate wire shape from <c>Finding</c> and
/// <c>GateResult</c> themselves. Those are <c>Detent.Core</c>'s domain model
/// and carry no opinion about presentation; this is the opinion. Unlike a
/// snapshot, this output is never committed to a repository and diffed byte for
/// byte, so it uses ordinary indented JSON rather than
/// <c>Detent.Core.Capture.CanonicalJson</c>'s stricter canonical form.
/// </remarks>
public static class JsonRenderer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = FindingReportJsonContext.Default,
    };

    public static string Render(GateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var report = new FindingReport
        {
            ExitCode = (int)result.ExitCode,
            Summary = new FindingSummary
            {
                Failures = result.Failures.Count,
                Warnings = result.Warnings.Count,
                Passed = result.Passed.Count,
            },
            Findings =
            [
                .. result.Failures.Select(f => ToJson(f, "fail")),
                .. result.Warnings.Select(f => ToJson(f, "warn")),
                .. result.Passed.Select(f => ToJson(f, "pass")),
            ],
        };

        return JsonSerializer.Serialize(report, FindingReportJsonContext.Default.FindingReport);
    }

    /// <summary>
    /// Path and message are server-derived and therefore tainted, per
    /// <c>docs/arch/security-model.md</c> §1 - but JSON escaping already
    /// neutralises the terminal-injection threat that text renders must guard
    /// against explicitly, since a consumer of this output is a JSON parser,
    /// not a terminal. Nothing here writes these strings to a console.
    /// </summary>
    private static FindingJson ToJson(Finding finding, string outcome) => new()
    {
        Id = finding.Id,
        Severity = finding.Severity.ToString().ToLowerInvariant(),
        Outcome = outcome,
        Path = finding.Path,
        Message = finding.Message,
    };
}

internal sealed record FindingReport
{
    public required int ExitCode { get; init; }

    public required FindingSummary Summary { get; init; }

    public required IReadOnlyList<FindingJson> Findings { get; init; }
}

internal sealed record FindingSummary
{
    public required int Failures { get; init; }

    public required int Warnings { get; init; }

    public required int Passed { get; init; }
}

internal sealed record FindingJson
{
    public required string Id { get; init; }

    public required string Severity { get; init; }

    /// <summary>"fail", "warn", or "pass" - this finding's outcome under the
    /// policy that was evaluated, not just its raw severity.</summary>
    public required string Outcome { get; init; }

    public required string Path { get; init; }

    public required string Message { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FindingReport))]
internal sealed partial class FindingReportJsonContext : JsonSerializerContext;
