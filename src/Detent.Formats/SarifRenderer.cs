using System.Text.Json;
using System.Text.Json.Serialization;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Formats;

/// <summary>
/// Renders a policy outcome as SARIF 2.1.0, for native rendering in GitHub
/// code scanning and Azure DevOps.
/// </summary>
/// <remarks>
/// The plan calls this the best effort-to-reach ratio in the whole project:
/// about half a day of work buys native rendering in two platforms without
/// writing either integration by hand. There is no file or line number behind
/// a finding - <c>tools/search_products/inputSchema/properties/query</c> is a
/// position in a server's advertised surface, not a position in a source
/// file - so every result carries a <c>logicalLocation</c> instead of a
/// <c>physicalLocation</c>, which is exactly what SARIF's logical-location
/// concept exists for.
/// </remarks>
public static class SarifRenderer
{
    private const string SchemaUri = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json";
    private const string InformationUri = "https://github.com/BMichal93/detent";

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = SarifLogJsonContext.Default,
    };

    public static string Render(GateResult result, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(toolVersion);

        var results = new List<SarifResult>();
        results.AddRange(result.Failures.Select(f => ToResult(f, "error")));
        results.AddRange(result.Warnings.Select(f => ToResult(f, "warning")));
        results.AddRange(result.Passed.Select(f => ToResult(f, "note")));

        var log = new SarifLog
        {
            Schema = SchemaUri,
            Version = "2.1.0",
            Runs =
            [
                new SarifRun
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifDriver
                        {
                            Name = "detent",
                            InformationUri = InformationUri,
                            Version = toolVersion,
                            Rules = BuildRuleCatalog(result),
                        },
                    },
                    Results = results,
                },
            ],
        };

        return JsonSerializer.Serialize(log, SarifLogJsonContext.Default.SarifLog);
    }

    /// <summary>
    /// One catalog entry per distinct rule ID that actually fired, not the
    /// full ~47-row table - SARIF does not require declaring rules that never
    /// appear in <c>results</c>, and hard-coding the whole catalog here would
    /// duplicate <c>docs/arch/diff-rules.md</c> in a place that could drift
    /// from it.
    /// </summary>
    private static List<SarifRule> BuildRuleCatalog(GateResult result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var catalog = new List<SarifRule>();

        foreach (var finding in result.Failures.Concat(result.Warnings).Concat(result.Passed)
                     .OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            if (!seen.Add(finding.Id))
            {
                continue;
            }

            catalog.Add(new SarifRule
            {
                Id = finding.Id,
                ShortDescription = new SarifMessage { Text = finding.Id },
            });
        }

        return catalog;
    }

    /// <summary>
    /// Path and message are server-derived and therefore tainted, per
    /// <c>docs/arch/security-model.md</c> §1 - but as with <see cref="JsonRenderer"/>,
    /// this is a machine format read by a JSON parser, not a terminal, so
    /// JSON's own string escaping is the applicable control, not <c>Sanitize()</c>.
    /// </summary>
    private static SarifResult ToResult(Finding finding, string level) => new()
    {
        RuleId = finding.Id,
        Level = level,
        Message = new SarifMessage { Text = finding.Message },
        Locations =
        [
            new SarifLocation
            {
                LogicalLocations = [new SarifLogicalLocation { FullyQualifiedName = finding.Path, Kind = "member" }],
            },
        ],
    };
}

internal sealed record SarifLog
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyList<SarifRun> Runs { get; init; }
}

internal sealed record SarifRun
{
    public required SarifTool Tool { get; init; }

    public required IReadOnlyList<SarifResult> Results { get; init; }
}

internal sealed record SarifTool
{
    public required SarifDriver Driver { get; init; }
}

internal sealed record SarifDriver
{
    public required string Name { get; init; }

    public required string InformationUri { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyList<SarifRule> Rules { get; init; }
}

internal sealed record SarifRule
{
    public required string Id { get; init; }

    public required SarifMessage ShortDescription { get; init; }
}

internal sealed record SarifResult
{
    public required string RuleId { get; init; }

    /// <summary>SARIF's own vocabulary: "error", "warning", or "note".</summary>
    public required string Level { get; init; }

    public required SarifMessage Message { get; init; }

    public required IReadOnlyList<SarifLocation> Locations { get; init; }
}

internal sealed record SarifMessage
{
    public required string Text { get; init; }
}

internal sealed record SarifLocation
{
    public required IReadOnlyList<SarifLogicalLocation> LogicalLocations { get; init; }
}

internal sealed record SarifLogicalLocation
{
    public required string FullyQualifiedName { get; init; }

    public required string Kind { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SarifLog))]
internal sealed partial class SarifLogJsonContext : JsonSerializerContext;
