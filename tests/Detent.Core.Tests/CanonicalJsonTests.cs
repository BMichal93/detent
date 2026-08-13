using System.Text;
using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Core.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void Object_keys_are_sorted_ordinally()
    {
        var node = JsonNode.Parse("""{"zebra":1,"Apple":2,"mango":3}""");

        var json = CanonicalJson.SerialiseToString(node);

        Assert.Equal(
            """
            {
              "Apple": 2,
              "mango": 3,
              "zebra": 1
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    /// <summary>Property reordering is never a diff. Canonical form sorts keys.</summary>
    [Fact]
    public void Reordered_properties_produce_identical_bytes()
    {
        var a = JsonNode.Parse("""{"a":1,"b":{"c":2,"d":3}}""");
        var b = JsonNode.Parse("""{"b":{"d":3,"c":2},"a":1}""");

        Assert.Equal(CanonicalJson.Serialise(a), CanonicalJson.Serialise(b));
    }

    /// <summary>
    /// Array order is semantic in JSON Schema. Sorting enum values or anyOf
    /// branches here would destroy meaning the diff engine needs.
    /// </summary>
    [Fact]
    public void Array_order_is_preserved()
    {
        var node = JsonNode.Parse("""{"enum":["c","a","b"]}""");

        Assert.Contains("""
            "c",
                "a",
                "b"
            """.ReplaceLineEndings("\n"), CanonicalJson.SerialiseToString(node));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1.0", "1")]
    [InlineData("1.00", "1")]
    [InlineData("1.50", "1.5")]
    [InlineData("0.1", "0.1")]
    [InlineData("-0.0", "0")]
    [InlineData("1e2", "100")]
    [InlineData("100", "100")]
    public void Equivalent_numbers_normalise_to_one_spelling(string input, string expected)
    {
        var json = CanonicalJson.SerialiseToString(JsonNode.Parse($"{{\"n\":{input}}}"));

        Assert.Contains($"\"n\": {expected}", json);
    }

    [Fact]
    public void Line_endings_are_lf_regardless_of_host()
    {
        var json = CanonicalJson.SerialiseToString(JsonNode.Parse("""{"a":1,"b":2}"""));

        Assert.DoesNotContain('\r', json);
        Assert.Contains('\n', json);
    }

    [Fact]
    public void Indentation_is_two_spaces()
    {
        var json = CanonicalJson.SerialiseToString(JsonNode.Parse("""{"outer":{"inner":1}}"""));

        Assert.Contains("\n  \"outer\": {\n    \"inner\": 1\n  }\n", json);
    }

    [Fact]
    public void Output_ends_with_exactly_one_newline()
    {
        var json = CanonicalJson.SerialiseToString(JsonNode.Parse("""{"a":1}"""));

        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", json);
    }

    [Fact]
    public void No_line_has_trailing_whitespace()
    {
        var json = CanonicalJson.SerialiseToString(
            JsonNode.Parse("""{"a":{"b":[1,2]},"c":"x"}"""));

        Assert.All(
            json.Split('\n'),
            line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void Output_is_valid_utf8_and_reparses()
    {
        var original = JsonNode.Parse("""{"z":1,"a":{"nested":[1,2,3]}}""");

        var bytes = CanonicalJson.Serialise(original);
        var reparsed = JsonNode.Parse(Encoding.UTF8.GetString(bytes));

        Assert.Equal(CanonicalJson.Serialise(original), CanonicalJson.Serialise(reparsed));
    }

    /// <summary>Canonicalise must be idempotent, per diff-rules.md §11.</summary>
    [Fact]
    public void Serialising_canonical_output_is_a_fixed_point()
    {
        var node = JsonNode.Parse("""{"b":1.50,"a":{"d":2,"c":[3,1]}}""");

        var once = CanonicalJson.Serialise(node);
        var twice = CanonicalJson.Serialise(JsonNode.Parse(Encoding.UTF8.GetString(once)));

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// A snapshot is a committed file that people will cat. Escape sequences in
    /// a server-supplied description must not survive into it raw.
    /// </summary>
    [Fact]
    public void Control_characters_in_strings_are_escaped()
    {
        var node = new JsonObject { ["d"] = "danger[31mred" };

        var json = CanonicalJson.SerialiseToString(node);

        Assert.DoesNotContain('', json);
        Assert.DoesNotContain('', json);
        Assert.Contains("\\u001B", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A JsonValue parsed from text is backed by a JsonElement; one built in
    /// code wraps a raw CLR value. Both reach the writer, and an implementation
    /// that only handles the parsed shape throws on the constructed one.
    /// </summary>
    [Fact]
    public void Constructed_and_parsed_nodes_serialise_identically()
    {
        var constructed = new JsonObject
        {
            ["text"] = "value",
            ["number"] = 1.50m,
            ["flag"] = true,
            ["nothing"] = null,
            ["nested"] = new JsonArray(1, "two", false),
        };

        var parsed = JsonNode.Parse(
            """{"text":"value","number":1.50,"flag":true,"nothing":null,"nested":[1,"two",false]}""");

        Assert.Equal(
            CanonicalJson.SerialiseToString(parsed),
            CanonicalJson.SerialiseToString(constructed));
    }

    [Fact]
    public void Empty_and_absent_objects_stay_distinct()
    {
        var empty = CanonicalJson.SerialiseToString(JsonNode.Parse("""{"schema":{}}"""));
        var absent = CanonicalJson.SerialiseToString(JsonNode.Parse("""{}"""));

        Assert.NotEqual(empty, absent);
    }
}
