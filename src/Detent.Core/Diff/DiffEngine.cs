using System.Text.Json.Nodes;
using Detent.Core.Capture;
using Detent.Core.Policy;

namespace Detent.Core.Diff;

/// <summary>
/// Classifies the differences between two snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <b>Incomplete. Not a gate yet.</b> MCPC402 (auth scheme or required scopes
/// changed) has no row: nothing about authentication is ever captured, so
/// there is no field to compare. Adding it means teaching
/// <c>Detent.Transport</c> to read <c>WWW-Authenticate</c> or an OAuth
/// protected-resource metadata document, which is new capture surface with its
/// own security shape, not a diff-engine task - see the remarks on
/// <see cref="ServerRules"/>. That is a false negative, which
/// <c>docs/arch/testing.md</c> §1 calls the one failure that destroys this
/// product, and it is why <c>detent diff</c> is not registered as a CLI
/// command yet. Nothing may expose this engine to a user until every row in
/// <c>docs/arch/diff-rules.md</c> has a passing golden case.
/// </para>
/// <para>
/// Implemented: MCPC101-118 (input schemas), MCPC201-209 (output schemas),
/// MCPC301-310 (tool level), MCPC401, MCPC403-407 (server level), and the
/// MCPC901/903 analysis limits.
/// </para>
/// </remarks>
public static class DiffEngine
{
    /// <summary>
    /// Compares a baseline against a candidate and returns findings in a stable
    /// order.
    /// </summary>
    public static IReadOnlyList<Finding> Diff(Snapshot before, Snapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var findings = new List<Finding>();

        // A protocol revision change is a re-baseline event: everything below
        // this point compares two snapshots as if the same server merely
        // changed, which is not a safe assumption across a revision boundary.
        // See the remarks on ServerComparer.Compare.
        if (ServerComparer.Compare(before, after, findings))
        {
            findings.Sort(Finding.Compare);
            return findings;
        }

        var baseline = before.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var candidate = after.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        // Present under the same name on both sides: the ordinary case, and
        // where almost every finding comes from.
        foreach (var name in baseline.Keys.Where(candidate.ContainsKey))
        {
            CompareMatchedTool(baseline[name], candidate[name], findings);
        }

        var removed = baseline.Keys.Where(n => !candidate.ContainsKey(n)).Select(n => baseline[n]).ToList();
        var added = candidate.Keys.Where(n => !baseline.ContainsKey(n)).Select(n => candidate[n]).ToList();

        var renamed = ToolRenameDetector.Match(removed, added);
        var renamedFrom = renamed.Select(m => m.Removed.Name).ToHashSet(StringComparer.Ordinal);
        var renamedTo = renamed.Select(m => m.Added.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var (was, now) in renamed)
        {
            findings.Add(new Finding
            {
                Id = ToolRules.Renamed.Id,
                Severity = ToolRules.Renamed.Severity,
                Path = $"tools/{now.Name}",
                Message = $"Tool '{was.Name}' appears to have been renamed to '{now.Name}'. "
                    + $"A consumer calling '{was.Name}' will now fail.",
            });

            // A rename is the same tool going forward under a new name, not a
            // terminal event. Comparing the pair here is what stops a rename
            // from being a way to smuggle an annotation downgrade past review.
            CompareMatchedTool(was, now, findings);
        }

        foreach (var tool in removed.Where(t => !renamedFrom.Contains(t.Name)))
        {
            findings.Add(new Finding
            {
                Id = "MCPC301",
                Severity = Severity.Breaking,
                Path = $"tools/{tool.Name}",
                Message = $"Tool '{tool.Name}' was removed. A consumer that calls it will now fail.",
            });
        }

        foreach (var tool in added.Where(t => !renamedTo.Contains(t.Name)))
        {
            findings.Add(new Finding
            {
                Id = "MCPC303",
                Severity = Severity.Additive,
                Path = $"tools/{tool.Name}",
                Message = $"Tool '{tool.Name}' was added.",
            });
        }

        findings.Sort(Finding.Compare);
        return findings;
    }

    /// <summary>
    /// Every rule that applies to a tool known to exist on both sides: the
    /// tool-level rules, and both schema slots under their own variance table.
    /// </summary>
    private static void CompareMatchedTool(ToolDescriptor before, ToolDescriptor after, List<Finding> findings)
    {
        ToolComparer.Compare(before, after, findings);

        CompareSchema(before.InputSchema, after.InputSchema, $"tools/{after.Name}/inputSchema", SchemaRules.Input, findings);
        CompareSchema(before.OutputSchema, after.OutputSchema, $"tools/{after.Name}/outputSchema", SchemaRules.Output, findings);
    }

    private static void CompareSchema(
        JsonObject? before,
        JsonObject? after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        var normalisedBefore = SchemaNormaliser.Normalise(before);
        var normalisedAfter = SchemaNormaliser.Normalise(after);

        ReportIssues(normalisedBefore.Issues, normalisedAfter.Issues, path, findings);

        SchemaComparer.Compare(normalisedBefore.Schema, normalisedAfter.Schema, path, rules, findings);
    }

    /// <summary>
    /// Surfaces what the normaliser could not analyse, once per place.
    /// </summary>
    /// <remarks>
    /// Deduplicated across the two sides, because a schema that is recursive in
    /// the baseline is almost always recursive in the candidate too, and saying
    /// so twice helps nobody. Never dropped entirely: the default posture on
    /// anything unanalysable is to report it. See diff-rules.md §10.
    /// </remarks>
    private static void ReportIssues(
        IReadOnlyList<SchemaIssue> before,
        IReadOnlyList<SchemaIssue> after,
        string path,
        List<Finding> findings)
    {
        var seen = new HashSet<(string, string)>();

        foreach (var issue in before.Concat(after))
        {
            if (!seen.Add((issue.Id, issue.Path)))
            {
                continue;
            }

            findings.Add(new Finding
            {
                Id = issue.Id,
                Severity = Severity.Unanalysable,
                Path = path + issue.Path,
                Message = issue.Message,
            });
        }
    }
}
