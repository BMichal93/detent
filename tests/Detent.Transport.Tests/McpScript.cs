using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace Detent.Transport.Tests;

/// <summary>
/// A well-behaved MCP server whose answers a test can dictate.
/// </summary>
/// <remarks>
/// Deliberately literal about the protocol rather than sharing code with the
/// probe. A fixture built from the implementation it tests agrees with the
/// implementation by construction and proves nothing.
/// </remarks>
internal sealed class McpScript
{
    public string ServerName { get; init; } = "fake-mcp";

    public string ServerVersion { get; init; } = "1.0.0";

    public string ProtocolVersion { get; init; } = "2026-07-28";

    public JsonObject Capabilities { get; init; } = new() { ["tools"] = new JsonObject() };

    public string? Instructions { get; init; }

    public JsonArray Tools { get; init; } = [];

    public JsonArray Resources { get; init; } = [];

    public JsonArray Prompts { get; init; } = [];

    /// <summary>Entries per page. Zero returns everything at once.</summary>
    public int PageSize { get; init; }

    /// <summary>Returns a cursor forever, to exercise the page cap.</summary>
    public bool PaginateEndlessly { get; init; }

    /// <summary>Answers listings as SSE rather than JSON.</summary>
    public bool UseEventStream { get; init; }

    /// <summary>Methods answered with -32601 regardless of capabilities.</summary>
    public HashSet<string> UnimplementedMethods { get; init; } = [];

    public int InitializeCount { get; private set; }

    public int RequestCount { get; private set; }

    public RequestDelegate Handler => HandleAsync;

    private async Task HandleAsync(HttpContext context)
    {
        RequestCount++;

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var request = JsonNode.Parse(body)!.AsObject();
        var method = request["method"]!.GetValue<string>();

        if (request["id"] is null)
        {
            // A notification. Nothing to correlate, so nothing to answer.
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        var id = request["id"]!.GetValue<long>();

        if (UnimplementedMethods.Contains(method))
        {
            await WriteAsync(context, Error(id, -32601, "Method not found"));
            return;
        }

        var result = method switch
        {
            "initialize" => Initialize(),
            "tools/list" => Page("tools", Tools, request),
            "resources/list" => Page("resources", Resources, request),
            "prompts/list" => Page("prompts", Prompts, request),
            _ => null,
        };

        if (result is null)
        {
            await WriteAsync(context, Error(id, -32601, "Method not found"));
            return;
        }

        if (method == "initialize")
        {
            InitializeCount++;
            context.Response.Headers["Mcp-Session-Id"] = "session-" + ServerName;
        }

        await WriteAsync(context, Success(id, result));
    }

    private JsonObject Initialize()
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = Capabilities.DeepClone(),
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
            },
        };

        if (Instructions is not null)
        {
            result["instructions"] = Instructions;
        }

        return result;
    }

    private JsonObject Page(string collection, JsonArray all, JsonObject request)
    {
        var cursor = request["params"]?["cursor"]?.GetValue<string>();
        var offset = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);

        if (PaginateEndlessly)
        {
            return new JsonObject
            {
                [collection] = new JsonArray(),
                ["nextCursor"] = (offset + 1).ToString(CultureInfo.InvariantCulture),
            };
        }

        var size = PageSize > 0 ? PageSize : all.Count;
        var page = new JsonArray();

        for (var i = offset; i < Math.Min(offset + size, all.Count); i++)
        {
            page.Add(all[i]!.DeepClone());
        }

        var result = new JsonObject { [collection] = page };
        var next = offset + size;

        if (next < all.Count)
        {
            result["nextCursor"] = next.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    private async Task WriteAsync(HttpContext context, JsonObject envelope)
    {
        var json = envelope.ToJsonString();

        if (UseEventStream)
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync($"event: message\ndata: {json}\n\n", context.RequestAborted);
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(json, context.RequestAborted);
    }

    private static JsonObject Success(long id, JsonObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    };

    private static JsonObject Error(long id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };
}
