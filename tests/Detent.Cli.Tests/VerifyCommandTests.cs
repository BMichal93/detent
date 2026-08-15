using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Cli.Tests;

[Collection(nameof(ConsoleTests))]
public sealed class VerifyCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "detent-verify-tests-" + Guid.NewGuid());

    public VerifyCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static Snapshot Snap(params ToolDescriptor[] tools) => new()
    {
        SchemaVersion = Snapshot.CurrentSchemaVersion,
        Server = new ServerIdentity { Name = "example-mcp", ProtocolRevision = "2026-07-28" },
        Tools = tools,
        Resources = [],
        Prompts = [],
    };

    private static ToolDescriptor Tool(
        string name,
        (string, string)[]? inputProps = null,
        (string, string)[]? outputProps = null,
        bool? readOnlyHint = null) => new()
        {
            Name = name,
            InputSchema = SchemaOf(inputProps),
            OutputSchema = outputProps is null ? null : SchemaOf(outputProps),
            Annotations = readOnlyHint is null ? null : new ToolAnnotations { ReadOnlyHint = readOnlyHint },
        };

    private static JsonObject SchemaOf((string, string)[]? props) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(
            (props ?? [("id", "string")]).Select(p =>
                new KeyValuePair<string, JsonNode?>(p.Item1, new JsonObject { ["type"] = p.Item2 }))),
    };

    private string WriteSnapshot(string fileName, Snapshot snapshot)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, SnapshotWriter.Write(snapshot));
        return path;
    }

    private string WriteContract(string yaml)
    {
        var path = Path.Combine(_directory, "contract.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static async Task<CliResult> Verify(params string[] args)
        => await CliInvoker.RunAsync(VerifyCommand.Create(), args);

    // =====================================================================
    // Normal cases
    // =====================================================================

    [Fact]
    public async Task N1_Identical_snapshots_with_a_matching_contract_pass()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", [("query", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", [("query", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  sends: [query]
            """);

        var result = await Verify(before, after, "--contract", contract);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No findings.", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>The plan's own Phase 3 exit criterion, at the CLI level: a removed
    /// output field the contract does not read produces zero findings.</summary>
    [Fact]
    public async Task N2_Removed_output_field_not_in_reads_produces_zero_findings()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", outputProps: [("sku", "string"), ("internal", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", outputProps: [("sku", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  reads: [sku]
            """);

        var result = await Verify(before, after, "--contract", contract);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No findings.", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>The other half of the same criterion: one the contract does
    /// read fails the build.</summary>
    [Fact]
    public async Task N3_Removed_output_field_in_reads_fails_the_build()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", outputProps: [("sku", "string"), ("price", "number")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", outputProps: [("sku", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  reads: [sku, price]
            """);

        var result = await Verify(before, after, "--contract", contract);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("MCPC202", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N4_Removed_input_field_not_in_sends_produces_zero_findings()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", [("query", "string"), ("debug", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", [("query", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  sends: [query]
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task N5_A_tool_not_declared_in_the_contract_has_its_removal_dropped_entirely()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", [("q", "string")]), Tool("legacy_export", [("f", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", [("q", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  sends: [q]
            """);

        var result = await Verify(before, after, "--contract", contract);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("legacy_export", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N6_ExhaustiveEnums_promotes_mcpc208_to_breaking()
    {
        var before = Snap(Tool("search", outputProps: [("market", "string")]) with
        {
            OutputSchema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["market"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("us") } } },
        });
        var after = Snap(Tool("search", outputProps: [("market", "string")]) with
        {
            OutputSchema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["market"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("us", "eu") } } },
        });

        var beforePath = WriteSnapshot("before.json", before);
        var afterPath = WriteSnapshot("after.json", after);
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  reads: [market]
                  exhaustiveEnums: [market]
            """);

        var result = await Verify(beforePath, afterPath, "--contract", contract);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("MCPC208", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N7_Contracts_own_policy_is_used_when_no_cli_override_given()
    {
        // additive normally passes; the contract's own failOn widens it.
        var before = WriteSnapshot("before.json", Snap(Tool("search", [("q", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", [("q", "string"), ("extra", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  sends: [q, extra]
            policy:
              failOn: [additive]
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task N8_Cli_fail_on_overrides_the_contracts_own_policy()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", [("q", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", [("q", "string"), ("extra", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  sends: [q, extra]
            policy:
              failOn: [additive]
            """);

        // CLI override back to the ordinary default: additive should pass now.
        var result = await Verify(before, after, "--contract", contract, "--fail-on", "breaking", "security");
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task N9_Json_format_produces_a_parseable_contract_scoped_report()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", outputProps: [("sku", "string"), ("secret", "string")])));
        var after = WriteSnapshot("after.json", Snap(Tool("search", outputProps: [("sku", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  reads: [sku]
            """);

        var result = await Verify(before, after, "--contract", contract, "--format", "json");

        Assert.Equal(0, result.ExitCode);
        var parsed = JsonNode.Parse(result.StdOut)!;
        Assert.Equal(0, parsed["summary"]!["failures"]!.GetValue<int>());
    }

    [Fact]
    public async Task N10_A_violated_assumption_fails_the_build_as_mcpc501()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("delete_all", readOnlyHint: false)));
        var after = WriteSnapshot("after.json", Snap(Tool("delete_all", readOnlyHint: false)));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: delete_all
                  assumes:
                    readOnlyHint: true
            """);

        var result = await Verify(before, after, "--contract", contract);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("MCPC501", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N11_A_satisfied_assumption_produces_no_finding()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", readOnlyHint: true)));
        var after = WriteSnapshot("after.json", Snap(Tool("search", readOnlyHint: true)));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  assumes:
                    readOnlyHint: true
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task N12_An_active_suppression_passes_the_build_despite_a_breaking_change()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("legacy_export", [("f", "string")])));
        var after = WriteSnapshot("after.json", Snap());
        var contract = WriteContract($"""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: legacy_export
                  sends: [f]
            policy:
              ignore:
                - tool: legacy_export
                  reason: scheduled for removal
                  expires: {DateOnly.FromDateTime(DateTime.Now).AddYears(1):yyyy-MM-dd}
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task N13_An_expired_suppression_no_longer_protects_the_build()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("legacy_export", [("f", "string")])));
        var after = WriteSnapshot("after.json", Snap());
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: legacy_export
                  sends: [f]
            policy:
              ignore:
                - tool: legacy_export
                  reason: scheduled for removal
                  expires: 2020-01-01
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(1, result.ExitCode);
    }

    // =====================================================================
    // Edge cases
    // =====================================================================

    [Fact]
    public async Task E1_Missing_contract_file_is_a_usage_error()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search")));
        var after = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await Verify(before, after, "--contract", Path.Combine(_directory, "nope.yaml"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("detent:", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task E2_Malformed_contract_yaml_is_a_usage_error_not_a_crash()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search")));
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: [unterminated");

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task E3_Missing_baseline_file_is_a_usage_error()
    {
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var result = await Verify(Path.Combine(_directory, "nope.json"), after, "--contract", contract);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task E4_Missing_target_file_is_a_usage_error()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var result = await Verify(before, Path.Combine(_directory, "nope.json"), "--contract", contract);
        Assert.Equal(2, result.ExitCode);
    }

    /// <summary>An empty contract - no tools declared at all - drops every
    /// tool-scoped finding, even a breaking one.</summary>
    [Fact]
    public async Task E5_Contract_with_no_declared_tools_drops_every_tool_scoped_finding()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", [("q", "string")])));
        var after = WriteSnapshot("after.json", Snap());
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(0, result.ExitCode);
    }

    /// <summary>Security findings are never suppressed, even under an active
    /// ignore entry for the same tool.</summary>
    [Fact]
    public async Task E6_A_security_finding_survives_an_active_suppression_for_the_same_tool()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search", readOnlyHint: true)));
        var after = WriteSnapshot("after.json", Snap(Tool("search", readOnlyHint: false)));
        var contract = WriteContract($"""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
            policy:
              ignore:
                - tool: search
                  reason: we accept this risk for now
                  expires: {DateOnly.FromDateTime(DateTime.Now).AddYears(1):yyyy-MM-dd}
            """);

        var result = await Verify(before, after, "--contract", contract);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("MCPC306", result.StdOut, StringComparison.Ordinal);
    }

    /// <summary>exhaustiveEnums on a field absent from reads never gets the
    /// chance to promote anything: the finding is dropped for not being read
    /// before the promotion check ever runs.</summary>
    [Fact]
    public async Task E7_ExhaustiveEnums_on_an_unread_field_has_no_effect()
    {
        var before = Snap(Tool("search") with
        {
            OutputSchema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["market"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("us") } } },
        });
        var after = Snap(Tool("search") with
        {
            OutputSchema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["market"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("us", "eu") } } },
        });

        var beforePath = WriteSnapshot("before.json", before);
        var afterPath = WriteSnapshot("after.json", after);
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  exhaustiveEnums: [market]
            """);

        var result = await Verify(beforePath, afterPath, "--contract", contract);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task E8_Unknown_severity_in_fail_on_is_a_usage_error()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search")));
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var result = await Verify(before, after, "--contract", contract, "--fail-on", "catastrophic");
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task E9_Unknown_format_is_a_usage_error()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search")));
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var result = await Verify(before, after, "--contract", contract, "--format", "xml");
        Assert.Equal(2, result.ExitCode);
    }

    /// <summary>An absent annotation is not the same claim as false, so it
    /// still violates an assumption of true.</summary>
    [Fact]
    public async Task E10_An_absent_annotation_violates_an_assumption_of_true()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search"))); // no annotations at all
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  assumes:
                    readOnlyHint: true
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(1, result.ExitCode);
    }

    /// <summary>A mix of one declared and one undeclared tool in a single run:
    /// the declared tool's findings survive scoping, the undeclared tool's are
    /// dropped, in the same pass.</summary>
    [Fact]
    public async Task E11_Mixed_declared_and_undeclared_tools_are_scoped_independently()
    {
        var before = WriteSnapshot("before.json", Snap(
            Tool("search", outputProps: [("sku", "string"), ("hidden", "string")]),
            Tool("legacy_export", [("f", "string")])));
        var after = WriteSnapshot("after.json", Snap(
            Tool("search", outputProps: [("sku", "string")])));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search
                  reads: [sku]
            """);

        var result = await Verify(before, after, "--contract", contract);

        // search's removed "hidden" field is not in reads (dropped), and
        // legacy_export was never declared at all (dropped, tool removed).
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No findings.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task E12_Json_output_for_a_passing_run_is_still_well_formed()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("search")));
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var result = await Verify(before, after, "--contract", contract, "--format", "json");

        var parsed = JsonNode.Parse(result.StdOut)!;
        Assert.Equal(0, parsed["exitCode"]!.GetValue<int>());
        Assert.Empty(parsed["findings"]!.AsArray());
    }

    /// <summary>An assumption already violated in BOTH before and after -
    /// nothing changed - still fires, proving assumes checking is not
    /// diff-dependent even at the CLI level, not merely at the unit level.</summary>
    [Fact]
    public async Task E13_An_assumption_violated_with_no_underlying_change_still_fires()
    {
        var before = WriteSnapshot("before.json", Snap(Tool("delete_all", readOnlyHint: false)));
        var after = WriteSnapshot("after.json", Snap(Tool("delete_all", readOnlyHint: false)));
        var contract = WriteContract("""
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: delete_all
                  assumes:
                    readOnlyHint: true
            """);

        var result = await Verify(before, after, "--contract", contract);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task E14_A_hand_tampered_baseline_fails_digest_verification()
    {
        var path = WriteSnapshot("before.json", Snap(Tool("search")));
        var after = WriteSnapshot("after.json", Snap(Tool("search")));
        var contract = WriteContract("apiVersion: detent/v1\nconsumer: c\n");

        var tampered = File.ReadAllText(path).Replace("\"search\"", "\"tampered\"", StringComparison.Ordinal);
        File.WriteAllText(path, tampered);

        var result = await Verify(path, after, "--contract", contract);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("digest", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }
}
