namespace Detent.Core.Contracts;

/// <summary>
/// A parser for the subset of YAML a <c>detent</c> contract file actually
/// needs: block mappings, block sequences (including sequences of mappings),
/// inline <c>[a, b, c]</c> flow sequences, quoted and unquoted scalars, and
/// <c>#</c> comments.
/// </summary>
/// <remarks>
/// Not a general YAML engine, deliberately. The only AOT-safe path for
/// YamlDotNet - its Roslyn source generator - is compiled against
/// <c>Microsoft.CodeAnalysis 4.4.0</c> and does not load under the current
/// SDK's Roslyn version; it generates nothing and fails silently rather than
/// erroring, discovered by comparing against a working generator
/// (<c>System.Text.Json</c>'s) in the same project rather than assumed. See
/// ADR-0009. Anchors, aliases, multi-document streams, tags, and block scalars
/// (<c>|</c>, <c>&gt;</c>) are all out of scope: nothing in a contract file
/// needs them, and reaching for a general-purpose grammar to parse a bounded,
/// self-authored config shape would be solving a harder problem than the one
/// this project actually has.
/// </remarks>
internal static class YamlParser
{
    /// <exception cref="ContractFormatException">The text is not valid within this subset.</exception>
    public static YamlNode? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = Preprocess(text);

        if (lines.Count == 0)
        {
            return null;
        }

        var index = 0;
        var node = ParseBlock(lines, ref index, parentIndent: -1);

        if (index < lines.Count)
        {
            throw new ContractFormatException(
                $"Unexpected indentation at line {lines[index].LineNumber}: "
                + $"'{lines[index].Content}' does not belong to any enclosing block.");
        }

        return node;
    }

    private static List<Line> Preprocess(string text)
    {
        var lines = new List<Line>();
        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r');

            if (line.Contains('\t', StringComparison.Ordinal))
            {
                throw new ContractFormatException($"Line {lineNumber}: tabs are not allowed for indentation.");
            }

            var indent = 0;
            while (indent < line.Length && line[indent] == ' ')
            {
                indent++;
            }

            var content = StripComment(line[indent..]).TrimEnd();

            if (content.Length == 0)
            {
                continue;
            }

            lines.Add(new Line(lineNumber, indent, content));
        }

        return lines;
    }

    /// <summary>
    /// A <c>#</c> starts a comment only outside a quoted string and only when
    /// it opens a token - preceded by the start of content or whitespace - so
    /// a literal <c>#</c> inside a value, or a URL fragment, is never mistaken
    /// for one.
    /// </summary>
    private static string StripComment(string content)
    {
        char? quote = null;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (quote is { } open)
            {
                if (c == open)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == '#' && (i == 0 || content[i - 1] == ' '))
            {
                return content[..i];
            }
        }

        return content;
    }

    private static YamlNode ParseBlock(List<Line> lines, ref int index, int parentIndent)
    {
        var blockIndent = lines[index].Indent;

        if (blockIndent <= parentIndent)
        {
            throw new ContractFormatException(
                $"Line {lines[index].LineNumber}: expected more indentation than the enclosing block.");
        }

        return lines[index].Content.StartsWith('-')
            && (lines[index].Content.Length == 1 || lines[index].Content[1] == ' ')
            ? ParseSequence(lines, ref index, blockIndent)
            : ParseMapping(lines, ref index, blockIndent);
    }

    private static YamlList ParseSequence(List<Line> lines, ref int index, int blockIndent)
    {
        var list = new YamlList();

        while (index < lines.Count && lines[index].Indent == blockIndent && IsSequenceItem(lines[index].Content))
        {
            var lineNumber = lines[index].LineNumber;
            var remainder = lines[index].Content.Length == 1 ? string.Empty : lines[index].Content[2..];

            if (remainder.Length == 0)
            {
                // "-" alone: the item is a nested block on the following,
                // more-indented lines.
                index++;

                if (index >= lines.Count || lines[index].Indent <= blockIndent)
                {
                    throw new ContractFormatException($"Line {lineNumber}: empty sequence item.");
                }

                list.Items.Add(ParseBlock(lines, ref index, blockIndent));
                continue;
            }

            if (TrySplitMappingLine(remainder, out _, out _))
            {
                // "- key: value": the item is a mapping. Its first entry is
                // this line, synthesised at the column right after "- ";
                // further entries are the following lines indented to match
                // that same column, which is how YAML aligns continuation
                // lines for a sequence of mappings.
                var itemIndent = blockIndent + 2;
                var itemLines = new List<Line> { new(lineNumber, itemIndent, remainder) };
                index++;

                while (index < lines.Count && lines[index].Indent >= itemIndent
                    && !(lines[index].Indent == blockIndent && IsSequenceItem(lines[index].Content)))
                {
                    itemLines.Add(lines[index]);
                    index++;
                }

                var itemIndex = 0;
                list.Items.Add(ParseBlock(itemLines, ref itemIndex, parentIndent: itemIndent - 1));
                continue;
            }

            list.Items.Add(ParseScalarOrFlow(remainder));
            index++;
        }

        return list;
    }

    private static YamlMap ParseMapping(List<Line> lines, ref int index, int blockIndent)
    {
        var map = new YamlMap();

        while (index < lines.Count && lines[index].Indent == blockIndent)
        {
            var lineNumber = lines[index].LineNumber;

            if (!TrySplitMappingLine(lines[index].Content, out var key, out var remainder))
            {
                throw new ContractFormatException($"Line {lineNumber}: expected 'key: value'.");
            }

            index++;

            if (remainder.Length == 0)
            {
                if (index >= lines.Count || lines[index].Indent <= blockIndent)
                {
                    // A key with no inline value and nothing indented under
                    // it. Treated as an empty mapping rather than an error:
                    // "assumes:" with nothing under it is a plausible typo to
                    // tolerate rather than a hard failure.
                    map.Entries.Add((key, new YamlMap()));
                    continue;
                }

                map.Entries.Add((key, ParseBlock(lines, ref index, blockIndent)));
            }
            else
            {
                map.Entries.Add((key, ParseScalarOrFlow(remainder)));
            }
        }

        return map;
    }

    private static bool IsSequenceItem(string content) => content.Length >= 1 && content[0] == '-'
        && (content.Length == 1 || content[1] == ' ');

    /// <summary>
    /// Finds the colon that separates a mapping key from its value: the first
    /// one outside a quoted string that is followed by a space or the end of
    /// the line. A colon inside <c>https://...</c> is followed by neither, so
    /// it is never mistaken for one.
    /// </summary>
    private static bool TrySplitMappingLine(string content, out string key, out string remainder)
    {
        key = string.Empty;
        remainder = string.Empty;

        char? quote = null;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (quote is { } open)
            {
                if (c == open)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == ':' && (i == content.Length - 1 || content[i + 1] == ' '))
            {
                key = Unquote(content[..i].Trim());
                remainder = content[(i + 1)..].Trim();
                return true;
            }
        }

        return false;
    }

    private static YamlNode ParseScalarOrFlow(string text)
    {
        var trimmed = text.Trim();

        return trimmed.StartsWith('[') && trimmed.EndsWith(']')
            ? ParseFlowSequence(trimmed[1..^1])
            : new YamlScalar(Unquote(trimmed));
    }

    private static YamlList ParseFlowSequence(string inner)
    {
        var list = new YamlList();

        if (inner.Trim().Length == 0)
        {
            return list;
        }

        foreach (var part in SplitTopLevel(inner, ','))
        {
            list.Items.Add(new YamlScalar(Unquote(part.Trim())));
        }

        return list;
    }

    /// <summary>
    /// Deliberately returns a materialised list rather than using
    /// <c>yield return</c>: an iterator method compiles to a state machine
    /// that captures <c>Environment.CurrentManagedThreadId</c> to decide
    /// whether its enumerator can be reused, and <c>Detent.Core</c> takes no
    /// dependency on <c>System.Environment</c> even when the compiler would
    /// add one on its own for something this innocuous.
    /// </summary>
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        char? quote = null;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quote is { } open)
            {
                if (c == open)
                {
                    quote = null;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == separator)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == text[^1] && text[0] is '"' or '\'')
        {
            return text[1..^1];
        }

        return text;
    }

    private readonly record struct Line(int LineNumber, int Indent, string Content);
}
