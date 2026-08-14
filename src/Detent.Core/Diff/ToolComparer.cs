using Detent.Core.Capture;

namespace Detent.Core.Diff;

/// <summary>
/// Compares the non-schema surface of a matched tool pair: description, title,
/// and the four safety annotations.
/// </summary>
/// <remarks>
/// Called for every tool present under the same name on both sides, and also
/// for a pair matched by <see cref="ToolRenameDetector"/>. A rename is treated
/// as the same tool going forward under a new name, not as a terminal event, so
/// a rename that also carries an annotation downgrade still surfaces the
/// downgrade rather than letting the rename hide it.
/// </remarks>
internal static class ToolComparer
{
    public static void Compare(ToolDescriptor before, ToolDescriptor after, List<Finding> findings)
    {
        // Path is always the new identity. For a same-name pair that is also
        // the old one; for a rename it is deliberately not, since the tool a
        // reviewer can still call is the one worth naming.
        var path = $"tools/{after.Name}";

        CompareDescription(before, after, path, findings);
        CompareTitle(before, after, path, findings);
        CompareAnnotations(before, after, path, findings);
    }

    private static void CompareDescription(
        ToolDescriptor before,
        ToolDescriptor after,
        string path,
        List<Finding> findings)
    {
        if (TextChanged(before.Description, after.Description))
        {
            findings.Add(Make(ToolRules.DescriptionChanged, $"{path}/description", "description"));
        }
    }

    private static void CompareTitle(
        ToolDescriptor before,
        ToolDescriptor after,
        string path,
        List<Finding> findings)
    {
        if (TextChanged(before.Title, after.Title))
        {
            findings.Add(Make(ToolRules.TitleChanged, $"{path}/title", "title"));
        }
    }

    private static void CompareAnnotations(
        ToolDescriptor before,
        ToolDescriptor after,
        string path,
        List<Finding> findings)
    {
        CompareHint(
            before.Annotations?.ReadOnlyHint, after.Annotations?.ReadOnlyHint,
            $"{path}/annotations/readOnlyHint",
            downgradeFrom: true, downgradeTo: false, ToolRules.ReadOnlyDowngraded, findings);

        CompareHint(
            before.Annotations?.DestructiveHint, after.Annotations?.DestructiveHint,
            $"{path}/annotations/destructiveHint",
            downgradeFrom: false, downgradeTo: true, ToolRules.DestructiveUpgraded, findings);

        CompareHint(
            before.Annotations?.IdempotentHint, after.Annotations?.IdempotentHint,
            $"{path}/annotations/idempotentHint",
            downgradeFrom: true, downgradeTo: false, ToolRules.IdempotentDowngraded, findings);

        CompareHint(
            before.Annotations?.OpenWorldHint, after.Annotations?.OpenWorldHint,
            $"{path}/annotations/openWorldHint",
            downgradeFrom: false, downgradeTo: true, ToolRules.OpenWorldUpgraded, findings);
    }

    /// <summary>
    /// One hint's transition, classified against the one direction that makes
    /// the tool more dangerous while claiming otherwise.
    /// </summary>
    /// <remarks>
    /// Every (before, after) pair for a single hint falls into exactly one of
    /// four buckets, so nothing here can double-fire on the same transition:
    /// unchanged (nothing); a new assertion appearing, <c>null</c> to a value
    /// (nothing - diff-rules.md has no row for a hint appearing); an assertion
    /// disappearing, a value to <c>null</c> (MCPC310, regardless of which value
    /// it held); or a value-to-value flip, which is either the specific
    /// dangerous direction this call was given (its own rule) or the safe
    /// direction (nothing).
    /// </remarks>
    private static void CompareHint(
        bool? before,
        bool? after,
        string hintPath,
        bool downgradeFrom,
        bool downgradeTo,
        Rule downgradeRule,
        List<Finding> findings)
    {
        if (before is null)
        {
            return;
        }

        if (after is null)
        {
            findings.Add(Make(ToolRules.AnnotationRemoved, hintPath, "annotation removed"));
            return;
        }

        if (before == downgradeFrom && after == downgradeTo)
        {
            findings.Add(Make(downgradeRule, hintPath, "annotation changed"));
        }
    }

    private static bool TextChanged(string? before, string? after)
        => !string.Equals(Normalised(before), Normalised(after), StringComparison.Ordinal);

    // Compared in the normalised form for the same reason a schema property
    // description is: re-wrapping is not a behaviour change.
    private static string? Normalised(string? value)
        => value is null ? null : TextNormaliser.ForComparison(value);

    private static Finding Make(Rule rule, string path, string subject) => new()
    {
        Id = rule.Id,
        Severity = rule.Severity,
        Path = path,
        Message = $"At {path}: {rule.Summary} ({subject}).",
    };
}
