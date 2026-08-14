using Detent.Core.Capture;
using Detent.Core.Diff;

namespace Detent.Core.Tests;

/// <summary>
/// MCPC302 boundary cases the golden corpus does not probe. The corpus pins one
/// clear match and one clear non-match; this pins the threshold and the greedy
/// matching itself, both through <see cref="DiffEngine"/> since the detector is
/// internal.
/// </summary>
public sealed class ToolRenameDetectionTests
{
    private static Snapshot Snap(params ToolDescriptor[] tools) => new()
    {
        SchemaVersion = Snapshot.CurrentSchemaVersion,
        Server = new ServerIdentity { Name = "example-mcp" },
        Tools = tools,
        Resources = [],
        Prompts = [],
    };

    private static ToolDescriptor Tool(
        string name,
        string? description = null,
        IReadOnlyDictionary<string, string>? properties = null) => new()
        {
            Name = name,
            Description = description,
            InputSchema = properties is null
            ? null
            : new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "object",
                ["properties"] = new System.Text.Json.Nodes.JsonObject(
                    properties.Select(p =>
                        new KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>(
                            p.Key,
                            new System.Text.Json.Nodes.JsonObject { ["type"] = p.Value }))),
            },
        };

    private static bool HasRename(IReadOnlyList<Finding> findings) => findings.Any(f => f.Id == "MCPC302");

    private static bool HasPair(IReadOnlyList<Finding> findings)
        => findings.Any(f => f.Id == "MCPC301") && findings.Any(f => f.Id == "MCPC303");

    [Fact]
    public void Identical_schema_and_wording_is_detected_as_a_rename()
    {
        var before = Snap(Tool("old_name", "Search the catalogue.", new Dictionary<string, string> { ["q"] = "string" }));
        var after = Snap(Tool("new_name", "Search the catalogue.", new Dictionary<string, string> { ["q"] = "string" }));

        var findings = DiffEngine.Diff(before, after);

        Assert.True(HasRename(findings));
        Assert.False(HasPair(findings));
    }

    [Fact]
    public void No_shared_signal_at_all_is_not_a_rename()
    {
        var before = Snap(Tool("export_data"));
        var after = Snap(Tool("send_email"));

        var findings = DiffEngine.Diff(before, after);

        Assert.False(HasRename(findings));
        Assert.True(HasPair(findings));
    }

    /// <summary>
    /// A description alone, with no schema on either side at all, is enough
    /// signal to match. The score is the mean of whichever components exist,
    /// not a fixed weighting that would penalise a tool for lacking a schema.
    /// </summary>
    [Fact]
    public void Matching_description_alone_is_sufficient_signal()
    {
        var before = Snap(Tool("old_name", "Search the product catalogue for a given market."));
        var after = Snap(Tool("new_name", "Search the product catalogue for a given market."));

        Assert.True(HasRename(DiffEngine.Diff(before, after)));
    }

    /// <summary>
    /// Identical schema plus partially-overlapping wording clears the
    /// threshold even though neither signal alone would. The score is a mean
    /// across whichever components exist, so a real but imperfect match on one
    /// axis genuinely compensates for an imperfect match on another - the
    /// scenario an actual rename produces, where a description is reworded but
    /// not replaced outright. A hard-zero component cannot be compensated for
    /// this way, which the weak-overlap test below pins on the other side.
    /// </summary>
    [Fact]
    public void Partial_wording_overlap_and_full_schema_overlap_together_clear_the_threshold()
    {
        var shape = new Dictionary<string, string> { ["query"] = "string", ["market"] = "string", ["limit"] = "number" };

        // 3 of 4 words shared: Jaccard 0.6. Combined with a schema match of
        // 1.0 the mean is 0.8, above the 0.75 threshold - deliberately chosen
        // so this test would fail if either signal were dropped or reweighted.
        var before = Snap(Tool("search_catalogue", "Search the product catalogue.", shape));
        var after = Snap(Tool("search_products", "Search the product listing.", shape));

        Assert.True(HasRename(DiffEngine.Diff(before, after)));
    }

    /// <summary>
    /// Weak overlap on every axis must not average above the threshold. A
    /// rename claim needs real evidence on at least one axis, not a little bit
    /// of coincidental similarity on several.
    /// </summary>
    [Fact]
    public void Weak_overlap_on_every_axis_does_not_average_into_a_match()
    {
        var before = Snap(Tool(
            "search_catalogue",
            "Search the product catalogue for a given market.",
            new Dictionary<string, string> { ["query"] = "string", ["market"] = "string" }));

        var after = Snap(Tool(
            "delete_account",
            "Permanently remove a user account from the system.",
            new Dictionary<string, string> { ["query"] = "string", ["userId"] = "string" }));

        var findings = DiffEngine.Diff(before, after);

        Assert.False(HasRename(findings));
        Assert.True(HasPair(findings));
    }

    /// <summary>
    /// Two simultaneous renames must not cross-pair. The greedy matcher picks
    /// the best-scoring pair first and never reconsiders a tool already used,
    /// so the strong pairing cannot be split by a weaker candidate on either
    /// side.
    /// </summary>
    [Fact]
    public void Multiple_renames_in_one_diff_do_not_cross_pair()
    {
        var before = Snap(
            Tool("search_catalogue", "Search products.", new Dictionary<string, string> { ["query"] = "string", ["market"] = "string" }),
            Tool("list_orders", "List a customer's orders.", new Dictionary<string, string> { ["customerId"] = "string", ["status"] = "string" }));

        var after = Snap(
            Tool("search_products", "Search products.", new Dictionary<string, string> { ["query"] = "string", ["market"] = "string" }),
            Tool("list_customer_orders", "List a customer's orders.", new Dictionary<string, string> { ["customerId"] = "string", ["status"] = "string" }));

        var findings = DiffEngine.Diff(before, after).Where(f => f.Id == "MCPC302").ToList();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Path == "tools/search_products");
        Assert.Contains(findings, f => f.Path == "tools/list_customer_orders");
    }

    /// <summary>
    /// diff(x, x) must hold here too: a snapshot with itself has no removals or
    /// additions to match, so the detector must not invent a rename against an
    /// unrelated tool present on both sides.
    /// </summary>
    [Fact]
    public void Unrelated_tools_present_on_both_sides_are_not_matched_against_each_other()
    {
        var snapshot = Snap(
            Tool("search_products", "Search products.", new Dictionary<string, string> { ["q"] = "string" }),
            Tool("list_orders", "List orders.", new Dictionary<string, string> { ["id"] = "string" }));

        Assert.Empty(DiffEngine.Diff(snapshot, snapshot));
    }
}
