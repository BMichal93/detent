using System.Text.Json.Nodes;
using Detent.Core.Capture;
using Detent.Core.Contracts;

namespace Detent.Core.Tests;

public sealed class ContractScaffolderTests
{
    private static Snapshot Snap(params ToolDescriptor[] tools) => new()
    {
        SchemaVersion = Snapshot.CurrentSchemaVersion,
        Server = new ServerIdentity { Name = "example-mcp" },
        Tools = tools,
        Resources = [],
        Prompts = [],
    };

    private static JsonObject SchemaWith(params string[] props) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(props.Select(p => new KeyValuePair<string, JsonNode?>(p, new JsonObject { ["type"] = "string" }))),
    };

    [Fact]
    public void Scaffolds_one_requirement_per_tool()
    {
        var snapshot = Snap(
            new ToolDescriptor { Name = "search", InputSchema = SchemaWith("query") },
            new ToolDescriptor { Name = "list_orders", InputSchema = SchemaWith("customerId") });

        var contract = ContractScaffolder.FromSnapshot(snapshot, "test-consumer", null);

        Assert.Equal(2, contract.Tools.Count);
        Assert.Equal(["list_orders", "search"], contract.Tools.Select(t => t.Name));
    }

    [Fact]
    public void Sends_captures_top_level_input_properties()
    {
        var snapshot = Snap(new ToolDescriptor { Name = "search", InputSchema = SchemaWith("query", "market") });

        var contract = ContractScaffolder.FromSnapshot(snapshot, "c", null);

        Assert.Equal(new HashSet<string> { "query", "market" }, contract.Tools[0].Sends);
    }

    [Fact]
    public void Reads_captures_top_level_output_properties()
    {
        var snapshot = Snap(new ToolDescriptor
        {
            Name = "search",
            InputSchema = SchemaWith("query"),
            OutputSchema = SchemaWith("sku", "price"),
        });

        var contract = ContractScaffolder.FromSnapshot(snapshot, "c", null);

        Assert.Equal(new HashSet<string> { "sku", "price" }, contract.Tools[0].Reads);
    }

    [Fact]
    public void A_tool_with_no_output_schema_scaffolds_empty_reads()
    {
        var snapshot = Snap(new ToolDescriptor { Name = "search", InputSchema = SchemaWith("query") });

        var contract = ContractScaffolder.FromSnapshot(snapshot, "c", null);

        Assert.Empty(contract.Tools[0].Reads);
    }

    [Fact]
    public void Never_scaffolds_exhaustive_enums_or_assumes()
    {
        var snapshot = Snap(new ToolDescriptor
        {
            Name = "search",
            InputSchema = SchemaWith("query"),
            OutputSchema = SchemaWith("market"),
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        });

        var contract = ContractScaffolder.FromSnapshot(snapshot, "c", null);

        Assert.Empty(contract.Tools[0].ExhaustiveEnums);
        Assert.Null(contract.Tools[0].Assumes);
    }

    [Fact]
    public void A_provider_url_is_recorded_when_given()
    {
        var contract = ContractScaffolder.FromSnapshot(Snap(), "c", "https://mcp.example.com/mcp");

        Assert.Equal("http", contract.Provider!.Transport);
        Assert.Equal("https://mcp.example.com/mcp", contract.Provider.Url);
    }

    [Fact]
    public void No_provider_url_means_no_provider_section()
    {
        var contract = ContractScaffolder.FromSnapshot(Snap(), "c", null);
        Assert.Null(contract.Provider);
    }

    /// <summary>
    /// A $ref-based schema must scaffold the same properties an inline one
    /// would - the diff engine already sees through $ref via SchemaNormaliser,
    /// and a scaffold that could not would silently under-declare sends/reads
    /// for any tool built with $defs.
    /// </summary>
    [Fact]
    public void A_ref_based_schema_still_scaffolds_its_real_properties()
    {
        var schema = new JsonObject
        {
            ["$defs"] = new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
                },
            },
            ["$ref"] = "#/$defs/query",
        };

        var snapshot = Snap(new ToolDescriptor { Name = "search", InputSchema = schema });
        var contract = ContractScaffolder.FromSnapshot(snapshot, "c", null);

        Assert.Equal(["text"], contract.Tools[0].Sends);
    }

    [Fact]
    public void An_empty_snapshot_scaffolds_zero_tools()
    {
        var contract = ContractScaffolder.FromSnapshot(Snap(), "c", null);
        Assert.Empty(contract.Tools);
    }
}

public sealed class ContractYamlWriterTests
{
    private static Contract Contract(params ToolRequirement[] tools) => new()
    {
        ApiVersion = ContractYamlReader.SupportedApiVersion,
        Consumer = "brand-site-agent",
        Tools = tools,
    };

    [Fact]
    public void Written_output_reads_back_to_an_equivalent_contract()
    {
        var original = Contract(new ToolRequirement
        {
            Name = "search_products",
            Sends = new HashSet<string> { "query", "market" },
            Reads = new HashSet<string> { "sku", "price" },
        })
        with
        {
            Provider = new ContractProvider { Transport = "http", Url = "https://mcp.example.com/mcp" },
        };

        var yaml = ContractYamlWriter.Write(original);
        var reparsed = ContractYamlReader.Read(yaml);

        Assert.Equal(original.ApiVersion, reparsed.ApiVersion);
        Assert.Equal(original.Consumer, reparsed.Consumer);
        Assert.Equal(original.Provider!.Url, reparsed.Provider!.Url);
        Assert.Equal(original.Tools[0].Name, reparsed.Tools[0].Name);
        Assert.Equal(original.Tools[0].Sends, reparsed.Tools[0].Sends);
        Assert.Equal(original.Tools[0].Reads, reparsed.Tools[0].Reads);
    }

    [Fact]
    public void A_tool_with_no_sends_or_reads_still_round_trips()
    {
        var original = Contract(new ToolRequirement { Name = "ping" });

        var reparsed = ContractYamlReader.Read(ContractYamlWriter.Write(original));

        Assert.Empty(reparsed.Tools[0].Sends);
        Assert.Empty(reparsed.Tools[0].Reads);
    }

    [Fact]
    public void Zero_tools_still_produces_readable_yaml()
    {
        var reparsed = ContractYamlReader.Read(ContractYamlWriter.Write(Contract()));
        Assert.Empty(reparsed.Tools);
    }

    [Fact]
    public void A_consumer_name_needing_quotes_is_quoted_and_round_trips()
    {
        var original = Contract() with { Consumer = "team: brand site" };

        var reparsed = ContractYamlReader.Read(ContractYamlWriter.Write(original));

        Assert.Equal("team: brand site", reparsed.Consumer);
    }

    [Fact]
    public void Generated_output_contains_guidance_comments()
    {
        var yaml = ContractYamlWriter.Write(Contract());
        Assert.Contains("# Narrow sends/reads", yaml, StringComparison.Ordinal);
    }
}
