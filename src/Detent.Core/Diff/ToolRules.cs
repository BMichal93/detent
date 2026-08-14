using Detent.Core.Policy;

namespace Detent.Core.Diff;

/// <summary>
/// The tool-level and annotation rules from <c>docs/arch/diff-rules.md</c> §6.
/// </summary>
/// <remarks>
/// Unlike <see cref="SchemaRules"/>, this is one flat table rather than a
/// contravariant/covariant pair: a tool's description, title, and safety
/// annotations do not have an input side and an output side.
/// </remarks>
internal static class ToolRules
{
    public static Rule DescriptionChanged { get; } =
        new("MCPC304", Severity.Behavioural, "the description changed");

    public static Rule TitleChanged { get; } =
        new("MCPC305", Severity.Cosmetic, "the title changed");

    public static Rule ReadOnlyDowngraded { get; } =
        new("MCPC306", Severity.Security, "readOnlyHint changed from true to false");

    public static Rule DestructiveUpgraded { get; } =
        new("MCPC307", Severity.Security, "destructiveHint changed from false to true");

    public static Rule IdempotentDowngraded { get; } =
        new("MCPC308", Severity.Behavioural, "idempotentHint changed from true to false");

    public static Rule OpenWorldUpgraded { get; } =
        new("MCPC309", Severity.Security, "openWorldHint changed from false to true");

    /// <summary>
    /// Fires whenever any single hint goes from an explicit value to absent,
    /// regardless of which hint or which value it held. An absent hint is a
    /// different claim from a false one, and losing an assertion is never
    /// neutral. See the remarks on <c>ToolAnnotations</c>.
    /// </summary>
    public static Rule AnnotationRemoved { get; } =
        new("MCPC310", Severity.Security, "a safety annotation was removed");

    public static Rule Renamed { get; } =
        new("MCPC302", Severity.Breaking, "the tool appears to have been renamed");
}
