using System.Text;

namespace Detent.Core.Security;

/// <summary>
/// Renders server-derived text safe to print.
/// </summary>
/// <remarks>
/// The MCP server is untrusted input, and its strings reach a terminal: tool
/// names, descriptions, titles, server instructions, and error text quoted back
/// from a failed call. All of them pass through here first. See
/// <c>docs/arch/security-model.md</c> §1.
/// <para>
/// Control characters are dropped rather than escaped. An escaped sequence is
/// still a sequence that a pager, a log viewer, or a CI web console might
/// re-interpret later, and nothing in a legitimate tool description needs one.
/// </para>
/// <para>
/// The American spelling is deliberate: <c>Sanitize()</c> is the name the
/// security model and the CLAUDE.md guardrail already publish, and a guardrail
/// that names a method nobody can grep for is not a guardrail.
/// </para>
/// </remarks>
public static class Sanitizer
{
    /// <summary>
    /// Strips control and direction-manipulating characters from untrusted text.
    /// </summary>
    /// <remarks>
    /// Tab and newline survive, because a description is worth reading with its
    /// line structure intact and neither can reposition a cursor. Carriage
    /// return does not: it returns the cursor to column zero, which lets a
    /// server overwrite a line already printed and show the operator something
    /// other than what was rendered.
    /// </remarks>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is '\n' or '\t')
            {
                builder.Append(rune);
                continue;
            }

            if (IsDangerous(rune))
            {
                continue;
            }

            builder.Append(rune);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Sanitizes and clips to a budget, for text going into a one-line message.
    /// </summary>
    /// <remarks>
    /// A server controls the length of what it returns as much as the content.
    /// An error message quoting a megabyte of server text is a denial of service
    /// against the person reading the build log.
    /// </remarks>
    public static string SanitizeForMessage(string? value, int maxLength = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var sanitized = Sanitize(value).ReplaceLineEndings(" ").Replace('\t', ' ');

        return sanitized.Length <= maxLength
            ? sanitized
            : string.Concat(sanitized.AsSpan(0, maxLength), "...");
    }

    private static bool IsDangerous(Rune rune) => rune.Value switch
    {
        // C0 controls and DEL. ESC is the escape-sequence introducer, and the
        // rest of the range is cursor and display manipulation.
        <= 0x1F or 0x7F => true,

        // C1 controls. A terminal in 8-bit mode treats 0x9B as CSI, which is the
        // same attack as ESC[ with one byte instead of two.
        >= 0x80 and <= 0x9F => true,

        // Zero-width and invisible formatting. These hide text rather than
        // manipulate the terminal, which matters when the reviewer's decision
        // rests on the text they can see.
        0x200B or 0x200C or 0x200D or 0xFEFF => true,

        // Bidirectional overrides: the Trojan Source class. These reorder
        // rendered text without changing the bytes, so a tool name can display
        // as one thing and compare as another.
        >= 0x200E and <= 0x200F => true,
        >= 0x202A and <= 0x202E => true,
        >= 0x2066 and <= 0x2069 => true,

        // Line and paragraph separators. Some renderers treat these as breaks
        // and some do not, which is exactly the ambiguity to remove.
        0x2028 or 0x2029 => true,

        _ => false,
    };
}
