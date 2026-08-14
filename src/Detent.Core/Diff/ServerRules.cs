using Detent.Core.Policy;

namespace Detent.Core.Diff;

/// <summary>
/// The server-level rules from <c>docs/arch/diff-rules.md</c> §7.
/// </summary>
/// <remarks>
/// MCPC402 (auth scheme or required scopes changed) has no row here.
/// <c>Snapshot</c> carries nothing about authentication - the transport speaks
/// bearer tokens read from the environment, but nothing about what scheme or
/// scopes a server requires is ever captured - so there is no field for this
/// rule to compare. Adding one means teaching <c>Detent.Transport</c> to read
/// <c>WWW-Authenticate</c> or an OAuth protected-resource metadata document,
/// which is new capture surface with its own security shape, not a diff-engine
/// task. Tracked as a gap rather than guessed at.
/// </remarks>
internal static class ServerRules
{
    public static Rule CapabilityRemoved { get; } =
        new("MCPC401", Severity.Breaking, "an advertised capability was removed");

    public static Rule CapabilityAdded { get; } =
        new("MCPC407", Severity.Additive, "an advertised capability was added");

    public static Rule InstructionsChanged { get; } =
        new("MCPC403", Severity.Behavioural, "the server instructions changed");

    public static Rule ProtocolRevisionChanged { get; } =
        new("MCPC404", Severity.Notice, "the protocol revision changed");

    public static Rule DeprecatedSubsystem { get; } =
        new("MCPC405", Severity.Notice, "a deprecated subsystem is in use");

    public static Rule IdentityChanged { get; } =
        new("MCPC406", Severity.Notice, "the server identity changed");

    /// <summary>
    /// Capability keys the 2026-07-28 revision deprecated, each with a minimum
    /// twelve-month support window from that date.
    /// </summary>
    /// <remarks>
    /// Sourced from the project plan rather than re-derived: "the 2026-07-28
    /// revision deprecated three subsystems with a 12-month minimum support
    /// window." Earliest removal is therefore 2027-07-28, not before.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> DeprecatedSubsystems { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["roots"] = "2027-07-28",
            ["sampling"] = "2027-07-28",
            ["logging"] = "2027-07-28",
        };
}
