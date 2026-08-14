using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Detent.Core.Capture;
using Detent.Core.Security;

namespace Detent.Transport;

/// <summary>
/// Captures a server over Streamable HTTP, protocol revision 2026-07-28.
/// </summary>
/// <remarks>
/// The slice of MCP this needs is small and stays small: <c>initialize</c>, then
/// one listing call per capability. Nothing here calls a tool, so nothing here
/// can cause a side effect on the server being captured, which is a property
/// worth keeping when the target is production.
/// </remarks>
public sealed class StreamableHttpProbe : IMcpProbe, IDisposable
{
    private const string ClientName = "detent";
    private const string SessionHeader = "Mcp-Session-Id";
    private const string ProtocolHeader = "MCP-Protocol-Version";

    private readonly GuardedHttpClient _client;
    private readonly TransportOptions _options;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    private long _nextId = 1;

    public StreamableHttpProbe(TransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _client = new GuardedHttpClient(options);
    }

    /// <inheritdoc />
    public string ProtocolRevision => "2026-07-28";

    /// <inheritdoc />
    public async Task<Snapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        // The budget covers the whole capture rather than each request, so a
        // server that answers slowly but never quite times out still ends.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TransportLimits.TotalBudget);

        try
        {
            return await CaptureCoreAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransportException(
                $"Capture exceeded its {TransportLimits.TotalBudget.TotalSeconds.ToString(CultureInfo.InvariantCulture)} "
                + "second budget.");
        }
        catch (HttpRequestException ex)
        {
            throw new TransportException(
                $"Cannot reach {_options.Target}: {Sanitizer.SanitizeForMessage(ex.Message)}", ex);
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<Snapshot> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        var initialised = await InitializeAsync(cancellationToken).ConfigureAwait(false);

        // Servers are entitled to reject listing calls made before this, and a
        // stateful one will.
        await NotifyInitializedAsync(cancellationToken).ConfigureAwait(false);

        var capabilities = initialised["capabilities"] as JsonObject;

        var tools = capabilities?["tools"] is not null
            ? await ListAsync("tools/list", "tools", ReadTool, cancellationToken).ConfigureAwait(false)
            : [];

        var resources = capabilities?["resources"] is not null
            ? await ListAsync("resources/list", "resources", ReadResource, cancellationToken).ConfigureAwait(false)
            : [];

        var prompts = capabilities?["prompts"] is not null
            ? await ListAsync("prompts/list", "prompts", ReadPrompt, cancellationToken).ConfigureAwait(false)
            : [];

        var serverInfo = initialised["serverInfo"] as JsonObject;

        return new Snapshot
        {
            SchemaVersion = Snapshot.CurrentSchemaVersion,
            Server = new ServerIdentity
            {
                // A server that will not name itself gets a placeholder rather
                // than a failed capture: the identity is not what is diffed.
                Name = ReadString(serverInfo, "name") ?? "unknown",
                Version = ReadString(serverInfo, "version"),
                ProtocolRevision = ReadString(initialised, "protocolVersion"),
            },
            Capabilities = capabilities?.DeepClone().AsObject(),
            Tools = tools,
            Resources = resources,
            Prompts = prompts,
        };
    }

    private async Task<JsonObject> InitializeAsync(CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["protocolVersion"] = ProtocolRevision,

            // Empty rather than absent: this client consumes nothing and wants
            // nothing pushed to it. Claiming a capability we do not implement
            // would invite a server to open a stream nobody reads.
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = ClientName,
                ["version"] = typeof(StreamableHttpProbe).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            },
        };

        var id = _nextId++;
        var response = await _client
            .PostJsonAsync(_options.Target, JsonRpc.Request(id, "initialize", parameters), _headers, cancellationToken)
            .ConfigureAwait(false);

        // A stateful server hands out a session id here and rejects every later
        // call without it.
        if (response.Headers.TryGetValue(SessionHeader, out var session) && session.Length > 0)
        {
            _headers[SessionHeader] = session;
        }

        _headers[ProtocolHeader] = ProtocolRevision;

        var parsed = JsonRpc.Parse(response, id);

        if (parsed.IsError)
        {
            throw new TransportException(
                $"The server refused initialize: {parsed.ErrorMessage} "
                + $"(code {parsed.ErrorCode?.ToString(CultureInfo.InvariantCulture)}).");
        }

        return parsed.Result
            ?? throw new TransportException("The server answered initialize with no result.");
    }

    private async Task NotifyInitializedAsync(CancellationToken cancellationToken)
    {
        var body = JsonRpc.Notification("notifications/initialized", parameters: null);

        // A notification has no id and therefore no answer to correlate. The
        // response is read only so the connection is not left half-consumed.
        await _client
            .PostJsonAsync(_options.Target, body, _headers, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Walks one paginated listing under the item and page caps.
    /// </summary>
    private async Task<List<T>> ListAsync<T>(
        string method,
        string collection,
        Func<JsonObject, T> read,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? cursor = null;

        for (var page = 0; page < TransportLimits.MaxPagesPerListing; page++)
        {
            var parameters = cursor is null ? null : new JsonObject { ["cursor"] = cursor };
            var id = _nextId++;

            var response = await _client
                .PostJsonAsync(_options.Target, JsonRpc.Request(id, method, parameters), _headers, cancellationToken)
                .ConfigureAwait(false);

            var parsed = JsonRpc.Parse(response, id);

            if (parsed.IsError)
            {
                // A server may advertise a capability and still not implement
                // the listing. That is its problem to fix, not a reason to fail
                // a capture of everything else it does expose.
                if (parsed.ErrorCode == JsonRpc.MethodNotFound)
                {
                    return items;
                }

                throw new TransportException(
                    $"{method} failed: {parsed.ErrorMessage} "
                    + $"(code {parsed.ErrorCode?.ToString(CultureInfo.InvariantCulture)}).");
            }

            var result = parsed.Result
                ?? throw new TransportException($"{method} returned no result.");

            if (result[collection] is JsonArray array)
            {
                foreach (var entry in array)
                {
                    if (entry is not JsonObject item)
                    {
                        throw new TransportException($"{method} returned a non-object in '{collection}'.");
                    }

                    if (items.Count == TransportLimits.MaxItemsPerListing)
                    {
                        throw new TransportException(
                            $"{method} returned more than "
                            + TransportLimits.MaxItemsPerListing.ToString(CultureInfo.InvariantCulture)
                            + " entries.");
                    }

                    items.Add(read(item));
                }
            }

            cursor = ReadString(result, "nextCursor");

            if (cursor is null)
            {
                return items;
            }
        }

        throw new TransportException(
            $"{method} paginated past "
            + TransportLimits.MaxPagesPerListing.ToString(CultureInfo.InvariantCulture)
            + " pages without finishing.");
    }

    private static ToolDescriptor ReadTool(JsonObject tool) => new()
    {
        Name = RequireName(tool, "tool"),
        Description = ReadCappedText(tool, "description"),
        Title = ReadCappedText(tool, "title"),
        InputSchema = ReadObject(tool, "inputSchema"),
        OutputSchema = ReadObject(tool, "outputSchema"),
        Annotations = ReadAnnotations(tool["annotations"] as JsonObject),
    };

    private static ResourceDescriptor ReadResource(JsonObject resource) => new()
    {
        Uri = ReadString(resource, "uri")
            ?? throw new TransportException("A resource in the listing has no uri."),
        Name = ReadCappedText(resource, "name"),
        Description = ReadCappedText(resource, "description"),
        MimeType = ReadString(resource, "mimeType"),
    };

    private static PromptDescriptor ReadPrompt(JsonObject prompt) => new()
    {
        Name = RequireName(prompt, "prompt"),
        Description = ReadCappedText(prompt, "description"),
        Arguments = (prompt["arguments"] as JsonArray)?.DeepClone().AsArray(),
    };

    /// <summary>
    /// Reads the four safety hints, preserving the difference between an absent
    /// one and a false one.
    /// </summary>
    /// <remarks>
    /// Absent and false are different claims, and dropping an assertion is
    /// MCPC310. Defaulting to false here would erase the finding before the
    /// engine ever saw it.
    /// </remarks>
    private static ToolAnnotations? ReadAnnotations(JsonObject? annotations)
    {
        if (annotations is null)
        {
            return null;
        }

        return new ToolAnnotations
        {
            ReadOnlyHint = ReadBool(annotations, "readOnlyHint"),
            DestructiveHint = ReadBool(annotations, "destructiveHint"),
            IdempotentHint = ReadBool(annotations, "idempotentHint"),
            OpenWorldHint = ReadBool(annotations, "openWorldHint"),
        };
    }

    private static string RequireName(JsonObject item, string kind)
    {
        var name = ReadString(item, "name");

        return string.IsNullOrEmpty(name)
            ? throw new TransportException($"A {kind} in the listing has no name.")
            : name;
    }

    /// <summary>
    /// Reads a server-supplied string under the description cap.
    /// </summary>
    /// <remarks>
    /// The text is not sanitized here. A snapshot stores what the server sent so
    /// the diff is honest; sanitizing happens at the point of rendering, which
    /// is the boundary the taint rule actually names.
    /// </remarks>
    private static string? ReadCappedText(JsonObject item, string property)
    {
        var value = ReadString(item, property);

        if (value is null)
        {
            return null;
        }

        if (value.Length > TransportLimits.MaxDescriptionChars)
        {
            throw new TransportException(
                $"A '{property}' exceeds the "
                + TransportLimits.MaxDescriptionChars.ToString(CultureInfo.InvariantCulture)
                + " character cap.");
        }

        return value;
    }

    private static string? ReadString(JsonObject? item, string property)
        => item?[property] is { } node && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    private static bool? ReadBool(JsonObject item, string property)
        => item[property] is { } node && node.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? node.GetValue<bool>()
            : null;

    private static JsonObject? ReadObject(JsonObject item, string property)
        => (item[property] as JsonObject)?.DeepClone().AsObject();
}
