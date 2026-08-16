using System.Text.Json.Nodes;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Formats.Tests;

public sealed class SarifRendererTests
{
    private static Finding Finding(string id, Severity severity, string path = "tools/x", string message = "message") => new()
    {
        Id = id,
        Severity = severity,
        Path = path,
        Message = message,
    };

    [Fact]
    public void Output_is_schema_and_version_tagged()
    {
        var result = PolicyEvaluator.Evaluate([], GatePolicy.Default);
        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!;

        Assert.Equal("2.1.0", parsed["version"]!.GetValue<string>());
        Assert.Contains("sarif-schema-2.1.0.json", parsed["$schema"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Driver_carries_the_given_tool_version()
    {
        var result = PolicyEvaluator.Evaluate([], GatePolicy.Default);
        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "1.2.3"))!;

        var driver = parsed["runs"]![0]!["tool"]!["driver"]!;
        Assert.Equal("detent", driver["name"]!.GetValue<string>());
        Assert.Equal("1.2.3", driver["version"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(Severity.Breaking, "error")]
    [InlineData(Severity.Security, "error")]
    public void Failures_map_to_sarif_error_level(Severity severity, string expectedLevel)
    {
        var result = PolicyEvaluator.Evaluate([Finding("MCPC000", severity)], GatePolicy.Default);
        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!;

        Assert.Equal(expectedLevel, parsed["runs"]![0]!["results"]![0]!["level"]!.GetValue<string>());
    }

    [Fact]
    public void Warnings_map_to_sarif_warning_level()
    {
        var result = PolicyEvaluator.Evaluate([Finding("MCPC000", Severity.Behavioural)], GatePolicy.Default);
        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!;

        Assert.Equal("warning", parsed["runs"]![0]!["results"]![0]!["level"]!.GetValue<string>());
    }

    [Fact]
    public void Passed_findings_map_to_sarif_note_level()
    {
        var result = PolicyEvaluator.Evaluate([Finding("MCPC000", Severity.Additive)], GatePolicy.Default);
        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!;

        Assert.Equal("note", parsed["runs"]![0]!["results"]![0]!["level"]!.GetValue<string>());
    }

    [Fact]
    public void A_finding_carries_its_path_as_a_logical_location()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding("MCPC301", Severity.Breaking, path: "tools/search_products")],
            GatePolicy.Default);

        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!;
        var location = parsed["runs"]![0]!["results"]![0]!["locations"]![0]!["logicalLocations"]![0]!;

        Assert.Equal("tools/search_products", location["fullyQualifiedName"]!.GetValue<string>());
    }

    [Fact]
    public void The_rule_catalog_contains_one_entry_per_distinct_id()
    {
        var result = PolicyEvaluator.Evaluate(
            [Finding("MCPC301", Severity.Breaking), Finding("MCPC301", Severity.Breaking, path: "tools/y"), Finding("MCPC304", Severity.Behavioural)],
            GatePolicy.Default);

        var rules = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!["runs"]![0]!["tool"]!["driver"]!["rules"]!.AsArray();

        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void No_findings_still_produces_a_valid_empty_run()
    {
        var result = PolicyEvaluator.Evaluate([], GatePolicy.Default);
        var parsed = JsonNode.Parse(SarifRenderer.Render(result, "0.1.0"))!;

        Assert.Empty(parsed["runs"]![0]!["results"]!.AsArray());
        Assert.Empty(parsed["runs"]![0]!["tool"]!["driver"]!["rules"]!.AsArray());
    }

    /// <summary>Machine format read by a JSON parser: JSON string escaping is
    /// the applicable control here, and this pins that it actually applies.</summary>
    [Fact]
    public void Control_characters_in_a_message_stay_valid_json()
    {
        var finding = Finding("MCPC304", Severity.Behavioural, message: "line one\nred\x1b[31m\"quoted\"");
        var result = PolicyEvaluator.Evaluate([finding], GatePolicy.Default);

        var json = SarifRenderer.Render(result, "0.1.0");
        var reparsed = JsonNode.Parse(json)!;

        Assert.Equal(finding.Message, reparsed["runs"]![0]!["results"]![0]!["message"]!["text"]!.GetValue<string>());
    }
}
