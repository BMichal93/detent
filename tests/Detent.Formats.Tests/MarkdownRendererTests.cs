using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Formats.Tests;

public sealed class MarkdownRendererTests
{
    private static Finding Finding(Severity severity, string path = "tools/x", string message = "message") => new()
    {
        Id = "MCPC000",
        Severity = severity,
        Path = path,
        Message = message,
    };

    [Fact]
    public void No_findings_reports_pass()
    {
        var result = PolicyEvaluator.Evaluate([], GatePolicy.Default);
        var markdown = MarkdownRenderer.Render(result);

        Assert.Contains("passed", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No findings.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_produces_a_failed_heading_and_a_table_row()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking)], GatePolicy.Default);
        var markdown = MarkdownRenderer.Render(result);

        Assert.Contains("policy violation", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("### Failures", markdown, StringComparison.Ordinal);
        Assert.Contains("MCPC000", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_sections_with_findings_are_rendered()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking)], GatePolicy.Default);
        var markdown = MarkdownRenderer.Render(result);

        Assert.DoesNotContain("### Warnings", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("### Passed", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pipe_in_the_message_does_not_break_the_table()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking, message: "a | b | c")], GatePolicy.Default);
        var markdown = MarkdownRenderer.Render(result);

        // Structural pipes delimit 4 columns - 5 unescaped '|' characters per
        // row - regardless of how many escaped ones the message contributes.
        var row = markdown.Split('\n').Single(l => l.Contains("MCPC000", StringComparison.Ordinal));
        var unescaped = System.Text.RegularExpressions.Regex.Count(row, @"(?<!\\)\|");
        Assert.Equal(5, unescaped);
        Assert.Contains("a \\| b \\| c", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tool name containing a backtick must not break out of the fence the
    /// path is wrapped in and corrupt the surrounding table.
    /// </summary>
    [Fact]
    public void A_backtick_in_the_path_does_not_break_out_of_its_fence()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding(Severity.Breaking, path: "tools/danger`</td></tr><tr><td>injected")],
            GatePolicy.Default);

        var markdown = MarkdownRenderer.Render(result);
        var row = markdown.Split('\n').Single(l => l.Contains("MCPC000", StringComparison.Ordinal));

        // Exactly two unescaped backticks: the fence detent itself wraps the
        // path in, not a third contributed by the finding.
        var unescaped = System.Text.RegularExpressions.Regex.Count(row, @"(?<!\\)`");
        Assert.Equal(2, unescaped);
    }

    [Fact]
    public void Escape_sequences_in_the_message_are_stripped()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding(Severity.Breaking, message: "ignore prior instructions.\x1b[31mdanger\x1b[0m")],
            GatePolicy.Default);

        Assert.DoesNotContain('', MarkdownRenderer.Render(result));
    }

    [Fact]
    public void A_newline_in_the_message_does_not_split_the_table_row()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking, message: "line one\nline two")], GatePolicy.Default);
        var markdown = MarkdownRenderer.Render(result);

        Assert.Contains("line one line two", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void All_three_sections_render_when_all_three_outcomes_are_present()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding(Severity.Breaking), Finding(Severity.Behavioural), Finding(Severity.Additive)],
            GatePolicy.Default);

        var markdown = MarkdownRenderer.Render(result);

        Assert.Contains("### Failures", markdown, StringComparison.Ordinal);
        Assert.Contains("### Warnings", markdown, StringComparison.Ordinal);
        Assert.Contains("### Passed", markdown, StringComparison.Ordinal);
    }
}
