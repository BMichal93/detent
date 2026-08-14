using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Detent.Core.Security;

namespace Detent.Transport;

/// <summary>
/// The JSON-RPC 2.0 envelope, and the two ways Streamable HTTP returns one.
/// </summary>
/// <remarks>
/// A server may answer a single POST with either a JSON body or an SSE stream,
/// at its own discretion and without warning, so both are handled here rather
/// than at the call site.
/// </remarks>
internal static class JsonRpc
{
    private const string Version = "2.0";

    /// <summary>Well-known code for a method the server does not implement.</summary>
    public const int MethodNotFound = -32601;

    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        // The depth cap has to be enforced by the parser. A document deep enough
        // to matter is deep enough to overflow the stack of anything that walks
        // it afterwards, so it must never be materialised at all.
        MaxDepth = TransportLimits.MaxJsonDepth,
    };

    public static string Request(long id, string method, JsonObject? parameters)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters is not null)
        {
            envelope["params"] = parameters;
        }

        return Serialise(envelope);
    }

    public static string Notification(string method, JsonObject? parameters)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = Version,
            ["method"] = method,
        };

        if (parameters is not null)
        {
            envelope["params"] = parameters;
        }

        return Serialise(envelope);
    }

    /// <summary>
    /// Extracts the response carrying <paramref name="id"/> from a body that may
    /// be an object, a batch, or an SSE stream.
    /// </summary>
    public static JsonRpcResponse Parse(GuardedResponse response, long id)
    {
        if (response.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new TransportException(
                $"The server answered {(int)response.Status}. "
                + $"Set {TransportOptions.TokenVariable} in the environment if it needs a token.");
        }

        if ((int)response.Status >= 400)
        {
            throw new TransportException(
                $"The server answered {(int)response.Status}: "
                + Sanitizer.SanitizeForMessage(Encoding.UTF8.GetString(response.Body)));
        }

        foreach (var payload in Payloads(response))
        {
            var node = ParseNode(payload);

            if (node is JsonArray batch)
            {
                foreach (var item in batch)
                {
                    if (Match(item, id) is { } fromBatch)
                    {
                        return fromBatch;
                    }
                }

                continue;
            }

            if (Match(node, id) is { } matched)
            {
                return matched;
            }
        }

        throw new TransportException(
            $"No JSON-RPC response with id {id.ToString(CultureInfo.InvariantCulture)} in the server's answer.");
    }

    /// <summary>
    /// Yields every JSON document in the body: one for a JSON response, one per
    /// event for an SSE stream.
    /// </summary>
    private static IEnumerable<string> Payloads(GuardedResponse response)
    {
        var text = Encoding.UTF8.GetString(response.Body);

        if (!string.Equals(response.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Length > 0)
            {
                yield return text;
            }

            yield break;
        }

        var data = new StringBuilder();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                // A multi-line data field is rejoined with newlines, per the SSE
                // rules. Other fields (event, id, retry) carry no payload here.
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }

        if (data.Length > 0)
        {
            yield return data.ToString();
        }
    }

    private static JsonNode? ParseNode(string payload)
    {
        try
        {
            return JsonNode.Parse(payload, nodeOptions: null, _documentOptions);
        }
        catch (JsonException ex)
        {
            // The exception text can quote the offending document, which is
            // server-controlled and heading for a console.
            throw new TransportException(
                $"The server's answer is not valid JSON within the depth cap: "
                + Sanitizer.SanitizeForMessage(ex.Message),
                ex);
        }
    }

    private static JsonRpcResponse? Match(JsonNode? node, long id)
    {
        if (node is not JsonObject envelope)
        {
            return null;
        }

        // Notifications and server-initiated requests share the stream with the
        // answer we asked for, so the id is the only reliable selector.
        if (envelope["id"] is not { } candidate || !IdEquals(candidate, id))
        {
            return null;
        }

        if (envelope["error"] is JsonObject error)
        {
            return new JsonRpcResponse
            {
                ErrorCode = error["code"]?.GetValue<int>(),
                ErrorMessage = Sanitizer.SanitizeForMessage(error["message"]?.GetValue<string>()),
            };
        }

        return new JsonRpcResponse
        {
            Result = envelope["result"] as JsonObject,
        };
    }

    private static bool IdEquals(JsonNode candidate, long id)
    {
        // Servers echo the id as a number, but a few normalise it to a string.
        // Refusing those costs correctness for no security benefit.
        return candidate.GetValueKind() switch
        {
            JsonValueKind.Number => candidate.GetValue<long>() == id,
            JsonValueKind.String => long.TryParse(
                candidate.GetValue<string>(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) && parsed == id,
            _ => false,
        };
    }

    private static string Serialise(JsonObject envelope)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            envelope.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

/// <summary>One JSON-RPC response: a result, or an error, never both.</summary>
internal sealed record JsonRpcResponse
{
    public JsonObject? Result { get; init; }

    public int? ErrorCode { get; init; }

    /// <summary>Already sanitized: this text originates at the server.</summary>
    public string? ErrorMessage { get; init; }

    public bool IsError => ErrorCode is not null;
}
