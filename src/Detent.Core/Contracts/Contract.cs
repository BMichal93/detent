using Detent.Core.Policy;

namespace Detent.Core.Contracts;

/// <summary>
/// What one consumer actually uses of a server, and the policy to verify it
/// against. The plain data model behind <c>.detent/contract.yaml</c>.
/// </summary>
/// <remarks>
/// Parsing YAML into this shape happens in <c>Detent.Formats</c>, not here:
/// <c>Detent.Core</c> takes no package dependencies, and a contract file is
/// read at the same I/O boundary a snapshot file is. This type is what crosses
/// that boundary - the data, not the file format.
/// </remarks>
public sealed record Contract
{
    public required string ApiVersion { get; init; }

    public required string Consumer { get; init; }

    public ContractProvider? Provider { get; init; }

    public required IReadOnlyList<ToolRequirement> Tools { get; init; }

    public ContractPolicy? Policy { get; init; }
}

/// <summary>Where the contract's own <c>detent verify --live</c> would connect.</summary>
public sealed record ContractProvider
{
    public required string Transport { get; init; }

    public required string Url { get; init; }
}

/// <summary>
/// One tool as a specific consumer actually calls it: which inputs it
/// supplies, which outputs it reads, and what it assumes stays true.
/// </summary>
public sealed record ToolRequirement
{
    public required string Name { get; init; }

    /// <summary>
    /// Top-level input property names this consumer supplies. A finding on an
    /// input property absent from this set does not affect the consumer and
    /// is dropped. See <c>docs/arch/diff-rules.md</c> §8.
    /// </summary>
    public IReadOnlySet<string> Sends { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Top-level output property names this consumer reads. A finding on an
    /// output property absent from this set is dropped, the same way.
    /// </summary>
    public IReadOnlySet<string> Reads { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Output enum fields this consumer switches on exhaustively. Promotes
    /// MCPC208 from <c>behavioural</c> to <c>breaking</c> for that field only,
    /// per diff-rules.md §5 - the one case a contract may make a finding
    /// worse rather than drop it.
    /// </summary>
    public IReadOnlySet<string> ExhaustiveEnums { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Safety assumptions this consumer's own code relies on.</summary>
    public ToolAssumptions? Assumes { get; init; }
}

/// <summary>
/// What a consumer's code assumes about a tool's safety, independent of
/// whether anything changed. Checked against the candidate snapshot's actual
/// annotations, not diffed - a first-ever verify run with no prior baseline
/// must still catch a tool that never satisfied the assumption.
/// </summary>
public sealed record ToolAssumptions
{
    public bool? ReadOnlyHint { get; init; }

    public bool? DestructiveHint { get; init; }

    public bool? IdempotentHint { get; init; }

    public bool? OpenWorldHint { get; init; }
}

/// <summary>
/// Overrides the default gate policy for a contract-scoped verify. Absent
/// fields fall back to <see cref="GatePolicy.Default"/>.
/// </summary>
public sealed record ContractPolicy
{
    public IReadOnlySet<Severity>? FailOn { get; init; }

    public IReadOnlySet<Severity>? WarnOn { get; init; }

    public IReadOnlyList<Suppression> Ignore { get; init; } = [];
}

/// <summary>
/// An expiring suppression for one tool. Deliberately not permanent:
/// suppressions that never expire accumulate until the gate means nothing.
/// </summary>
public sealed record Suppression
{
    public required string Tool { get; init; }

    public required string Reason { get; init; }

    /// <summary>
    /// The last date this suppression applies. Whether it has passed is
    /// determined by the caller, not this type - <c>Detent.Core</c> does not
    /// read the clock, so "today" is always an explicit parameter.
    /// </summary>
    public required DateOnly Expires { get; init; }
}
