namespace Detent.Core.Policy;

/// <summary>
/// The classification assigned to a single finding.
/// </summary>
/// <remarks>
/// These names are normative and are mirrored in <c>docs/arch/diff-rules.md</c>,
/// in contract policy files, and in the JSON and SARIF output formats. Renaming
/// a member is a breaking change to the file formats.
/// <para>
/// The numeric values carry no ranking. Policy is expressed as a set
/// (<c>fail_on: [breaking, security]</c>), never as a threshold, so nothing may
/// compare these values with &lt; or &gt;. Ordering security against breaking is
/// a judgement the user makes in their policy file, not one this enum makes for
/// them.
/// </para>
/// </remarks>
public enum Severity
{
    /// <summary>No semantic effect. Passes, and is hidden from output by default.</summary>
    Cosmetic,

    /// <summary>A new capability that is backwards compatible. Passes.</summary>
    Additive,

    /// <summary>
    /// Information the operator needs but which is not itself a compatibility
    /// event, such as a protocol revision change or a deprecation deadline.
    /// </summary>
    Notice,

    /// <summary>
    /// Schema-compatible, but agent behaviour will likely change. Warns by
    /// default. This class does not exist in ordinary API contract testing and
    /// is the reason MCP needs its own tool: description text is instruction
    /// text to a model.
    /// </summary>
    Behavioural,

    /// <summary>
    /// The trust or blast radius of a tool changed. Fails the build by default.
    /// </summary>
    Security,

    /// <summary>
    /// A conforming consumer will now fail. Fails the build by default.
    /// </summary>
    Breaking,

    /// <summary>
    /// The engine could not analyse the change with confidence, for example a
    /// recursive schema past the depth cap. Surfaced rather than swallowed: the
    /// default posture on anything unrecognised is to flag it.
    /// </summary>
    Unanalysable,
}
