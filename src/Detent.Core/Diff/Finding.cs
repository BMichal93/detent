using Detent.Core.Policy;

namespace Detent.Core.Diff;

/// <summary>
/// One classified difference between two snapshots.
/// </summary>
/// <remarks>
/// <see cref="Id"/> and <see cref="Severity"/> are the contract, and golden cases
/// pin both. <see cref="Message"/> is not pinned: wording is UX and iterable in
/// public, while a classification that changes silently is the failure this
/// product exists to prevent. See <c>docs/arch/testing.md</c>.
/// </remarks>
public sealed record Finding
{
    /// <summary>The stable rule ID, such as <c>MCPC301</c>.</summary>
    public required string Id { get; init; }

    public required Severity Severity { get; init; }

    /// <summary>
    /// Where in the snapshot this applies, as <c>tools/search_products</c> or
    /// <c>tools/search_products/inputSchema/properties/query</c>.
    /// </summary>
    /// <remarks>
    /// Carries the server-supplied name, so it is tainted and must be sanitized
    /// before it reaches a console. See <c>docs/arch/security-model.md</c>.
    /// </remarks>
    public required string Path { get; init; }

    /// <summary>Human-readable explanation. Also tainted.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Orders findings so output is a function of content, never of input order.
    /// </summary>
    public static int Compare(Finding a, Finding b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var byId = string.CompareOrdinal(a.Id, b.Id);
        return byId != 0 ? byId : string.CompareOrdinal(a.Path, b.Path);
    }
}
