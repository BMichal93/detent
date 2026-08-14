using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Core.Diff;

/// <summary>
/// The server-level rules from <c>docs/arch/diff-rules.md</c> §7: capabilities,
/// instructions, protocol revision, identity, and deprecated subsystems.
/// </summary>
internal static class ServerComparer
{
    /// <summary>
    /// Runs every server-level rule, or just the revision one if the protocol
    /// revision changed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the protocol revision changed, in which case
    /// <paramref name="findings"/> holds only the MCPC404 notice and nothing
    /// else in this snapshot was compared.
    /// </returns>
    /// <remarks>
    /// diff-rules.md §7 is explicit that a revision change "must suppress the
    /// wall of false breaking changes that a revision bump otherwise produces,"
    /// because it is a re-baseline event, not a compatibility one - the tool
    /// list, the capability set, even the shape of a schema can legitimately
    /// differ between two protocol revisions for reasons that have nothing to
    /// do with what the server actually changed. Diffing across that boundary
    /// as if it were an ordinary comparison would manufacture exactly the alert
    /// fatigue security-model.md warns is how a gate loses its audience, so the
    /// whole comparison stops here rather than only the capability rows.
    /// </remarks>
    public static bool Compare(Snapshot before, Snapshot after, List<Finding> findings)
    {
        if (!string.Equals(before.Server.ProtocolRevision, after.Server.ProtocolRevision, StringComparison.Ordinal))
        {
            findings.Add(Make(ServerRules.ProtocolRevisionChanged, "server/protocolRevision", "protocolRevision"));
            return true;
        }

        CompareCapabilities(before.Capabilities, after.Capabilities, findings);
        CompareInstructions(before.Instructions, after.Instructions, findings);
        CompareIdentity(before.Server, after.Server, findings);
        ReportDeprecatedSubsystems(after.Capabilities, findings);

        return false;
    }

    /// <summary>
    /// A capability is a top-level key of the <c>capabilities</c> object, which
    /// is stored exactly as the server returned it.
    /// </summary>
    private static void CompareCapabilities(JsonObject? before, JsonObject? after, List<Finding> findings)
    {
        var beforeKeys = before?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal) ?? [];
        var afterKeys = after?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var key in beforeKeys.Except(afterKeys).Order(StringComparer.Ordinal))
        {
            findings.Add(Make(ServerRules.CapabilityRemoved, $"capabilities/{key}", key));
        }

        foreach (var key in afterKeys.Except(beforeKeys).Order(StringComparer.Ordinal))
        {
            findings.Add(Make(ServerRules.CapabilityAdded, $"capabilities/{key}", key));
        }
    }

    private static void CompareInstructions(string? before, string? after, List<Finding> findings)
    {
        // Compared in the normalised form, same reasoning as every other
        // description field: a re-wrapped paragraph is not a behaviour change.
        var beforeNormalised = before is null ? null : TextNormaliser.ForComparison(before);
        var afterNormalised = after is null ? null : TextNormaliser.ForComparison(after);

        if (!string.Equals(beforeNormalised, afterNormalised, StringComparison.Ordinal))
        {
            findings.Add(Make(ServerRules.InstructionsChanged, "instructions", "instructions"));
        }
    }

    private static void CompareIdentity(ServerIdentity before, ServerIdentity after, List<Finding> findings)
    {
        if (!string.Equals(
                TextNormaliser.ForComparison(before.Name),
                TextNormaliser.ForComparison(after.Name),
                StringComparison.Ordinal))
        {
            findings.Add(Make(ServerRules.IdentityChanged, "server/name", "name"));
        }

        if (!string.Equals(before.Version, after.Version, StringComparison.Ordinal))
        {
            findings.Add(Make(ServerRules.IdentityChanged, "server/version", "version"));
        }
    }

    /// <summary>
    /// Fires on the candidate alone, per diff-rules.md §7: this is the one rule
    /// that reports a state rather than a transition, so it keeps nagging every
    /// run until the server stops advertising the capability, not just on the
    /// run where it first appears.
    /// </summary>
    /// <remarks>
    /// Checked against the same top-level capability keys MCPC401/407 use.
    /// "roots" and "sampling" are ordinarily client-declared rather than
    /// server-declared in MCP, so this fires only if a server actually surfaces
    /// one of the three keys in its own capabilities object; it cannot detect a
    /// deprecated subsystem a server merely calls into without saying so, since
    /// nothing about that would appear in a snapshot.
    /// </remarks>
    private static void ReportDeprecatedSubsystems(JsonObject? capabilities, List<Finding> findings)
    {
        if (capabilities is null)
        {
            return;
        }

        foreach (var key in capabilities.Select(p => p.Key).Order(StringComparer.Ordinal))
        {
            if (ServerRules.DeprecatedSubsystems.TryGetValue(key, out var earliestRemoval))
            {
                findings.Add(new Finding
                {
                    Id = ServerRules.DeprecatedSubsystem.Id,
                    Severity = ServerRules.DeprecatedSubsystem.Severity,
                    Path = $"capabilities/{key}",
                    Message = $"'{key}' is deprecated and will not be removed before {earliestRemoval}.",
                });
            }
        }
    }

    private static Finding Make(Rule rule, string path, string subject) => new()
    {
        Id = rule.Id,
        Severity = rule.Severity,
        Path = path,
        Message = $"At {path}: {rule.Summary} ({subject}).",
    };
}
