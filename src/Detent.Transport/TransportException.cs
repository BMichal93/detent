namespace Detent.Transport;

/// <summary>
/// The target could not be reached, refused a control, or answered in a way the
/// client will not accept.
/// </summary>
/// <remarks>
/// Distinct from a contract failure on purpose. A flaky network that exits with
/// the same code as a broken contract teaches users to ignore the gate, so this
/// maps to <see cref="Detent.Core.Policy.ExitCode.TransportFailure"/> and never
/// to a policy violation. See <c>Detent.Core.Policy.ExitCode</c>.
/// <para>
/// Any server-derived text in <see cref="Exception.Message"/> has already been
/// sanitized by the throw site. The message reaches a console.
/// </para>
/// </remarks>
public sealed class TransportException : Exception
{
    public TransportException()
    {
    }

    public TransportException(string message)
        : base(message)
    {
    }

    public TransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
