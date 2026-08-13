using System.Text;

namespace Detent.Core.Capture;

/// <summary>
/// Normalises server-supplied text for storage and for comparison.
/// </summary>
/// <remarks>
/// These are deliberately two different forms. The stored form stays readable,
/// because the primary UX of a snapshot is reading it in a pull request diff.
/// The comparison form is aggressive, because a reflowed paragraph must not be
/// reported as a behaviour change.
/// <para>
/// Requires globalization to be enabled. Under <c>InvariantGlobalization</c>,
/// <see cref="string.Normalize(NormalizationForm)"/> silently returns its input
/// rather than throwing, so a misconfiguration here would not announce itself.
/// See <c>docs/adr/0006-globalization.md</c>.
/// </para>
/// </remarks>
public static class TextNormaliser
{
    /// <summary>
    /// The form written into a snapshot: NFC, with line structure preserved.
    /// </summary>
    public static string ForStorage(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The form fed to the hash the diff engine compares: NFC, with every run of
    /// whitespace collapsed to a single space, trimmed.
    /// </summary>
    /// <remarks>
    /// Collapsing newlines here is what makes a re-wrapped description a no-op
    /// for classification while still showing up in the pull request diff.
    /// </remarks>
    public static string ForComparison(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalised = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalised.Length);
        var pendingSpace = false;

        foreach (var rune in normalised.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                // Defer the space so a trailing run never reaches the output.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune);
        }

        return builder.ToString();
    }
}
