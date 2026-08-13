namespace Detent.Core.Policy;

/// <summary>
/// Process exit codes.
/// </summary>
/// <remarks>
/// Keeping these distinct matters more than it looks. A flaky network must not
/// read as a broken contract, or users will start ignoring the gate.
/// </remarks>
public enum ExitCode
{
    /// <summary>No findings at or above the configured failure classes.</summary>
    Pass = 0,

    /// <summary>Findings matched the policy's failure classes.</summary>
    PolicyViolation = 1,

    /// <summary>Bad arguments, malformed contract, or unreadable baseline.</summary>
    UsageError = 2,

    /// <summary>The target could not be reached, or the transport failed.</summary>
    TransportFailure = 3,

    /// <summary>An unexpected fault in detent itself. Always a bug worth reporting.</summary>
    InternalError = 4,
}
