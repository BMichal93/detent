using System.Text.Json;
using System.Text.Json.Nodes;

namespace Detent.Core.Diff;

/// <summary>
/// Rewrites a JSON Schema into the one form the rules are allowed to compare.
/// </summary>
/// <remarks>
/// Every edge case in <c>docs/arch/diff-rules.md</c> §9 that is about shape
/// rather than classification is settled here. If two schemas mean the same
/// thing, they must reach the rules as the same tree, or the rules will report
/// differences nobody made.
/// <para>
/// External <c>$ref</c> is never fetched. <c>Detent.Core</c> has no network by
/// construction, and a differ that dereferenced a URL out of an untrusted
/// snapshot would be an SSRF vector in the one component that cannot defend
/// itself. Unresolvable references are reported, not chased.
/// </para>
/// </remarks>
public static class SchemaNormaliser
{
    /// <summary>Deepest nesting expanded before giving up.</summary>
    public const int MaxDepth = 64;

    // internal rather than private: SchemaComparer unions these into its
    // "known keyword" set for MCPC902, so an unrecognised keyword is
    // determined from one source of truth instead of two lists that could
    // silently drift apart.
    internal static readonly string[] SingleSchemaKeywords =
    [
        "items", "additionalItems", "additionalProperties", "unevaluatedItems",
        "unevaluatedProperties", "not", "if", "then", "else", "contains", "propertyNames",
    ];

    internal static readonly string[] SchemaListKeywords =
    [
        "anyOf", "oneOf", "allOf", "prefixItems",
    ];

    internal static readonly string[] SchemaMapKeywords =
    [
        "properties", "patternProperties", "dependentSchemas",
    ];

    /// <summary>Normalises a schema, reporting anything it could not analyse.</summary>
    public static SchemaNormalisationResult Normalise(JsonObject? schema)
    {
        if (schema is null)
        {
            return new SchemaNormalisationResult { Schema = null, Issues = [] };
        }

        var issues = new List<SchemaIssue>();
        var root = schema.DeepClone().AsObject();

        var normalised = Visit(root, root, [], path: string.Empty, depth: 0, issues);

        if (normalised is JsonObject obj)
        {
            // Definitions are inlined by now. Leaving them would make an unused
            // definition a diff, which is a change to nothing anybody can call.
            obj.Remove("$defs");
            obj.Remove("definitions");
            return new SchemaNormalisationResult { Schema = obj, Issues = issues };
        }

        return new SchemaNormalisationResult { Schema = null, Issues = issues };
    }

    private static JsonNode? Visit(
        JsonNode? node,
        JsonObject root,
        HashSet<string> expanding,
        string path,
        int depth,
        List<SchemaIssue> issues)
    {
        if (node is not JsonObject schema)
        {
            // A boolean schema, or a non-schema literal. Nothing to normalise.
            return node?.DeepClone();
        }

        if (depth > MaxDepth)
        {
            issues.Add(new SchemaIssue(
                "MCPC901",
                path,
                $"Schema nests deeper than {MaxDepth} levels and was not analysed."));

            return new JsonObject();
        }

        if (schema["$ref"] is { } reference)
        {
            return VisitReference(schema, reference, root, expanding, path, depth, issues);
        }

        var result = new JsonObject();

        foreach (var (key, value) in schema)
        {
            result[key] = VisitKeyword(key, value, root, expanding, path, depth, issues);
        }

        return NormaliseNullability(result);
    }

    private static JsonNode? VisitKeyword(
        string key,
        JsonNode? value,
        JsonObject root,
        HashSet<string> expanding,
        string path,
        int depth,
        List<SchemaIssue> issues)
    {
        if (SingleSchemaKeywords.Contains(key, StringComparer.Ordinal))
        {
            return Visit(value, root, expanding, $"{path}/{key}", depth + 1, issues);
        }

        if (SchemaListKeywords.Contains(key, StringComparer.Ordinal) && value is JsonArray list)
        {
            var branches = new JsonArray();
            var index = 0;

            foreach (var branch in list)
            {
                branches.Add(Visit(branch, root, expanding, $"{path}/{key}/{index++}", depth + 1, issues));
            }

            return branches;
        }

        // Keys here are property names, not keywords, so the map itself is not a
        // schema. Without this, a property legitimately named "$ref" would be
        // read as a reference.
        if (SchemaMapKeywords.Contains(key, StringComparer.Ordinal) && value is JsonObject map)
        {
            var members = new JsonObject();

            foreach (var (name, member) in map)
            {
                members[name] = Visit(member, root, expanding, $"{path}/{key}/{name}", depth + 1, issues);
            }

            return members;
        }

        return value?.DeepClone();
    }

    /// <summary>
    /// Inlines a local reference, or reports why it could not.
    /// </summary>
    /// <remarks>
    /// Sibling keywords are kept and win over the target, which is what
    /// JSON Schema 2020-12 specifies for <c>$ref</c> alongside other keywords.
    /// </remarks>
    private static JsonNode? VisitReference(
        JsonObject schema,
        JsonNode reference,
        JsonObject root,
        HashSet<string> expanding,
        string path,
        int depth,
        List<SchemaIssue> issues)
    {
        var pointer = reference.GetValueKind() == JsonValueKind.String
            ? reference.GetValue<string>()
            : null;

        if (pointer is null || !pointer.StartsWith('#'))
        {
            issues.Add(new SchemaIssue(
                "MCPC903",
                path,
                $"Reference '{pointer ?? "(not a string)"}' is not a local pointer and was not resolved."));

            return new JsonObject();
        }

        // A reference already on the expansion stack is a cycle. Expanding it
        // again would not terminate, and guessing a finite unrolling would
        // invent a schema the server never published.
        if (!expanding.Add(pointer))
        {
            issues.Add(new SchemaIssue(
                "MCPC901",
                path,
                $"Reference '{pointer}' is recursive and was not analysed."));

            return new JsonObject();
        }

        try
        {
            if (Resolve(root, pointer) is not { } target)
            {
                issues.Add(new SchemaIssue(
                    "MCPC903",
                    path,
                    $"Reference '{pointer}' does not resolve within the schema."));

                return new JsonObject();
            }

            var expanded = Visit(target, root, expanding, path, depth + 1, issues);

            if (expanded is not JsonObject merged)
            {
                return expanded;
            }

            foreach (var (key, value) in schema)
            {
                if (key != "$ref")
                {
                    merged[key] = VisitKeyword(key, value, root, expanding, path, depth, issues);
                }
            }

            return NormaliseNullability(merged);
        }
        finally
        {
            expanding.Remove(pointer);
        }
    }

    private static JsonObject? Resolve(JsonObject root, string pointer)
    {
        if (pointer == "#")
        {
            return root;
        }

        JsonNode? current = root;

        foreach (var rawSegment in pointer[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            // RFC 6901 escaping: ~1 is '/', ~0 is '~', and the order matters.
            var segment = Uri.UnescapeDataString(rawSegment).Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            current = current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(segment, out var next) => next,
                JsonArray array when int.TryParse(segment, out var i) && i >= 0 && i < array.Count => array[i],
                _ => null,
            };

            if (current is null)
            {
                return null;
            }
        }

        return current as JsonObject;
    }

    /// <summary>
    /// Collapses the three ways a schema can say "or null" into one.
    /// </summary>
    /// <remarks>
    /// <c>type: ["string","null"]</c>, <c>nullable: true</c>, and an
    /// <c>anyOf</c> carrying a null branch all mean the same thing. Left alone,
    /// a server switching between spellings would produce a type-narrowed
    /// finding for a schema that did not change. The canonical form is a sorted
    /// <c>type</c> array, so single and multiple types also stop differing by
    /// shape alone.
    /// </remarks>
    private static JsonObject NormaliseNullability(JsonObject schema)
    {
        var types = new SortedSet<string>(TypeNames(schema["type"]), StringComparer.Ordinal);

        // OpenAPI's spelling rather than JSON Schema's. Dropped once absorbed,
        // so it cannot also read as a vendor keyword change.
        if (schema["nullable"] is { } nullable
            && nullable.GetValueKind() == JsonValueKind.True
            && types.Count > 0)
        {
            types.Add("null");
            schema.Remove("nullable");
        }

        // A union of nothing but bare types is a type union written the long
        // way. Only collapsed when every branch is bare: a branch carrying
        // constraints means something the type array cannot express.
        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            if (schema[keyword] is JsonArray branches
                && branches.Count > 0
                && types.Count > 0
                && branches.All(IsBareType))
            {
                foreach (var branch in branches)
                {
                    types.UnionWith(TypeNames(branch!["type"]));
                }

                schema.Remove(keyword);
            }
        }

        if (types.Count > 0)
        {
            schema["type"] = new JsonArray([.. types.Select(t => JsonValue.Create(t))]);
        }

        return schema;
    }

    /// <summary>
    /// Whether a union branch says nothing but "this type".
    /// </summary>
    /// <remarks>
    /// Branches are normalised before this runs, so a bare <c>{"type":"null"}</c>
    /// arrives here as <c>{"type":["null"]}</c>. Matching only the string
    /// spelling would silently stop collapsing every union.
    /// </remarks>
    private static bool IsBareType(JsonNode? branch)
        => branch is JsonObject obj && obj.Count == 1 && TypeNames(obj["type"]).Count > 0;

    /// <summary>Type names from either spelling: a string or an array.</summary>
    private static List<string> TypeNames(JsonNode? type)
    {
        var names = new List<string>();

        switch (type)
        {
            case JsonArray declared:
                names.AddRange(
                    declared.Where(e => e?.GetValueKind() == JsonValueKind.String)
                        .Select(e => e!.GetValue<string>()));
                break;

            case { } single when single.GetValueKind() == JsonValueKind.String:
                names.Add(single.GetValue<string>());
                break;

            default:
                break;
        }

        return names;
    }
}

/// <summary>A normalised schema, plus anything that could not be analysed.</summary>
public sealed record SchemaNormalisationResult
{
    public required JsonObject? Schema { get; init; }

    /// <summary>
    /// Reported rather than swallowed. The default posture on anything the
    /// engine cannot analyse is to say so; see diff-rules.md §10.
    /// </summary>
    public required IReadOnlyList<SchemaIssue> Issues { get; init; }
}

/// <summary>Something the normaliser could not resolve or could not analyse.</summary>
public sealed record SchemaIssue(string Id, string Path, string Message);
