namespace Detent.Core.Policy;

/// <summary>
/// Which severities fail the build and which merely warn.
/// </summary>
/// <remarks>
/// Two sets, never a threshold. See the remarks on <see cref="Severity"/>: its
/// numeric order is arbitrary, and ranking one class against another is the
/// user's judgement, not this type's. A severity absent from both sets passes
/// silently for <see cref="Severity.Cosmetic"/> or visibly for anything else,
/// per the default policy column in <c>docs/arch/diff-rules.md</c> §2.
/// <para>
/// Suppressions (<c>ignore</c>, with expiry dates) are a contract-file concept
/// from Phase 3 and are deliberately not part of this type. A snapshot-only
/// diff has no contract to scope them against.
/// </para>
/// </remarks>
public sealed record GatePolicy
{
    public required IReadOnlySet<Severity> FailOn { get; init; }

    public required IReadOnlySet<Severity> WarnOn { get; init; }

    /// <summary>
    /// The default policy column of <c>docs/arch/diff-rules.md</c> §2, applied
    /// when nothing overrides it.
    /// </summary>
    public static GatePolicy Default { get; } = new()
    {
        FailOn = new HashSet<Severity> { Severity.Breaking, Severity.Security },
        WarnOn = new HashSet<Severity> { Severity.Behavioural, Severity.Notice, Severity.Unanalysable },
    };
}
