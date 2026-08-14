namespace Detent.Transport;

/// <summary>
/// The parts of transport behaviour a user is allowed to influence.
/// </summary>
/// <remarks>
/// Everything absent from this type is absent on purpose. The resource caps live
/// in <see cref="TransportLimits"/> and are not negotiable; see
/// <c>docs/arch/security-model.md</c> §2. What remains here are two escape
/// hatches that a developer working against their own machine genuinely needs,
/// each narrow enough to be hard to leave switched on by accident.
/// </remarks>
public sealed record TransportOptions
{
    /// <summary>The MCP endpoint to capture.</summary>
    public required Uri Target { get; init; }

    /// <summary>
    /// Hosts exempted from the address blocklist, from <c>--allow-host</c>.
    /// </summary>
    /// <remarks>
    /// Matched on the host as written in the URL, never on the resolved address.
    /// Exempting an address would mean re-resolving to decide, which is the
    /// rebinding window the guard exists to close.
    /// </remarks>
    public IReadOnlySet<string> AllowedHosts { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Skips certificate validation. Refused unless the target is loopback.
    /// </summary>
    /// <remarks>
    /// Exists for a developer running a server on their own machine with a
    /// self-signed certificate, and for nothing else. The transport refuses it
    /// on any non-loopback host and refuses it outright when a CI environment
    /// variable is set, because "temporarily" disabling TLS validation in a
    /// pipeline is permanent in practice.
    /// </remarks>
    public bool AllowInvalidCertificates { get; init; }

    /// <summary>
    /// Bearer token for the target, read from the environment by the caller.
    /// </summary>
    /// <remarks>
    /// Never a command-line argument: arguments are visible in <c>/proc</c> and
    /// in CI logs. See security-model.md §1, secret leakage.
    /// </remarks>
    public string? BearerToken { get; init; }

    /// <summary>The environment variable the bearer token is read from.</summary>
    public const string TokenVariable = "DETENT_TOKEN";
}
