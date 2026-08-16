using System.Text;
using Detent.Core.Diff;
using Detent.Core.Policy;
using Detent.Core.Security;

namespace Detent.Formats;

/// <summary>
/// Renders a policy outcome as GitHub-flavoured Markdown, for pasting into a
/// pull request comment or a CI job summary.
/// </summary>
/// <remarks>
/// Server-derived text reaches this output too, and a PR comment is rendered
/// by GitHub the same way a terminal would misrender raw ANSI or Trojan-Source
/// characters, so the same taint rule applies as <see cref="HumanRenderer"/>:
/// every finding's <c>Path</c> and <c>Message</c> goes through
/// <see cref="Sanitizer.Sanitize"/> before it reaches the returned string.
/// Every cell is also table-escaped, since Markdown has its own special
/// characters that sanitising alone does not neutralise: a literal <c>|</c>
/// would terminate the cell early, and a literal backtick in a path - which
/// is wrapped in backticks for readability - would break out of that fence
/// and let a server-controlled name corrupt the surrounding table rather than
/// merely display oddly inside its own cell.
/// </remarks>
public static class MarkdownRenderer
{
    public static string Render(GateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var total = result.Failures.Count + result.Warnings.Count + result.Passed.Count;
        var b = new StringBuilder();

        b.Append(result.ExitCode == ExitCode.Pass ? "## ✅ detent: passed" : "## ❌ detent: policy violation")
            .Append('\n').Append('\n');

        if (total == 0)
        {
            b.Append("No findings.\n");
            return b.ToString();
        }

        b.Append(result.Failures.Count).Append(" failure(s), ")
            .Append(result.Warnings.Count).Append(" warning(s), ")
            .Append(result.Passed.Count).Append(" passed.\n\n");

        AppendTable(b, "Failures", result.Failures);
        AppendTable(b, "Warnings", result.Warnings);
        AppendTable(b, "Passed", result.Passed);

        return b.ToString();
    }

    private static void AppendTable(StringBuilder b, string heading, IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
        {
            return;
        }

        b.Append("### ").Append(heading).Append('\n').Append('\n')
            .Append("| Rule | Severity | Location | Message |\n")
            .Append("|---|---|---|---|\n");

        foreach (var finding in findings)
        {
            b.Append("| ").Append(finding.Id)
                .Append(" | ").Append(finding.Severity.ToString().ToLowerInvariant())
                .Append(" | `").Append(Cell(finding.Path)).Append('`')
                .Append(" | ").Append(Cell(finding.Message))
                .Append(" |\n");
        }

        b.Append('\n');
    }

    /// <summary>
    /// Sanitised for the terminal-injection reasons every renderer shares,
    /// then table-cell-escaped. A literal <c>|</c> would terminate the cell
    /// early and corrupt every column after it; a literal backtick would
    /// break out of the fence <see cref="AppendTable"/> wraps the path in and
    /// let a server-controlled name inject Markdown into the surrounding
    /// table rather than merely render oddly inside its own cell.
    /// </summary>
    private static string Cell(string value)
        => Sanitizer.Sanitize(value)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
