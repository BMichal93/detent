using System.Text.Json.Nodes;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Formats.Tests;

public sealed class JsonRendererTests
{
    private static Finding Finding(Severity severity, string id = "MCPC000") => new()
    {
        Id = id,
        Severity = severity,
        Path = "tools/x",
        Message = "message",
    };

    [Fact]
    public void Exit_code_reflects_the_policy_outcome()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking)], GatePolicy.Default);

        var parsed = JsonNode.Parse(JsonRenderer.Render(result))!;

        Assert.Equal(1, parsed["exitCode"]!.GetValue<int>());
    }

    [Fact]
    public void Summary_counts_match_the_partition()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding(Severity.Breaking), Finding(Severity.Behavioural), Finding(Severity.Additive)],
            GatePolicy.Default);

        var parsed = JsonNode.Parse(JsonRenderer.Render(result))!;

        Assert.Equal(1, parsed["summary"]!["failures"]!.GetValue<int>());
        Assert.Equal(1, parsed["summary"]!["warnings"]!.GetValue<int>());
        Assert.Equal(1, parsed["summary"]!["passed"]!.GetValue<int>());
    }

    [Fact]
    public void Each_finding_carries_its_outcome_alongside_its_severity()
    {
        var result = PolicyEvaluator.Evaluate([Finding(Severity.Breaking, "MCPC301")], GatePolicy.Default);

        var finding = JsonNode.Parse(JsonRenderer.Render(result))!["findings"]!.AsArray().Single()!;

        Assert.Equal("MCPC301", finding["id"]!.GetValue<string>());
        Assert.Equal("breaking", finding["severity"]!.GetValue<string>());
        Assert.Equal("fail", finding["outcome"]!.GetValue<string>());
    }

    [Fact]
    public void No_findings_still_produces_valid_json()
    {
        var result = PolicyEvaluator.Evaluate([], GatePolicy.Default);

        var parsed = JsonNode.Parse(JsonRenderer.Render(result))!;

        Assert.Equal(0, parsed["exitCode"]!.GetValue<int>());
        Assert.Empty(parsed["findings"]!.AsArray());
    }

    /// <summary>
    /// This output is a machine format, not a terminal render, so escape
    /// sequences are not the threat model here - the threat is a string that
    /// breaks the JSON structure itself. JSON string escaping already handles
    /// that; this pins that it actually does for the characters that would
    /// otherwise be dangerous in a text render.
    /// </summary>
    [Fact]
    public void Control_characters_in_message_text_stay_valid_json()
    {
        var finding = new Finding
        {
            Id = "MCPC304",
            Severity = Severity.Behavioural,
            Path = "tools/x",
            Message = "line one\nline two\x1b[31mred\x1b[0m\"quoted\"",
        };

        var result = PolicyEvaluator.Evaluate([finding], GatePolicy.Default);

        var json = JsonRenderer.Render(result);
        var reparsed = JsonNode.Parse(json)!;

        Assert.Equal(finding.Message, reparsed["findings"]![0]!["message"]!.GetValue<string>());
    }
}
