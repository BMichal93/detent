using Detent.Core.Capture;
using Detent.Core.Policy;

namespace Detent.Core.Diff;

/// <summary>
/// Classifies the differences between two snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <b>Incomplete. Not a gate yet.</b> Only the tool-presence rules below are
/// implemented, so every other kind of change currently produces no finding.
/// That is a false negative, which <c>docs/arch/testing.md</c> §1 calls the one
/// failure that destroys this product, and it is why <c>detent diff</c> is not
/// registered as a CLI command yet. Nothing may expose this engine to a user
/// until every row in <c>docs/arch/diff-rules.md</c> has a passing golden case.
/// </para>
/// <para>
/// Implemented: MCPC301, MCPC303.
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

        CompareToolPresence(before, after, findings);

        findings.Sort(Finding.Compare);
        return findings;
    }

    /// <summary>
    /// MCPC301 (tool removed) and MCPC303 (tool added).
    /// </summary>
    /// <remarks>
    /// Rename detection (MCPC302) will later collapse a matched removal and
    /// addition into one finding. Until it exists, a rename surfaces as the pair,
    /// which is the conservative reading rather than the tidy one.
    /// </remarks>
    private static void CompareToolPresence(Snapshot before, Snapshot after, List<Finding> findings)
    {
        var baseline = before.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var candidate = after.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (var name in baseline.Keys.Where(n => !candidate.ContainsKey(n)))
        {
            findings.Add(new Finding
            {
                Id = "MCPC301",
                Severity = Severity.Breaking,
                Path = $"tools/{name}",
                Message = $"Tool '{name}' was removed. A consumer that calls it will now fail.",
            });
        }

        foreach (var name in candidate.Keys.Where(n => !baseline.ContainsKey(n)))
        {
            findings.Add(new Finding
            {
                Id = "MCPC303",
                Severity = Severity.Additive,
                Path = $"tools/{name}",
                Message = $"Tool '{name}' was added.",
            });
        }
    }
}
