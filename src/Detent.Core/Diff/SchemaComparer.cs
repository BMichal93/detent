using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Core.Diff;

/// <summary>
/// Walks two normalised schemas and classifies what changed.
/// </summary>
/// <remarks>
/// Takes its classifications from a <see cref="SchemaRules"/> table rather than
/// hard-coding them, so the contravariant and covariant sides cannot
/// accidentally share a rule. See <c>docs/arch/diff-rules.md</c> §1.
/// </remarks>
internal static class SchemaComparer
{
    /// <summary>
    /// Constraints where raising the value rejects more, and so tightens.
    /// </summary>
    private static readonly string[] _lowerBounds =
    [
        "minLength", "minimum", "exclusiveMinimum", "minItems", "minProperties",
    ];

    /// <summary>
    /// Constraints where lowering the value rejects more, and so tightens.
    /// </summary>
    private static readonly string[] _upperBounds =
    [
        "maxLength", "maximum", "exclusiveMaximum", "maxItems", "maxProperties",
    ];

    /// <summary>
    /// Constraints with no ordering. Appearing or changing tightens; vanishing
    /// loosens.
    /// </summary>
    private static readonly string[] _unorderedConstraints =
    [
        "pattern", "multipleOf", "const",
    ];

    public static void Compare(
        JsonObject? before,
        JsonObject? after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        // Absent on both sides, or absent on one. diff-rules.md has no row for a
        // schema appearing or vanishing wholesale, so nothing is invented here.
        if (before is null || after is null)
        {
            return;
        }

        CompareAdditionalProperties(before, after, path, rules, findings);
        CompareTypes(before, after, path, rules, findings);
        CompareEnum(before, after, path, rules, findings);
        CompareConstraints(before, after, path, rules, findings);
        CompareDefault(before, after, path, rules, findings);
        CompareDescription(before, after, path, rules, findings);
        CompareUnions(before, after, path, rules, findings);
        CompareProperties(before, after, path, rules, findings);
    }

    private static void CompareProperties(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        var beforeProperties = before["properties"] as JsonObject;
        var afterProperties = after["properties"] as JsonObject;

        if (beforeProperties is null && afterProperties is null)
        {
            return;
        }

        var beforeRequired = RequiredNames(before);
        var afterRequired = RequiredNames(after);

        var names = new SortedSet<string>(StringComparer.Ordinal);
        names.UnionWith(beforeProperties?.Select(p => p.Key) ?? []);
        names.UnionWith(afterProperties?.Select(p => p.Key) ?? []);

        foreach (var name in names)
        {
            var child = $"{path}/properties/{name}";
            var inBefore = beforeProperties?[name] as JsonObject;
            var inAfter = afterProperties?[name] as JsonObject;

            var existedBefore = beforeProperties?.ContainsKey(name) == true;
            var existsAfter = afterProperties?.ContainsKey(name) == true;

            if (!existedBefore && existsAfter)
            {
                // Whether this breaks callers turns entirely on whether they
                // must now send it, which is why the two IDs are separate rows.
                findings.Add(Make(
                    afterRequired.Contains(name) ? rules.AddRequiredProperty : rules.AddOptionalProperty,
                    child,
                    name));

                continue;
            }

            if (existedBefore && !existsAfter)
            {
                findings.Add(Make(rules.RemoveProperty, child, name));
                continue;
            }

            if (!beforeRequired.Contains(name) && afterRequired.Contains(name))
            {
                findings.Add(Make(rules.OptionalBecomesRequired, child, name));
            }
            else if (beforeRequired.Contains(name) && !afterRequired.Contains(name))
            {
                findings.Add(Make(rules.RequiredBecomesOptional, child, name));
            }

            Compare(inBefore, inAfter, child, rules, findings);
        }
    }

    private static void CompareTypes(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        var beforeTypes = TypeSet(before);
        var afterTypes = TypeSet(after);

        if (beforeTypes.Count == 0 || afterTypes.Count == 0 || beforeTypes.SetEquals(afterTypes))
        {
            return;
        }

        if (afterTypes.IsProperSupersetOf(beforeTypes))
        {
            findings.Add(Make(rules.TypeWidened, path, Join(beforeTypes) + " to " + Join(afterTypes)));
            return;
        }

        // A proper subset narrows. Anything else swapped one type for another,
        // which rejects values that used to be accepted, so it is read the same
        // conservative way rather than as a special case.
        findings.Add(Make(rules.TypeNarrowed, path, Join(beforeTypes) + " to " + Join(afterTypes)));
    }

    private static void CompareEnum(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        if (before["enum"] is not JsonArray beforeValues || after["enum"] is not JsonArray afterValues)
        {
            return;
        }

        var beforeSet = ValueSet(beforeValues);
        var afterSet = ValueSet(afterValues);

        if (afterSet.Except(beforeSet, StringComparer.Ordinal).Any())
        {
            findings.Add(Make(rules.EnumValueAdded, path, "enum"));
        }

        if (beforeSet.Except(afterSet, StringComparer.Ordinal).Any())
        {
            findings.Add(Make(rules.EnumValueRemoved, path, "enum"));
        }
    }

    private static void CompareConstraints(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        // Absent from the output table (§5). Constraints describe what a server
        // accepts; covariance has nothing to say about that side. Both are
        // checked and captured into locals rather than read from rules further
        // down, because narrowing one nullable field does not narrow its
        // sibling, and does not survive a call into another method at all.
        if (rules.ConstraintTightened is not { } tightened
            || rules.ConstraintLoosened is not { } loosened)
        {
            return;
        }

        foreach (var keyword in _lowerBounds)
        {
            CompareBound(before, after, path, keyword, raisingTightens: true, tightened, loosened, findings);
        }

        foreach (var keyword in _upperBounds)
        {
            CompareBound(before, after, path, keyword, raisingTightens: false, tightened, loosened, findings);
        }

        foreach (var keyword in _unorderedConstraints)
        {
            var had = before[keyword];
            var has = after[keyword];

            if (Same(had, has))
            {
                continue;
            }

            // No ordering to reason about, so presence is the only signal: a
            // pattern that appears or changes can only reject more.
            findings.Add(Make(has is null ? loosened : tightened, path, keyword));
        }

        CompareUniqueItems(before, after, path, tightened, loosened, findings);
    }

    private static void CompareBound(
        JsonObject before,
        JsonObject after,
        string path,
        string keyword,
        bool raisingTightens,
        Rule tightened,
        Rule loosened,
        List<Finding> findings)
    {
        var had = Number(before[keyword]);
        var has = Number(after[keyword]);

        if (had == has)
        {
            return;
        }

        // An absent bound is no bound at all. Introducing one tightens and
        // dropping one loosens, whichever direction the bound runs.
        if (had is null)
        {
            findings.Add(Make(tightened, path, keyword));
            return;
        }

        if (has is null)
        {
            findings.Add(Make(loosened, path, keyword));
            return;
        }

        var raised = has > had;
        findings.Add(Make(raised == raisingTightens ? tightened : loosened, path, keyword));
    }

    private static void CompareUniqueItems(
        JsonObject before,
        JsonObject after,
        string path,
        Rule tightened,
        Rule loosened,
        List<Finding> findings)
    {
        var had = before["uniqueItems"]?.GetValueKind() == JsonValueKind.True;
        var has = after["uniqueItems"]?.GetValueKind() == JsonValueKind.True;

        if (had == has)
        {
            return;
        }

        findings.Add(Make(has ? tightened : loosened, path, "uniqueItems"));
    }

    private static void CompareAdditionalProperties(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        // Absent from the output table (§5): a server does not declare which
        // extra fields it might produce, only what it promises.
        if (rules.AdditionalPropertiesOpened is not { } opened
            || rules.AdditionalPropertiesClosed is not { } closed)
        {
            return;
        }

        var had = before["additionalProperties"];
        var has = after["additionalProperties"];

        if (had is null || has is null)
        {
            return;
        }

        var wasOpen = had.GetValueKind() != JsonValueKind.False;
        var isOpen = has.GetValueKind() != JsonValueKind.False;

        if (wasOpen == isOpen)
        {
            return;
        }

        findings.Add(Make(isOpen ? opened : closed, path, "additionalProperties"));
    }

    private static void CompareDefault(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        // Absent from the output table (§5): a default is a value a consumer
        // may omit from a call, which is an input-side concept only.
        if (rules.DefaultAdded is not { } added || rules.DefaultChanged is not { } changed)
        {
            return;
        }

        var had = before["default"];
        var has = after["default"];

        if (Same(had, has) || has is null)
        {
            return;
        }

        findings.Add(Make(had is null ? added : changed, path, "default"));
    }

    private static void CompareDescription(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        var had = Text(before["description"]);
        var has = Text(after["description"]);

        if (had is null && has is null)
        {
            return;
        }

        // Compared in the normalised form, so re-wrapping a description is not a
        // behaviour change. Same reasoning as descriptionSha256 on a tool.
        if (!string.Equals(
                had is null ? null : TextNormaliser.ForComparison(had),
                has is null ? null : TextNormaliser.ForComparison(has),
                StringComparison.Ordinal))
        {
            findings.Add(Make(rules.DescriptionChanged, path, "description"));
        }
    }

    private static void CompareUnions(
        JsonObject before,
        JsonObject after,
        string path,
        SchemaRules rules,
        List<Finding> findings)
    {
        // Absent from the output table (§5): whether a server might produce one
        // more shape than before is exactly the type-widened row, MCPC206, and
        // is already covered there. A separate union row would double-report it.
        if (rules.UnionBranchAdded is not { } added || rules.UnionBranchRemoved is not { } removed)
        {
            return;
        }

        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            var had = BranchSet(before[keyword]);
            var has = BranchSet(after[keyword]);

            if (had.Count == 0 && has.Count == 0)
            {
                continue;
            }

            // Compared as sets of canonical text, so reordering branches is not
            // a change. diff-rules.md §9.2.
            if (has.Except(had, StringComparer.Ordinal).Any())
            {
                findings.Add(Make(added, path, keyword));
            }

            if (had.Except(has, StringComparer.Ordinal).Any())
            {
                findings.Add(Make(removed, path, keyword));
            }
        }
    }

    private static Finding Make(Rule rule, string path, string subject) => new()
    {
        Id = rule.Id,
        Severity = rule.Severity,
        Path = path,
        Message = $"At {path}: {rule.Summary} ({subject}).",
    };

    private static HashSet<string> RequiredNames(JsonObject schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (schema["required"] is JsonArray required)
        {
            names.UnionWith(
                required.Where(n => n?.GetValueKind() == JsonValueKind.String)
                    .Select(n => n!.GetValue<string>()));
        }

        return names;
    }

    private static HashSet<string> TypeSet(JsonObject schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (schema["type"] is JsonArray types)
        {
            names.UnionWith(
                types.Where(t => t?.GetValueKind() == JsonValueKind.String)
                    .Select(t => t!.GetValue<string>()));
        }

        return names;
    }

    private static HashSet<string> ValueSet(JsonArray values)
        => [.. values.Select(CanonicalJson.SerialiseToString)];

    private static HashSet<string> BranchSet(JsonNode? node)
        => node is JsonArray branches
            ? [.. branches.Select(CanonicalJson.SerialiseToString)]
            : [];

    private static string? Text(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static decimal? Number(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.Number
            && decimal.TryParse(
                node.ToJsonString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            ? value
            : null;

    private static bool Same(JsonNode? a, JsonNode? b)
        => (a is null && b is null)
            || (a is not null
                && b is not null
                && string.Equals(
                    CanonicalJson.SerialiseToString(a),
                    CanonicalJson.SerialiseToString(b),
                    StringComparison.Ordinal));

    private static string Join(IEnumerable<string> types)
        => string.Join('|', types.Order(StringComparer.Ordinal));
}
