using Detent.Core.Capture;
using Detent.Core.Contracts;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Core.Tests;

public sealed class ContractScopeTests
{
    private static Finding Finding(string id, Severity severity, string path) => new()
    {
        Id = id,
        Severity = severity,
        Path = path,
        Message = "test",
    };

    private static Contract Contract(params ToolRequirement[] tools) => new()
    {
        ApiVersion = "detent/v1",
        Consumer = "test-consumer",
        Tools = tools,
    };

    // --- §8: tool-level scoping ---------------------------------------

    [Fact]
    public void A_finding_on_an_undeclared_tool_is_dropped_entirely()
    {
        var contract = Contract(); // no tools declared

        var result = ContractScope.Apply(
            [Finding("MCPC301", Severity.Breaking, "tools/legacy_export")],
            contract);

        Assert.Empty(result);
    }

    /// <summary>
    /// The plan's own exit criterion for this phase, verbatim: a removed
    /// output field the contract does not read produces zero findings.
    /// </summary>
    [Fact]
    public void An_output_property_absent_from_reads_produces_zero_findings()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Reads = new HashSet<string> { "sku", "price" },
        });

        var result = ContractScope.Apply(
            [Finding("MCPC202", Severity.Breaking, "tools/search_products/outputSchema/properties/internalNotes")],
            contract);

        Assert.Empty(result);
    }

    /// <summary>
    /// The other half of the same criterion: one the contract does read
    /// fails the build - i.e. survives scoping unchanged.
    /// </summary>
    [Fact]
    public void An_output_property_present_in_reads_survives_scoping()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Reads = new HashSet<string> { "sku", "price" },
        });

        var finding = Finding("MCPC202", Severity.Breaking, "tools/search_products/outputSchema/properties/price");
        var result = ContractScope.Apply([finding], contract);

        Assert.Equal([finding], result);
    }

    [Fact]
    public void An_input_property_absent_from_sends_is_dropped()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Sends = new HashSet<string> { "query" },
        });

        var result = ContractScope.Apply(
            [Finding("MCPC102", Severity.Breaking, "tools/search_products/inputSchema/properties/market")],
            contract);

        Assert.Empty(result);
    }

    [Fact]
    public void An_input_property_present_in_sends_survives_scoping()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Sends = new HashSet<string> { "query" },
        });

        var finding = Finding("MCPC102", Severity.Breaking, "tools/search_products/inputSchema/properties/query");
        Assert.Equal([finding], ContractScope.Apply([finding], contract));
    }

    /// <summary>
    /// A nested change under a declared top-level property is attributed to
    /// that property, not treated as a separate, unnamed thing.
    /// </summary>
    [Fact]
    public void A_nested_property_change_is_scoped_by_its_top_level_ancestor()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Sends = new HashSet<string> { "filters" },
        });

        var finding = Finding(
            "MCPC102", Severity.Breaking,
            "tools/search_products/inputSchema/properties/filters/properties/region");

        Assert.Equal([finding], ContractScope.Apply([finding], contract));
    }

    [Fact]
    public void A_nested_change_under_an_unsent_property_is_dropped()
    {
        var contract = Contract(new ToolRequirement { Name = "search_products" }); // no sends

        var result = ContractScope.Apply(
            [Finding(
                "MCPC102", Severity.Breaking,
                "tools/search_products/inputSchema/properties/filters/properties/region")],
            contract);

        Assert.Empty(result);
    }

    /// <summary>
    /// A tool-level finding - not scoped to any single property - passes
    /// through for a declared tool regardless of what it sends or reads.
    /// </summary>
    [Theory]
    [InlineData("tools/search_products")]
    [InlineData("tools/search_products/description")]
    [InlineData("tools/search_products/annotations/readOnlyHint")]
    public void Tool_level_findings_are_not_filtered_by_sends_or_reads(string path)
    {
        var contract = Contract(new ToolRequirement { Name = "search_products" }); // empty sends/reads

        var finding = Finding("MCPC304", Severity.Behavioural, path);
        Assert.Equal([finding], ContractScope.Apply([finding], contract));
    }

    /// <summary>
    /// A schema-root-level finding, like additionalProperties flipping, is
    /// not attributable to one named property and is not filtered.
    /// </summary>
    [Fact]
    public void Schema_root_level_findings_are_not_filtered()
    {
        var contract = Contract(new ToolRequirement { Name = "search_products" });

        var finding = Finding("MCPC112", Severity.Breaking, "tools/search_products/inputSchema");
        Assert.Equal([finding], ContractScope.Apply([finding], contract));
    }

    [Fact]
    public void Server_level_findings_are_never_scoped_to_a_tool()
    {
        var contract = Contract(); // no tools declared at all

        var findings = new[]
        {
            Finding("MCPC401", Severity.Breaking, "capabilities/resources"),
            Finding("MCPC403", Severity.Behavioural, "instructions"),
            Finding("MCPC406", Severity.Notice, "server/name"),
        };

        Assert.Equal(findings, ContractScope.Apply(findings, contract));
    }

    [Fact]
    public void Mcpc208_promotes_to_breaking_when_the_field_is_exhaustive()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Reads = new HashSet<string> { "market" },
            ExhaustiveEnums = new HashSet<string> { "market" },
        });

        var result = ContractScope.Apply(
            [Finding("MCPC208", Severity.Behavioural, "tools/search_products/outputSchema/properties/market")],
            contract);

        var promoted = Assert.Single(result);
        Assert.Equal(Severity.Breaking, promoted.Severity);
    }

    [Fact]
    public void Mcpc208_stays_behavioural_when_the_field_is_not_exhaustive()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Reads = new HashSet<string> { "market" },
        });

        var result = ContractScope.Apply(
            [Finding("MCPC208", Severity.Behavioural, "tools/search_products/outputSchema/properties/market")],
            contract);

        Assert.Equal(Severity.Behavioural, Assert.Single(result).Severity);
    }

    /// <summary>Only MCPC208 promotes. Nothing else is upgradeable by a contract.</summary>
    [Fact]
    public void Only_mcpc208_is_ever_promoted()
    {
        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Reads = new HashSet<string> { "market" },
            ExhaustiveEnums = new HashSet<string> { "market" },
        });

        var finding = Finding("MCPC207", Severity.Additive, "tools/search_products/outputSchema/properties/market");
        Assert.Equal(Severity.Additive, Assert.Single(ContractScope.Apply([finding], contract)).Severity);
    }

    // --- §12: assumes -> MCPC501 -----------------------------------------

    private static Snapshot Snapshot(params ToolDescriptor[] tools) => new()
    {
        SchemaVersion = Core.Capture.Snapshot.CurrentSchemaVersion,
        Server = new ServerIdentity { Name = "example-mcp" },
        Tools = tools,
        Resources = [],
        Prompts = [],
    };

    [Fact]
    public void An_assumption_that_holds_produces_no_finding()
    {
        var candidate = Snapshot(new ToolDescriptor
        {
            Name = "search_products",
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        });

        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Assumes = new ToolAssumptions { ReadOnlyHint = true },
        });

        Assert.Empty(ContractScope.CheckAssumptions(candidate, contract));
    }

    [Fact]
    public void A_violated_assumption_is_mcpc501_security_regardless_of_any_diff()
    {
        var candidate = Snapshot(new ToolDescriptor
        {
            Name = "search_products",
            Annotations = new ToolAnnotations { ReadOnlyHint = false },
        });

        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Assumes = new ToolAssumptions { ReadOnlyHint = true },
        });

        var finding = Assert.Single(ContractScope.CheckAssumptions(candidate, contract));
        Assert.Equal("MCPC501", finding.Id);
        Assert.Equal(Severity.Security, finding.Severity);
    }

    /// <summary>
    /// An absent hint is not the value true, so an assumption of true is
    /// still violated - the same "absent is not false" reasoning MCPC310
    /// applies to the diff engine's own annotation comparisons.
    /// </summary>
    [Fact]
    public void An_absent_annotation_violates_an_assumption_of_true()
    {
        var candidate = Snapshot(new ToolDescriptor { Name = "search_products" }); // no annotations at all

        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Assumes = new ToolAssumptions { ReadOnlyHint = true },
        });

        Assert.Single(ContractScope.CheckAssumptions(candidate, contract));
    }

    [Fact]
    public void A_missing_tool_produces_no_assumption_finding()
    {
        var candidate = Snapshot(); // search_products does not exist

        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Assumes = new ToolAssumptions { ReadOnlyHint = true },
        });

        Assert.Empty(ContractScope.CheckAssumptions(candidate, contract));
    }

    [Fact]
    public void No_assumes_declared_checks_nothing()
    {
        var candidate = Snapshot(new ToolDescriptor
        {
            Name = "search_products",
            Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = true },
        });

        var contract = Contract(new ToolRequirement { Name = "search_products" }); // no Assumes

        Assert.Empty(ContractScope.CheckAssumptions(candidate, contract));
    }

    [Fact]
    public void Multiple_violated_hints_on_one_tool_each_produce_their_own_finding()
    {
        var candidate = Snapshot(new ToolDescriptor
        {
            Name = "search_products",
            Annotations = new ToolAnnotations { ReadOnlyHint = false, OpenWorldHint = true },
        });

        var contract = Contract(new ToolRequirement
        {
            Name = "search_products",
            Assumes = new ToolAssumptions { ReadOnlyHint = true, OpenWorldHint = false },
        });

        var findings = ContractScope.CheckAssumptions(candidate, contract);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("MCPC501", f.Id));
    }

    // --- suppressions ------------------------------------------------

    private static ContractPolicy Policy(params Suppression[] ignore) => new() { Ignore = ignore };

    [Fact]
    public void An_active_suppression_drops_findings_for_that_tool()
    {
        var policy = Policy(new Suppression
        {
            Tool = "legacy_export",
            Reason = "scheduled for removal",
            Expires = new DateOnly(2027, 1, 1),
        });

        var result = ContractScope.ApplySuppressions(
            [Finding("MCPC301", Severity.Breaking, "tools/legacy_export")],
            policy,
            today: new DateOnly(2026, 6, 1));

        Assert.Empty(result);
    }

    [Fact]
    public void An_expired_suppression_no_longer_drops_findings()
    {
        var policy = Policy(new Suppression
        {
            Tool = "legacy_export",
            Reason = "scheduled for removal",
            Expires = new DateOnly(2026, 1, 1),
        });

        var finding = Finding("MCPC301", Severity.Breaking, "tools/legacy_export");
        var result = ContractScope.ApplySuppressions([finding], policy, today: new DateOnly(2026, 6, 1));

        Assert.Equal([finding], result);
    }

    /// <summary>An expiry of exactly today is still active - "expires on" is inclusive.</summary>
    [Fact]
    public void A_suppression_expiring_today_is_still_active()
    {
        var policy = Policy(new Suppression
        {
            Tool = "legacy_export",
            Reason = "scheduled for removal",
            Expires = new DateOnly(2026, 6, 1),
        });

        var result = ContractScope.ApplySuppressions(
            [Finding("MCPC301", Severity.Breaking, "tools/legacy_export")],
            policy,
            today: new DateOnly(2026, 6, 1));

        Assert.Empty(result);
    }

    [Fact]
    public void A_security_finding_is_never_suppressed_even_when_active()
    {
        var policy = Policy(new Suppression
        {
            Tool = "search_products",
            Reason = "we accept the risk",
            Expires = new DateOnly(2027, 1, 1),
        });

        var finding = Finding("MCPC306", Severity.Security, "tools/search_products/annotations/readOnlyHint");
        var result = ContractScope.ApplySuppressions([finding], policy, today: new DateOnly(2026, 6, 1));

        Assert.Equal([finding], result);
    }

    [Fact]
    public void No_policy_at_all_leaves_findings_untouched()
    {
        var finding = Finding("MCPC301", Severity.Breaking, "tools/legacy_export");
        Assert.Equal([finding], ContractScope.ApplySuppressions([finding], policy: null, today: new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void A_suppression_for_an_unrelated_tool_does_not_affect_others()
    {
        var policy = Policy(new Suppression
        {
            Tool = "legacy_export",
            Reason = "scheduled for removal",
            Expires = new DateOnly(2027, 1, 1),
        });

        var finding = Finding("MCPC301", Severity.Breaking, "tools/other_tool");
        var result = ContractScope.ApplySuppressions([finding], policy, today: new DateOnly(2026, 6, 1));

        Assert.Equal([finding], result);
    }
}
