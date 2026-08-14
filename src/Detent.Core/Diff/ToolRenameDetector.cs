using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Core.Diff;

/// <summary>
/// Matches a removed tool against an added tool by shape, for MCPC302.
/// </summary>
/// <remarks>
/// diff-rules.md §6 requires rename detection "via schema similarity" and is
/// explicit that the metric and threshold are implementation detail; only the
/// choice between emitting one finding or two is pinned by golden cases. This
/// class is where that detail lives.
/// <para>
/// The threshold is set high on purpose. Getting a rename claim wrong is not a
/// severity mistake - MCPC302 and the MCPC301/MCPC303 pair it replaces are both
/// <c>breaking</c> overall - it is a trust mistake: telling a reviewer two
/// unrelated tools are "the same tool, renamed" when they are not misleads the
/// one part of the report a human is meant to read literally. A missed rename
/// degrades gracefully to the pair; a false one does not degrade at all.
/// </para>
/// </remarks>
internal static class ToolRenameDetector
{
    /// <summary>
    /// How much shape a removed and an added tool must share before they are
    /// reported as one rename rather than a removal and an addition.
    /// </summary>
    internal const double Threshold = 0.75;

    /// <summary>
    /// Matches removed tools against added tools, at most one pairing per tool
    /// on either side.
    /// </summary>
    /// <remarks>
    /// Greedy by descending score: every candidate pair at or above the
    /// threshold is considered, highest similarity first, and a tool already
    /// claimed by an earlier, better match is not reconsidered. This is a
    /// deterministic approximation of maximum-weight matching, which is enough
    /// here - a server rarely renames more than one or two tools in a release,
    /// and ties are broken on name so the result never depends on input order.
    /// </remarks>
    public static IReadOnlyList<(ToolDescriptor Removed, ToolDescriptor Added)> Match(
        IReadOnlyList<ToolDescriptor> removed,
        IReadOnlyList<ToolDescriptor> added)
    {
        var candidates = new List<(ToolDescriptor Removed, ToolDescriptor Added, double Score)>();

        foreach (var r in removed)
        {
            foreach (var a in added)
            {
                var score = Score(r, a);

                if (score >= Threshold)
                {
                    candidates.Add((r, a, score));
                }
            }
        }

        candidates.Sort((x, y) =>
        {
            var byScore = y.Score.CompareTo(x.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            var byRemoved = string.CompareOrdinal(x.Removed.Name, y.Removed.Name);
            return byRemoved != 0 ? byRemoved : string.CompareOrdinal(x.Added.Name, y.Added.Name);
        });

        var usedRemoved = new HashSet<string>(StringComparer.Ordinal);
        var usedAdded = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<(ToolDescriptor, ToolDescriptor)>();

        foreach (var candidate in candidates)
        {
            if (!usedRemoved.Add(candidate.Removed.Name) || !usedAdded.Add(candidate.Added.Name))
            {
                continue;
            }

            matches.Add((candidate.Removed, candidate.Added));
        }

        return matches;
    }

    /// <summary>
    /// A 0-1 similarity built from whichever signals both tools actually carry.
    /// </summary>
    /// <remarks>
    /// The mean of the available components rather than a fixed weighting: a
    /// tool with no output schema should not be penalised for lacking one, and
    /// requiring every signal to be present would make the detector useless
    /// against the many real servers that skip descriptions or output schemas
    /// entirely. Two tools with no comparable signal at all score zero - a
    /// rename claim needs evidence, not the absence of a reason to doubt it.
    /// </remarks>
    private static double Score(ToolDescriptor removed, ToolDescriptor added)
    {
        var components = new List<double>();

        if (SchemaSimilarity(removed.InputSchema, added.InputSchema) is { } input)
        {
            components.Add(input);
        }

        if (SchemaSimilarity(removed.OutputSchema, added.OutputSchema) is { } output)
        {
            components.Add(output);
        }

        if (TextSimilarity(removed.Description, added.Description) is { } description)
        {
            components.Add(description);
        }

        return components.Count == 0 ? 0.0 : components.Average();
    }

    /// <summary>Jaccard similarity over normalised property paths.</summary>
    private static double? SchemaSimilarity(JsonObject? before, JsonObject? after)
    {
        if (before is null || after is null)
        {
            return null;
        }

        var beforePaths = PropertyPaths(SchemaNormaliser.Normalise(before).Schema);
        var afterPaths = PropertyPaths(SchemaNormaliser.Normalise(after).Schema);

        return Jaccard(beforePaths, afterPaths);
    }

    private static HashSet<string> PropertyPaths(JsonObject? schema, string prefix = "")
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        if (schema?["properties"] is not JsonObject properties)
        {
            return paths;
        }

        foreach (var (name, value) in properties)
        {
            var path = prefix.Length == 0 ? name : $"{prefix}/{name}";
            paths.Add(path);
            paths.UnionWith(PropertyPaths(value as JsonObject, path));
        }

        return paths;
    }

    /// <summary>Jaccard similarity over lowercased word tokens.</summary>
    private static double? TextSimilarity(string? before, string? after)
    {
        if (before is null || after is null)
        {
            return null;
        }

        return Jaccard(Tokenise(before), Tokenise(after));
    }

    private static HashSet<string> Tokenise(string text)
        => [.. TextNormaliser.ForComparison(text)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 1.0;
        }

        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;

        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
