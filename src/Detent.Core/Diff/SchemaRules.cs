using Detent.Core.Policy;

namespace Detent.Core.Diff;

/// <summary>
/// Which rule and class a structural change maps to, for one side of a tool.
/// </summary>
/// <remarks>
/// This table is the whole variance argument made explicit. Input schemas are
/// contravariant and output schemas are covariant, so the same structural edit
/// classifies differently depending on which side it lands on: adding a required
/// property is breaking on an input and additive on an output. Naive differs
/// share one table between both sides and are consequently wrong about half the
/// time. See <c>docs/arch/diff-rules.md</c> §1.
/// </remarks>
internal sealed record SchemaRules
{
    public required Rule AddOptionalProperty { get; init; }

    public required Rule AddRequiredProperty { get; init; }

    public required Rule RemoveProperty { get; init; }

    public required Rule OptionalBecomesRequired { get; init; }

    public required Rule RequiredBecomesOptional { get; init; }

    public required Rule TypeWidened { get; init; }

    public required Rule TypeNarrowed { get; init; }

    public required Rule EnumValueAdded { get; init; }

    public required Rule EnumValueRemoved { get; init; }

    public required Rule ConstraintTightened { get; init; }

    public required Rule ConstraintLoosened { get; init; }

    public required Rule AdditionalPropertiesClosed { get; init; }

    public required Rule AdditionalPropertiesOpened { get; init; }

    public required Rule DefaultAdded { get; init; }

    public required Rule DefaultChanged { get; init; }

    public required Rule DescriptionChanged { get; init; }

    public required Rule UnionBranchAdded { get; init; }

    public required Rule UnionBranchRemoved { get; init; }

    /// <summary>
    /// The contravariant side: the server accepts, the consumer sends. Widening
    /// what is accepted is safe; narrowing it breaks callers already in the
    /// field. From diff-rules.md §4.
    /// </summary>
    public static SchemaRules Input { get; } = new()
    {
        AddOptionalProperty = new("MCPC101", Severity.Additive, "an optional property was added"),
        AddRequiredProperty = new("MCPC102", Severity.Breaking, "a required property was added"),
        RemoveProperty = new("MCPC103", Severity.Breaking, "a property was removed"),
        OptionalBecomesRequired = new("MCPC104", Severity.Breaking, "an optional property became required"),
        RequiredBecomesOptional = new("MCPC105", Severity.Additive, "a required property became optional"),
        TypeWidened = new("MCPC106", Severity.Additive, "the accepted types widened"),
        TypeNarrowed = new("MCPC107", Severity.Breaking, "the accepted types narrowed"),
        EnumValueAdded = new("MCPC108", Severity.Additive, "an enum value was added"),
        EnumValueRemoved = new("MCPC109", Severity.Breaking, "an enum value was removed"),
        ConstraintTightened = new("MCPC110", Severity.Breaking, "a constraint tightened"),
        ConstraintLoosened = new("MCPC111", Severity.Additive, "a constraint loosened"),
        AdditionalPropertiesClosed = new("MCPC112", Severity.Breaking, "unknown properties are no longer accepted"),
        AdditionalPropertiesOpened = new("MCPC113", Severity.Additive, "unknown properties are now accepted"),
        DefaultAdded = new("MCPC114", Severity.Additive, "a default was added"),
        DefaultChanged = new("MCPC115", Severity.Behavioural, "a default changed"),
        DescriptionChanged = new("MCPC116", Severity.Behavioural, "a property description changed"),
        UnionBranchAdded = new("MCPC117", Severity.Additive, "a union branch was added"),
        UnionBranchRemoved = new("MCPC118", Severity.Breaking, "a union branch was removed"),
    };
}

/// <summary>One row of a rule table.</summary>
internal sealed record Rule(string Id, Severity Severity, string Summary);
