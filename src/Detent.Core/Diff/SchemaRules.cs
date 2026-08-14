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
/// <para>
/// A null field means diff-rules.md has no row for that kind of change on this
/// side, and <see cref="SchemaComparer"/> skips the corresponding check rather
/// than inventing a classification nobody wrote down. The output table (§5) is
/// nine rows against the input table's eighteen: it has nothing to say about
/// constraints, <c>additionalProperties</c>, <c>default</c>, or union branches,
/// because none of those describe what a server promises to produce.
/// </para>
/// </remarks>
internal sealed record SchemaRules
{
    /// <summary>
    /// Rule for a property appearing that was absent before. On the input side
    /// this depends on whether the new property is required; on the output side
    /// it does not, so <see cref="AddOptionalProperty"/> and
    /// <see cref="AddRequiredProperty"/> point at the same row for that table.
    /// </summary>
    public required Rule AddOptionalProperty { get; init; }

    public required Rule AddRequiredProperty { get; init; }

    public required Rule RemoveProperty { get; init; }

    public required Rule OptionalBecomesRequired { get; init; }

    public required Rule RequiredBecomesOptional { get; init; }

    public required Rule TypeWidened { get; init; }

    public required Rule TypeNarrowed { get; init; }

    public required Rule EnumValueAdded { get; init; }

    public required Rule EnumValueRemoved { get; init; }

    public required Rule DescriptionChanged { get; init; }

    /// <summary>Not in the output table. Constraints describe what a server
    /// accepts, and covariance has nothing to say about that.</summary>
    public Rule? ConstraintTightened { get; init; }

    public Rule? ConstraintLoosened { get; init; }

    public Rule? AdditionalPropertiesClosed { get; init; }

    public Rule? AdditionalPropertiesOpened { get; init; }

    public Rule? DefaultAdded { get; init; }

    public Rule? DefaultChanged { get; init; }

    public Rule? UnionBranchAdded { get; init; }

    public Rule? UnionBranchRemoved { get; init; }

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

    /// <summary>
    /// The covariant side: the server produces, the consumer reads. Producing
    /// more is safe; producing less breaks a reader already in the field. From
    /// diff-rules.md §5.
    /// </summary>
    public static SchemaRules Output { get; } = new()
    {
        // A single row (MCPC201) regardless of whether the new property is
        // required, unlike the input side. Both fields point at it so the
        // shared property-comparison walk in SchemaComparer needs no branch on
        // which side it is running.
        AddOptionalProperty = new("MCPC201", Severity.Additive, "a property was added"),
        AddRequiredProperty = new("MCPC201", Severity.Additive, "a property was added"),
        RemoveProperty = new("MCPC202", Severity.Breaking, "a property was removed"),
        RequiredBecomesOptional = new("MCPC203", Severity.Breaking, "a required property became optional"),
        OptionalBecomesRequired = new("MCPC204", Severity.Additive, "an optional property became required"),
        TypeNarrowed = new("MCPC205", Severity.Additive, "the produced types narrowed"),
        TypeWidened = new("MCPC206", Severity.Breaking, "the produced types widened"),
        EnumValueRemoved = new("MCPC207", Severity.Additive, "an enum value was removed"),

        // diff-rules.md §5: breaking only when the contract declares
        // exhaustiveEnums for the field, else behavioural. Detent.Core takes no
        // contract; that promotion is a Phase 3 concern applied on top of this
        // finding, per §8, never something this table decides for itself.
        EnumValueAdded = new("MCPC208", Severity.Behavioural, "an enum value was added"),

        DescriptionChanged = new("MCPC209", Severity.Behavioural, "a property description changed"),
    };
}

/// <summary>One row of a rule table.</summary>
internal sealed record Rule(string Id, Severity Severity, string Summary);
