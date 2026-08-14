using System.Text.Json.Nodes;
using Detent.Core.Capture;
using Detent.Core.Diff;

namespace Detent.Core.Tests;

/// <summary>
/// The shape edge cases from docs/arch/diff-rules.md §9. Each one is a way two
/// schemas can mean the same thing and still compare as different.
/// </summary>
public sealed class SchemaNormaliserTests
{
    private static string Normalise(string json)
    {
        var result = SchemaNormaliser.Normalise(JsonNode.Parse(json)!.AsObject());
        return CanonicalJson.SerialiseToString(result.Schema);
    }

    private static SchemaNormalisationResult Result(string json)
        => SchemaNormaliser.Normalise(JsonNode.Parse(json)!.AsObject());

    [Fact]
    public void Referenced_and_inline_schemas_converge()
    {
        var referenced = Normalise("""
            {
              "$defs": { "name": { "type": "string", "minLength": 1 } },
              "type": "object",
              "properties": { "who": { "$ref": "#/$defs/name" } }
            }
            """);

        var inline = Normalise("""
            {
              "type": "object",
              "properties": { "who": { "type": "string", "minLength": 1 } }
            }
            """);

        Assert.Equal(inline, referenced);
    }

    [Fact]
    public void Legacy_definitions_keyword_resolves_too()
    {
        var legacy = Normalise("""
            {
              "definitions": { "n": { "type": "number" } },
              "properties": { "x": { "$ref": "#/definitions/n" } }
            }
            """);

        Assert.Equal(Normalise("""{ "properties": { "x": { "type": "number" } } }"""), legacy);
    }

    /// <summary>
    /// An unused definition is a change to nothing anyone can call, so it must
    /// not survive into the compared form.
    /// </summary>
    [Fact]
    public void Definitions_do_not_survive_normalisation()
    {
        Assert.DoesNotContain("$defs", Normalise("""
            { "$defs": { "unused": { "type": "string" } }, "type": "object" }
            """), StringComparison.Ordinal);
    }

    /// <summary>
    /// A recursive schema must produce a finding, not a stack overflow.
    /// </summary>
    [Fact]
    public void Recursive_references_are_reported_not_followed()
    {
        var result = Result("""
            {
              "$defs": {
                "node": {
                  "type": "object",
                  "properties": { "child": { "$ref": "#/$defs/node" } }
                }
              },
              "$ref": "#/$defs/node"
            }
            """);

        Assert.Contains(result.Issues, i => i.Id == "MCPC901");
        Assert.NotNull(result.Schema);
    }

    [Fact]
    public void Mutually_recursive_references_terminate()
    {
        var result = Result("""
            {
              "$defs": {
                "a": { "properties": { "toB": { "$ref": "#/$defs/b" } } },
                "b": { "properties": { "toA": { "$ref": "#/$defs/a" } } }
              },
              "$ref": "#/$defs/a"
            }
            """);

        Assert.Contains(result.Issues, i => i.Id == "MCPC901");
    }

    [Fact]
    public void Unresolvable_local_references_are_reported()
    {
        var result = Result("""{ "properties": { "x": { "$ref": "#/$defs/missing" } } }""");

        Assert.Contains(result.Issues, i => i.Id == "MCPC903");
    }

    /// <summary>
    /// Detent.Core has no network, so an external reference cannot be fetched
    /// even by accident. It is reported instead, which is also the only safe
    /// answer: the pointer comes from an untrusted snapshot.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/schema.json")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("file:///etc/passwd")]
    public void External_references_are_reported_never_fetched(string uri)
    {
        var result = Result($$"""{ "properties": { "x": { "$ref": "{{uri}}" } } }""");

        Assert.Contains(result.Issues, i => i.Id == "MCPC903");
    }

    /// <summary>
    /// Three spellings of the same claim. A server moving between them must not
    /// read as a type change.
    /// </summary>
    [Fact]
    public void The_three_nullability_spellings_converge()
    {
        var asUnion = Normalise("""{ "type": ["string", "null"] }""");
        var asFlag = Normalise("""{ "type": "string", "nullable": true }""");
        var asAnyOf = Normalise("""{ "type": "string", "anyOf": [{ "type": "null" }] }""");

        Assert.Equal(asUnion, asFlag);
        Assert.Equal(asUnion, asAnyOf);
    }

    [Fact]
    public void Single_and_listed_types_converge()
        => Assert.Equal(Normalise("""{ "type": ["string"] }"""), Normalise("""{ "type": "string" }"""));

    [Fact]
    public void Type_order_does_not_matter()
        => Assert.Equal(
            Normalise("""{ "type": ["null", "string"] }"""),
            Normalise("""{ "type": ["string", "null"] }"""));

    /// <summary>
    /// Only unions of bare types collapse. A branch carrying constraints says
    /// something a type array cannot, and flattening it would lose that.
    /// </summary>
    [Fact]
    public void Unions_carrying_constraints_are_left_alone()
    {
        var result = Normalise("""
            { "type": "string", "anyOf": [{ "type": "string", "minLength": 5 }] }
            """);

        Assert.Contains("anyOf", result, StringComparison.Ordinal);
        Assert.Contains("minLength", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// "$ref" is a legal property name. Treating the properties map as a schema
    /// would turn a property called $ref into a reference.
    /// </summary>
    [Fact]
    public void A_property_named_ref_is_not_a_reference()
    {
        var result = Result("""
            { "type": "object", "properties": { "$ref": { "type": "string" } } }
            """);

        Assert.Empty(result.Issues);
        Assert.Contains("$ref", CanonicalJson.SerialiseToString(result.Schema), StringComparison.Ordinal);
    }

    [Fact]
    public void Nesting_past_the_cap_is_reported_and_does_not_overflow()
    {
        var deep = new JsonObject { ["type"] = "object" };
        var cursor = deep;

        for (var i = 0; i < SchemaNormaliser.MaxDepth + 10; i++)
        {
            var child = new JsonObject { ["type"] = "object" };
            cursor["properties"] = new JsonObject { ["next"] = child };
            cursor = child;
        }

        var result = SchemaNormaliser.Normalise(deep);

        Assert.Contains(result.Issues, i => i.Id == "MCPC901");
    }

    /// <summary>diff-rules.md §11: canonicalise must be idempotent.</summary>
    [Theory]
    [InlineData("""{ "type": "string", "nullable": true }""")]
    [InlineData("""{ "$defs": { "a": { "type": "number" } }, "properties": { "x": { "$ref": "#/$defs/a" } } }""")]
    [InlineData("""{ "type": ["string", "null"], "enum": ["a", "b"] }""")]
    public void Normalisation_is_idempotent(string json)
    {
        var once = SchemaNormaliser.Normalise(JsonNode.Parse(json)!.AsObject()).Schema;
        var twice = SchemaNormaliser.Normalise(once).Schema;

        Assert.Equal(CanonicalJson.SerialiseToString(once), CanonicalJson.SerialiseToString(twice));
    }

    /// <summary>diff-rules.md §9.10: empty and absent are different claims.</summary>
    [Fact]
    public void Empty_and_absent_schemas_stay_distinct()
    {
        Assert.Null(SchemaNormaliser.Normalise(null).Schema);
        Assert.NotNull(SchemaNormaliser.Normalise([]).Schema);
    }

    /// <summary>
    /// enum values are data, not schemas. Rewriting them would change what the
    /// server said it accepts.
    /// </summary>
    [Fact]
    public void Enum_values_are_left_untouched()
    {
        var result = Normalise("""{ "type": "string", "enum": ["b", "a", "type"] }""");

        Assert.Contains("""
            "b",
                "a",
                "type"
            """.ReplaceLineEndings("\n"), result, StringComparison.Ordinal);
    }
}
