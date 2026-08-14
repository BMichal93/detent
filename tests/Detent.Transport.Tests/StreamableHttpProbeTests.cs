using System.Text.Json.Nodes;
using Detent.Core.Capture;
using Microsoft.AspNetCore.Http;

namespace Detent.Transport.Tests;

/// <summary>
/// The capture conversation against a server that behaves.
/// </summary>
public sealed class StreamableHttpProbeTests
{
    private static JsonArray SampleTools() =>
    [
        new JsonObject
        {
            ["name"] = "search_products",
            ["description"] = "Search the product catalogue.",
            ["inputSchema"] = new JsonObject { ["type"] = "object" },
            ["annotations"] = new JsonObject { ["readOnlyHint"] = true },
        },
        new JsonObject
        {
            ["name"] = "delete_product",
            ["inputSchema"] = new JsonObject { ["type"] = "object" },
        },
    ];

    private static async Task<Snapshot> CaptureAsync(McpScript script)
    {
        await using var server = await FakeServer.StartAsync(script.Handler);
        using var probe = new StreamableHttpProbe(server.Options());

        return await probe.CaptureAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Captures_server_identity_and_tools()
    {
        var snapshot = await CaptureAsync(new McpScript { Tools = SampleTools() });

        Assert.Equal("fake-mcp", snapshot.Server.Name);
        Assert.Equal("1.0.0", snapshot.Server.Version);
        Assert.Equal("2026-07-28", snapshot.Server.ProtocolRevision);
        Assert.Equal(2, snapshot.Tools.Count);
    }

    [Fact]
    public async Task Instructions_are_captured_when_the_server_sends_them()
    {
        var snapshot = await CaptureAsync(new McpScript
        {
            Tools = SampleTools(),
            Instructions = "Always search before attempting a purchase.",
        });

        Assert.Equal("Always search before attempting a purchase.", snapshot.Instructions);
    }

    [Fact]
    public async Task Instructions_are_absent_when_the_server_does_not_send_them()
    {
        var snapshot = await CaptureAsync(new McpScript { Tools = SampleTools() });

        Assert.Null(snapshot.Instructions);
    }

    /// <summary>
    /// Absent and false are different claims, and MCPC310 depends on the
    /// difference surviving the transport.
    /// </summary>
    [Fact]
    public async Task Absent_annotations_stay_absent()
    {
        var snapshot = await CaptureAsync(new McpScript { Tools = SampleTools() });

        var search = snapshot.Tools.Single(t => t.Name == "search_products");
        var delete = snapshot.Tools.Single(t => t.Name == "delete_product");

        Assert.True(search.Annotations!.ReadOnlyHint);
        Assert.Null(search.Annotations.DestructiveHint);
        Assert.Null(delete.Annotations);
    }

    /// <summary>
    /// Nothing in a listing is a capture failure. A server may legitimately
    /// expose no resources.
    /// </summary>
    [Fact]
    public async Task Capabilities_the_server_does_not_advertise_are_not_requested()
    {
        var snapshot = await CaptureAsync(new McpScript
        {
            Capabilities = new JsonObject { ["tools"] = new JsonObject() },
            Tools = SampleTools(),
        });

        Assert.Empty(snapshot.Resources);
        Assert.Empty(snapshot.Prompts);
    }

    /// <summary>
    /// A server may advertise a capability and still not implement the listing.
    /// That is its defect, not a reason to lose the rest of the capture.
    /// </summary>
    [Fact]
    public async Task Advertised_but_unimplemented_listings_are_tolerated()
    {
        var snapshot = await CaptureAsync(new McpScript
        {
            Capabilities = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject(),
            },
            Tools = SampleTools(),
            UnimplementedMethods = ["resources/list"],
        });

        Assert.Equal(2, snapshot.Tools.Count);
        Assert.Empty(snapshot.Resources);
    }

    [Fact]
    public async Task Paginated_listings_are_walked_to_the_end()
    {
        var tools = new JsonArray();

        for (var i = 0; i < 25; i++)
        {
            tools.Add(new JsonObject { ["name"] = $"tool_{i:D2}" });
        }

        var snapshot = await CaptureAsync(new McpScript { Tools = tools, PageSize = 4 });

        Assert.Equal(25, snapshot.Tools.Count);
    }

    /// <summary>
    /// Streamable HTTP lets a server answer a plain request with an event
    /// stream, without negotiation.
    /// </summary>
    [Fact]
    public async Task Server_sent_event_responses_are_understood()
    {
        var snapshot = await CaptureAsync(new McpScript
        {
            Tools = SampleTools(),
            UseEventStream = true,
        });

        Assert.Equal(2, snapshot.Tools.Count);
    }

    [Fact]
    public async Task Resources_and_prompts_are_captured_when_advertised()
    {
        var snapshot = await CaptureAsync(new McpScript
        {
            Capabilities = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject(),
                ["prompts"] = new JsonObject(),
            },
            Tools = SampleTools(),
            Resources = [new JsonObject { ["uri"] = "file:///catalogue", ["name"] = "catalogue" }],
            Prompts = [new JsonObject { ["name"] = "summarise", ["description"] = "Summarise a product." }],
        });

        Assert.Equal("file:///catalogue", Assert.Single(snapshot.Resources).Uri);
        Assert.Equal("summarise", Assert.Single(snapshot.Prompts).Name);
    }

    /// <summary>
    /// The Phase 1 exit criterion, end to end rather than at the writer alone.
    /// </summary>
    [Fact]
    public async Task Ten_consecutive_captures_are_byte_identical()
    {
        var script = new McpScript
        {
            Capabilities = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject(),
            },
            Tools = SampleTools(),
            Resources = [new JsonObject { ["uri"] = "file:///catalogue" }],
        };

        await using var server = await FakeServer.StartAsync(script.Handler);

        byte[]? first = null;

        for (var i = 0; i < 10; i++)
        {
            using var probe = new StreamableHttpProbe(server.Options());
            var bytes = SnapshotWriter.Write(await probe.CaptureAsync(CancellationToken.None));

            first ??= bytes;
            Assert.Equal(first, bytes);
        }
    }

    /// <summary>
    /// A server that reorders or repaginates its listing is not a change. This
    /// is the capture-side half of the property SnapshotWriter pins.
    /// </summary>
    [Fact]
    public async Task Listing_order_and_page_size_do_not_change_the_bytes()
    {
        var forwards = SampleTools();
        var backwards = new JsonArray(SampleTools().Reverse().Select(t => t!.DeepClone()).ToArray());

        await using var a = await FakeServer.StartAsync(new McpScript { Tools = forwards }.Handler);
        await using var b = await FakeServer.StartAsync(new McpScript { Tools = backwards, PageSize = 1 }.Handler);

        using var probeA = new StreamableHttpProbe(a.Options());
        using var probeB = new StreamableHttpProbe(b.Options());

        var first = SnapshotWriter.Write(await probeA.CaptureAsync(CancellationToken.None));
        var second = SnapshotWriter.Write(await probeB.CaptureAsync(CancellationToken.None));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Nothing in a capture calls a tool. Capturing production must not have a
    /// side effect on it.
    /// </summary>
    [Fact]
    public async Task Capture_never_calls_a_tool()
    {
        var methods = new List<string>();

        var script = new McpScript { Tools = SampleTools() };

        await using var server = await FakeServer.StartAsync(async context =>
        {
            context.Request.EnableBuffering();

            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                methods.Add(JsonNode.Parse(body)!["method"]!.GetValue<string>());
            }

            context.Request.Body.Position = 0;
            await script.Handler(context);
        });

        using var probe = new StreamableHttpProbe(server.Options());
        await probe.CaptureAsync(CancellationToken.None);

        Assert.DoesNotContain("tools/call", methods);
        Assert.Contains("initialize", methods);
        Assert.Contains("notifications/initialized", methods);
    }
}
