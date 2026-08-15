using System.Text;
using Detent.Core.Diff;
using Detent.Core.Policy;
using Detent.Core.Security;

namespace Detent.Formats;

/// <summary>
/// Renders a policy outcome for a terminal.
/// </summary>
/// <remarks>
/// Every string that came from the server - a finding's <see cref="Finding.Path"/>
/// and <see cref="Finding.Message"/>, both built from tool names, schema
/// property names, and description text - is tainted, per
/// <c>docs/arch/security-model.md</c> §1 and the taint guardrail in
/// <c>CLAUDE.md</c>. Both go through <see cref="Sanitizer.Sanitize"/> before
/// they reach the returned string. There is no code path in this type that
/// writes either field to the output unsanitised, and the tests for this class
/// exist specifically to keep that true.
/// </remarks>
public static class HumanRenderer
{
    public static string Render(GateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        AppendGroup(builder, "FAIL", result.Failures);
        AppendGroup(builder, "WARN", result.Warnings);
        AppendGroup(builder, "pass", result.Passed);

        var total = result.Failures.Count + result.Warnings.Count + result.Passed.Count;

        if (total == 0)
        {
            builder.Append("No findings.\n");
            return builder.ToString();
        }

        builder.Append(total).Append(total == 1 ? " finding: " : " findings: ")
            .Append(result.Failures.Count).Append(" failure, ")
            .Append(result.Warnings.Count).Append(" warning.\n");

        return builder.ToString();
    }

    private static void AppendGroup(StringBuilder builder, string label, IReadOnlyList<Finding> findings)
    {
        foreach (var finding in findings)
        {
            builder
                .Append(label.PadRight(5)).Append("  ")
                .Append(finding.Id).Append("  ")
                .Append(finding.Severity.ToString().ToLowerInvariant().PadRight(12)).Append(' ')
                .Append(Sanitizer.Sanitize(finding.Path)).Append('\n')
                .Append("      ").Append(Sanitizer.Sanitize(finding.Message)).Append('\n');
        }
    }
}
