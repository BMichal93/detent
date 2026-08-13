using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Detent.Core.Capture;

/// <summary>
/// Deterministic JSON serialisation: sorted keys, two-space indent, LF endings,
/// normalised numbers, trailing newline.
/// </summary>
/// <remarks>
/// A snapshot that serialises differently on two machines, or on two runs, is
/// worthless: nobody commits a file that churns. Every choice here exists to
/// make the output a pure function of the content, and to make the result
/// readable in a pull request diff.
/// </remarks>
public static class CanonicalJson
{
    private static readonly JsonWriterOptions _options = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        // Never Environment.NewLine. The bytes must not depend on the host OS.
        NewLine = "\n",
        // Server text is escaped conservatively rather than relaxed. A snapshot
        // is a committed file, and unescaped control characters in one are a
        // terminal injection vector for anything that cats it.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
    };

    /// <summary>Serialises a node to canonical UTF-8 bytes.</summary>
    public static byte[] Serialise(JsonNode? node)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, _options))
        {
            Write(node, writer);
        }

        // POSIX text files end with a newline, and git renders a missing one as
        // a diff marker on an otherwise unchanged last line.
        var bytes = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(bytes);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    /// <summary>Serialises a node to a canonical UTF-8 string.</summary>
    public static string SerialiseToString(JsonNode? node)
        => System.Text.Encoding.UTF8.GetString(Serialise(node));

    private static void Write(JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;

            case JsonObject obj:
                writer.WriteStartObject();

                // Ordinal, not culture-aware. Sorting must not depend on the
                // host locale any more than line endings do.
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonArray array:
                writer.WriteStartArray();

                // Array order is semantic in JSON Schema (enum, anyOf branches).
                // Sorting here would destroy meaning; the diff engine compares
                // those order-insensitively instead.
                foreach (var item in array)
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();
                break;

            case JsonValue value:
                WriteValue(value, writer);
                break;

            default:
                throw new JsonException($"Unsupported node type {node.GetType().Name}.");
        }
    }

    private static void WriteValue(JsonValue value, Utf8JsonWriter writer)
    {
        // GetValueKind rather than GetValue<JsonElement>. A JsonValue parsed
        // from text is backed by a JsonElement, but one built in code wraps a
        // raw CLR value and throws on that cast. Both reach this writer: the
        // snapshot arrives via SerializeToNode, captured schemas via Parse.
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetValue<string>());
                break;

            case JsonValueKind.Number:
                // ToJsonString yields the literal for either backing store.
                writer.WriteRawValue(NormaliseNumber(value.ToJsonString()), skipInputValidation: true);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(value.GetValue<bool>());
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new JsonException($"Unsupported value kind {value.GetValueKind()}.");
        }
    }

    /// <summary>
    /// Collapses equivalent numeric spellings so <c>1</c>, <c>1.0</c> and
    /// <c>1.00</c> produce identical bytes.
    /// </summary>
    /// <remarks>
    /// Decimal first, because it holds JSON Schema's realistic range exactly and
    /// round-trips without the artefacts binary floating point introduces.
    /// Double is the fallback for values outside decimal's range, where the
    /// shortest round-trippable form is the best available answer.
    /// </remarks>
    private static string NormaliseNumber(string raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
        {
            // "0.###" style drops trailing zeros that decimal otherwise keeps,
            // since decimal preserves the scale it was parsed with.
            var text = dec.ToString("0.#############################", CultureInfo.InvariantCulture);
            return text.Length == 0 ? "0" : text;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl)
            && double.IsFinite(dbl))
        {
            return dbl.ToString("R", CultureInfo.InvariantCulture);
        }

        // Unparseable or non-finite. JSON has no NaN or Infinity, so this is a
        // malformed document rather than something to paper over.
        throw new JsonException($"Cannot normalise numeric literal '{raw}'.");
    }
}
