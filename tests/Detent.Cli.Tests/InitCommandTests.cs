using System.Text.Json.Nodes;
using Detent.Core.Capture;
using Detent.Core.Contracts;

namespace Detent.Cli.Tests;

[Collection(nameof(ConsoleTests))]
public sealed class InitCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "detent-init-tests-" + Guid.NewGuid());

    public InitCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

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

    private static ToolDescriptor Tool(string name, string[]? sends = null, string[]? reads = null) => new()
    {
        Name = name,
        InputSchema = SchemaWith(sends ?? []),
        OutputSchema = reads is null ? null : SchemaWith(reads),
    };

    private string WriteSnapshot(Snapshot snapshot)
    {
        var path = Path.Combine(_directory, "snapshot.json");
        File.WriteAllBytes(path, SnapshotWriter.Write(snapshot));
        return path;
    }

    private static Task<CliResult> Init(params string[] args) => CliInvoker.RunAsync(InitCommand.Create(), args);

    [Fact]
    public async Task Scaffolds_one_tool_per_entry_in_the_snapshot()
    {
        var source = WriteSnapshot(Snap(Tool("search", ["query"]), Tool("list_orders", ["customerId"])));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        var result = await Init(source, "--consumer", "test-consumer", "-o", outputPath);

        Assert.Equal(0, result.ExitCode);
        var contract = ContractYamlReader.Read(File.ReadAllText(outputPath));
        Assert.Equal(2, contract.Tools.Count);
    }

    [Fact]
    public async Task Sends_and_reads_are_populated_from_the_schemas()
    {
        var source = WriteSnapshot(Snap(Tool("search", ["query", "market"], ["sku", "price"])));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        await Init(source, "--consumer", "c", "-o", outputPath);

        var contract = ContractYamlReader.Read(File.ReadAllText(outputPath));
        Assert.Equal(new HashSet<string> { "query", "market" }, contract.Tools[0].Sends);
        Assert.Equal(new HashSet<string> { "sku", "price" }, contract.Tools[0].Reads);
    }

    [Fact]
    public async Task The_consumer_name_is_recorded_as_given()
    {
        var source = WriteSnapshot(Snap(Tool("search")));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        await Init(source, "--consumer", "brand-site-agent", "-o", outputPath);

        var contract = ContractYamlReader.Read(File.ReadAllText(outputPath));
        Assert.Equal("brand-site-agent", contract.Consumer);
    }

    [Fact]
    public async Task Scaffolding_from_a_snapshot_file_records_no_provider()
    {
        var source = WriteSnapshot(Snap(Tool("search")));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        await Init(source, "--consumer", "c", "-o", outputPath);

        var contract = ContractYamlReader.Read(File.ReadAllText(outputPath));
        Assert.Null(contract.Provider);
    }

    [Fact]
    public async Task An_empty_snapshot_still_writes_a_valid_readable_contract()
    {
        var source = WriteSnapshot(Snap());
        var outputPath = Path.Combine(_directory, "contract.yaml");

        var result = await Init(source, "--consumer", "c", "-o", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(ContractYamlReader.Read(File.ReadAllText(outputPath)).Tools);
    }

    [Fact]
    public async Task Blank_consumer_is_a_usage_error()
    {
        var source = WriteSnapshot(Snap(Tool("search")));

        var result = await Init(source, "--consumer", " ", "-o", Path.Combine(_directory, "c.yaml"));

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task Missing_source_snapshot_is_a_usage_error_not_a_crash()
    {
        var result = await Init(
            Path.Combine(_directory, "does-not-exist.json"), "--consumer", "c", "-o", Path.Combine(_directory, "c.yaml"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("detent:", result.StdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// "-" writes raw bytes straight to the OS stdout handle
    /// (<c>Console.OpenStandardOutput()</c>), the same deliberate,
    /// encoding-safe pattern <c>CaptureCommand</c> uses - see its own remarks.
    /// <see cref="CliInvoker"/> only captures the <c>Console.Out</c>
    /// <see cref="TextWriter"/>, so it cannot observe those bytes; this pins
    /// only that the dash path does not error, not its content, which a real
    /// subprocess run already confirmed manually in this session.
    /// </summary>
    [Fact]
    public async Task Output_dash_does_not_error_and_writes_no_file()
    {
        var source = WriteSnapshot(Snap(Tool("search", ["query"])));

        var result = await Init(source, "--consumer", "c", "-o", "-");

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "-")));
    }

    [Fact]
    public async Task Writing_to_a_new_nested_directory_creates_it()
    {
        var source = WriteSnapshot(Snap(Tool("search")));
        var outputPath = Path.Combine(_directory, "nested", "dir", "contract.yaml");

        var result = await Init(source, "--consumer", "c", "-o", outputPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
    }

    /// <summary>Never guessed - both require knowledge of the consumer's own code.</summary>
    [Fact]
    public async Task Never_scaffolds_exhaustive_enums_or_assumes()
    {
        var source = WriteSnapshot(Snap(Tool("search", ["query"], ["market"])));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        await Init(source, "--consumer", "c", "-o", outputPath);

        var contract = ContractYamlReader.Read(File.ReadAllText(outputPath));
        Assert.Empty(contract.Tools[0].ExhaustiveEnums);
        Assert.Null(contract.Tools[0].Assumes);
    }

    [Fact]
    public async Task The_generated_file_carries_guidance_comments()
    {
        var source = WriteSnapshot(Snap(Tool("search")));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        await Init(source, "--consumer", "c", "-o", outputPath);

        Assert.Contains("# Narrow sends/reads", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_confirmation_message_reports_the_tool_count()
    {
        var source = WriteSnapshot(Snap(Tool("a"), Tool("b"), Tool("c")));
        var outputPath = Path.Combine(_directory, "contract.yaml");

        var result = await Init(source, "--consumer", "c", "-o", outputPath);

        Assert.Contains("3 tools", result.StdOut, StringComparison.Ordinal);
    }
}
