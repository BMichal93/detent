namespace Detent.Core.Contracts;

/// <summary>The generic tree <see cref="YamlParser"/> produces, before typed mapping.</summary>
internal abstract class YamlNode;

internal sealed class YamlScalar(string value) : YamlNode
{
    public string Value { get; } = value;
}

/// <summary>
/// Preserves insertion order, though nothing currently depends on it - a list
/// rather than a dictionary because a contract file is small and readable
/// order costs nothing.
/// </summary>
internal sealed class YamlMap : YamlNode
{
    public List<(string Key, YamlNode Value)> Entries { get; } = [];

    public YamlNode? this[string key]
        => Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal)).Value;
}

internal sealed class YamlList : YamlNode
{
    public List<YamlNode> Items { get; } = [];
}
