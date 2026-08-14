using Detent.Core.Capture;

namespace Detent.Transport;

/// <summary>
/// Reads the agent-facing surface of one MCP server.
/// </summary>
/// <remarks>
/// One implementation per protocol revision, per ADR-0003. The seam exists so
/// that spec churn stays inside the capture layer: the diff engine takes two
/// snapshots and never learns which revision produced them.
/// </remarks>
public interface IMcpProbe
{
    /// <summary>The protocol revision this implementation speaks.</summary>
    string ProtocolRevision { get; }

    /// <summary>
    /// Captures the server's surface, or throws
    /// <see cref="TransportException"/> if it cannot be reached under the
    /// controls in <c>docs/arch/security-model.md</c>.
    /// </summary>
    Task<Snapshot> CaptureAsync(CancellationToken cancellationToken);
}
