namespace Detent.Transport;

/// <summary>
/// The resource caps from <c>docs/arch/security-model.md</c> §1, at their single
/// definition site.
/// </summary>
/// <remarks>
/// These are constants and not command-line flags, deliberately. A user who
/// raises a cap to survive one awkward server has disarmed the control for every
/// server thereafter and will not remember they did it. See security-model.md §2.
/// </remarks>
public static class TransportLimits
{
    /// <summary>Largest response body accepted, before parsing.</summary>
    public const int MaxResponseBytes = 10 * 1024 * 1024;

    /// <summary>Deepest JSON nesting accepted.</summary>
    /// <remarks>
    /// Enforced by the parser rather than by inspection afterwards: a document
    /// deep enough to matter is deep enough to overflow the stack of whatever
    /// walks it, so it must never be materialised.
    /// </remarks>
    public const int MaxJsonDepth = 64;

    /// <summary>
    /// Largest number of entries accepted in a single listing.
    /// </summary>
    /// <remarks>
    /// security-model.md §1 states this as a tool count. The same cap applies to
    /// resources and prompts because the exhaustion vector is identical and a
    /// cap that only covers one of three listings is not a cap.
    /// </remarks>
    public const int MaxItemsPerListing = 5_000;

    /// <summary>Longest description accepted from a server.</summary>
    public const int MaxDescriptionChars = 100 * 1024;

    /// <summary>Redirect hops followed before giving up.</summary>
    public const int MaxRedirects = 3;

    /// <summary>
    /// Pagination pages fetched per listing.
    /// </summary>
    /// <remarks>
    /// Not in the security-model table, and it should be. A server that returns
    /// a fresh cursor forever is an unbounded loop that the item cap alone does
    /// not close, because a server can return one item per page indefinitely.
    /// </remarks>
    public const int MaxPagesPerListing = 100;

    /// <summary>Total wall clock for one capture, across every request.</summary>
    /// <remarks>
    /// A whole-operation budget rather than a per-request timeout, because a
    /// slow-loris server defeats per-request timeouts simply by staying just
    /// inside each one.
    /// </remarks>
    public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(30);
}
