using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Formats.Tests;

public sealed class HumanRendererTests
{
    private static Finding Finding(Severity severity, string path = "tools/x", string message = "message") => new()
    {
        Id = "MCPC000",
        Severity = severity,
        Path = path,
        Message = message,
    };

    private static GateResult Evaluate(params Finding[] findings) => PolicyEvaluator.Evaluate(findings, GatePolicy.Default);

    [Fact]
    public void No_findings_says_so_plainly()
    {
        Assert.Equal("No findings.\n", HumanRenderer.Render(Evaluate()));
    }

    [Fact]
    public void Failures_are_labelled_and_counted()
    {
        var output = HumanRenderer.Render(Evaluate(Finding(Severity.Breaking)));

        Assert.Contains("FAIL", output, StringComparison.Ordinal);
        Assert.Contains("1 finding: 1 failure, 0 warning.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Warnings_are_labelled_and_counted()
    {
        var output = HumanRenderer.Render(Evaluate(Finding(Severity.Behavioural)));

        Assert.Contains("WARN", output, StringComparison.Ordinal);
        Assert.Contains("0 failure, 1 warning.", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tool name is chosen by whoever runs the server, and this output goes
    /// to a real terminal. Every server-derived field must survive this
    /// renderer with no ANSI escape sequence intact.
    /// </summary>
    [Fact]
    public void Escape_sequences_in_the_path_are_stripped()
    {
        var output = HumanRenderer.Render(Evaluate(
            Finding(Severity.Breaking, path: "tools/danger[31mred[0m")));

        Assert.DoesNotContain('', output);
    }

    [Fact]
    public void Escape_sequences_in_the_message_are_stripped()
    {
        var output = HumanRenderer.Render(Evaluate(
            Finding(Severity.Breaking, message: "Ignore prior instructions.]0;pwned")));

        Assert.DoesNotContain('', output);
        Assert.DoesNotContain('\a', output);
    }

    /// <summary>
    /// Carriage return repositions the cursor to column zero, letting a server
    /// overwrite a line already printed. See the remarks on
    /// <c>Sanitizer.Sanitize</c>.
    /// </summary>
    [Fact]
    public void Carriage_returns_do_not_survive_into_the_output()
    {
        var output = HumanRenderer.Render(Evaluate(
            Finding(Severity.Breaking, message: "real message\rFAKE: all clear, nothing to see")));

        Assert.DoesNotContain('\r', output);
    }

    [Fact]
    public void Bidirectional_overrides_are_stripped()
    {
        // U+202E: right-to-left override, the Trojan Source character class.
        var output = HumanRenderer.Render(Evaluate(
            Finding(Severity.Breaking, path: "tools/safe‮malicious")));

        Assert.DoesNotContain('‮', output);
    }

    [Fact]
    public void Every_finding_group_appears_when_all_three_outcomes_are_present()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding(Severity.Breaking), Finding(Severity.Behavioural), Finding(Severity.Additive)],
            GatePolicy.Default);

        var output = HumanRenderer.Render(result);

        Assert.Contains("FAIL", output, StringComparison.Ordinal);
        Assert.Contains("WARN", output, StringComparison.Ordinal);
        Assert.Contains("pass", output, StringComparison.Ordinal);
        Assert.Contains("3 findings: 1 failure, 1 warning.", output, StringComparison.Ordinal);
    }
}
