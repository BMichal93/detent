using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Cli.Tests;

[Collection(nameof(ConsoleTests))]
public sealed class DiffCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "detent-cli-tests-" + Guid.NewGuid());

    public DiffCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static Snapshot Snap(params ToolDescriptor[] tools) => new()
    {
        SchemaVersion = Snapshot.CurrentSchemaVersion,
        Server = new ServerIdentity { Name = "example-mcp", ProtocolRevision = "2026-07-28" },
        Tools = tools,
        Resources = [],
        Prompts = [],
    };

    private static ToolDescriptor Tool(string name) => new()
    {
        Name = name,
        InputSchema = new JsonObject { ["type"] = "object" },
    };

    private string WriteSnapshot(string fileName, Snapshot snapshot)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, SnapshotWriter.Write(snapshot));
        return path;
    }

    [Fact]
    public async Task Identical_snapshots_pass_with_no_findings()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search")));
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(DiffCommand.Create(), baseline, target);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No findings.", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_removed_tool_fails_the_build_by_default()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search"), Tool("legacy_export")));
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(DiffCommand.Create(), baseline, target);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("MCPC301", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("FAIL", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_added_tool_passes_but_still_appears_in_output()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search")));
        var target = WriteSnapshot("after.json", Snap(Tool("search"), Tool("compare_prices")));

        var result = await CliInvoker.RunAsync(DiffCommand.Create(), baseline, target);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MCPC303", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_format_produces_a_parseable_report_with_the_right_exit_code()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search"), Tool("legacy_export")));
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(DiffCommand.Create(), baseline, target, "--format", "json");

        Assert.Equal(1, result.ExitCode);
        var parsed = JsonNode.Parse(result.StdOut)!;
        Assert.Equal(1, parsed["exitCode"]!.GetValue<int>());
        Assert.Equal(1, parsed["summary"]!["failures"]!.GetValue<int>());
    }

    /// <summary>
    /// --fail-on widens the default policy: a consumer that cannot tolerate
    /// even an additive change can make the build fail on one.
    /// </summary>
    [Fact]
    public async Task Fail_on_can_widen_the_default_policy()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search")));
        var target = WriteSnapshot("after.json", Snap(Tool("search"), Tool("compare_prices")));

        var result = await CliInvoker.RunAsync(
            DiffCommand.Create(), baseline, target, "--fail-on", "additive");

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Missing_baseline_file_is_a_usage_error_not_a_crash()
    {
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(
            DiffCommand.Create(), Path.Combine(_directory, "does-not-exist.json"), target);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("detent:", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_target_file_is_a_usage_error()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(
            DiffCommand.Create(), baseline, Path.Combine(_directory, "does-not-exist.json"));

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task A_hand_edited_baseline_fails_digest_verification()
    {
        var path = WriteSnapshot("before.json", Snap(Tool("search")));
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var tampered = File.ReadAllText(path).Replace("\"search\"", "\"tampered\"", StringComparison.Ordinal);
        File.WriteAllText(path, tampered);

        var result = await CliInvoker.RunAsync(DiffCommand.Create(), path, target);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("digest", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_format_is_a_usage_error()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search")));
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(DiffCommand.Create(), baseline, target, "--format", "xml");

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task Unknown_severity_name_is_a_usage_error()
    {
        var baseline = WriteSnapshot("before.json", Snap(Tool("search")));
        var target = WriteSnapshot("after.json", Snap(Tool("search")));

        var result = await CliInvoker.RunAsync(
            DiffCommand.Create(), baseline, target, "--fail-on", "catastrophic");

        Assert.Equal(2, result.ExitCode);
    }
}
