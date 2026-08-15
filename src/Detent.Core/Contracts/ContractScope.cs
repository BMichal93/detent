using Detent.Core.Capture;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Core.Contracts;

/// <summary>
/// Narrows and, for one case, promotes findings against what a consumer's
/// contract actually declares. See <c>docs/arch/diff-rules.md</c> §8 and §12.
/// </summary>
/// <remarks>
/// A contract may only ever narrow a finding's set - drop it - except through
/// <c>exhaustiveEnums</c>, and may never suppress a <c>security</c> finding.
/// The assumption check in <see cref="CheckAssumptions"/> is the other
/// direction entirely: it does not filter existing findings, it introduces new
/// ones the underlying diff could never produce, because an assumption can be
/// violated by a server that never changed at all.
/// </remarks>
public static class ContractScope
{
    /// <summary>
    /// Filters and promotes a set of findings from an ordinary diff, per
    /// <c>docs/arch/diff-rules.md</c> §8.
    /// </summary>
    public static IReadOnlyList<Finding> Apply(IReadOnlyList<Finding> findings, Contract contract)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(contract);

        var requirements = contract.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var result = new List<Finding>();

        foreach (var finding in findings)
        {
            var toolName = ExtractToolName(finding.Path);

            if (toolName is null)
            {
                // Not scoped to a specific tool - capabilities, instructions,
                // server identity - and applies to this consumer regardless of
                // which tools it happens to call.
                result.Add(finding);
                continue;
            }

            if (!requirements.TryGetValue(toolName, out var requirement))
            {
                // A tool this consumer never declared using. Its contract has
                // nothing to say about it, which is the same as saying it does
                // not matter to them: dropping findings nobody reads is the
                // whole point of a consumer-driven contract.
                continue;
            }

            if (TryExtractSchemaProperty(finding.Path, toolName, "inputSchema", out var sent))
            {
                if (!requirement.Sends.Contains(sent))
                {
                    continue;
                }
            }
            else if (TryExtractSchemaProperty(finding.Path, toolName, "outputSchema", out var read))
            {
                if (!requirement.Reads.Contains(read))
                {
                    continue;
                }

                // The one promotion a contract may make. A consumer switching
                // exhaustively on this field breaks when a new value appears;
                // one that does not, does not. See diff-rules.md §5, MCPC208.
                if (finding.Id == "MCPC208" && requirement.ExhaustiveEnums.Contains(read))
                {
                    result.Add(finding with { Severity = Severity.Breaking });
                    continue;
                }
            }

            // A tool-level finding (description, title, an annotation flip, the
            // tool's own presence), or a schema-root-level one with no single
            // property to attribute it to (an additionalProperties flip, for
            // instance). Neither is filterable by name, and dropping either
            // would hide something a consumer of a declared tool likely cares
            // about, so both pass through unfiltered.
            result.Add(finding);
        }

        return result;
    }

    /// <summary>
    /// Checks every declared <c>assumes</c> entry against the candidate
    /// snapshot directly, per <c>docs/arch/diff-rules.md</c> §12. Independent
    /// of any diff: a tool that never satisfied the assumption produces a
    /// finding here even when nothing about it changed.
    /// </summary>
    public static IReadOnlyList<Finding> CheckAssumptions(Snapshot candidate, Contract contract)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(contract);

        var findings = new List<Finding>();

        foreach (var requirement in contract.Tools)
        {
            if (requirement.Assumes is not { } assumes)
            {
                continue;
            }

            // A tool absent from the candidate has already produced MCPC301;
            // an assumption about a tool that no longer exists says nothing
            // new.
            var tool = candidate.Tools.FirstOrDefault(t => t.Name == requirement.Name);

            if (tool is null)
            {
                continue;
            }

            CheckHint(tool, assumes.ReadOnlyHint, tool.Annotations?.ReadOnlyHint, "readOnlyHint", findings);
            CheckHint(tool, assumes.DestructiveHint, tool.Annotations?.DestructiveHint, "destructiveHint", findings);
            CheckHint(tool, assumes.IdempotentHint, tool.Annotations?.IdempotentHint, "idempotentHint", findings);
            CheckHint(tool, assumes.OpenWorldHint, tool.Annotations?.OpenWorldHint, "openWorldHint", findings);
        }

        return findings;
    }

    /// <summary>
    /// Drops findings for a tool under an active <c>ignore</c> entry, per
    /// <c>docs/arch/diff-rules.md</c> §8. A <c>security</c> finding is never
    /// suppressed, even for a tool the consumer has otherwise ignored -
    /// everything else, including <c>breaking</c>, may be, since an expiring
    /// suppression for a tool scheduled for removal is exactly what
    /// <c>ignore</c> is for.
    /// </summary>
    /// <param name="findings">The findings to filter.</param>
    /// <param name="policy">The contract's policy, or <see langword="null"/> for none.</param>
    /// <param name="today">
    /// Never read internally - <c>Detent.Core</c> takes no clock, so the
    /// caller decides what "today" means.
    /// </param>
    public static IReadOnlyList<Finding> ApplySuppressions(
        IReadOnlyList<Finding> findings,
        ContractPolicy? policy,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (policy is null || policy.Ignore.Count == 0)
        {
            return findings;
        }

        var active = policy.Ignore
            .Where(s => s.Expires >= today)
            .Select(s => s.Tool)
            .ToHashSet(StringComparer.Ordinal);

        if (active.Count == 0)
        {
            return findings;
        }

        return findings
            .Where(f => f.Severity == Severity.Security || ExtractToolName(f.Path) is not { } tool || !active.Contains(tool))
            .ToList();
    }

    private static void CheckHint(
        ToolDescriptor tool,
        bool? assumed,
        bool? actual,
        string hintName,
        List<Finding> findings)
    {
        if (assumed is null || actual == assumed)
        {
            return;
        }

        var actualText = actual is null ? "no value" : actual.Value.ToString().ToLowerInvariant();

        findings.Add(new Finding
        {
            Id = "MCPC501",
            Severity = Severity.Security,
            Path = $"tools/{tool.Name}/annotations/{hintName}",
            Message = $"Contract for '{tool.Name}' assumes {hintName}="
                + $"{assumed.Value.ToString().ToLowerInvariant()}, but the server currently reports {actualText}.",
        });
    }

    private static string? ExtractToolName(string path)
    {
        const string prefix = "tools/";

        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = path[prefix.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    private static bool TryExtractSchemaProperty(string path, string toolName, string slot, out string property)
    {
        var prefix = $"tools/{toolName}/{slot}/properties/";
        property = string.Empty;

        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = path[prefix.Length..];
        var slash = rest.IndexOf('/');
        property = slash < 0 ? rest : rest[..slash];
        return property.Length > 0;
    }
}
