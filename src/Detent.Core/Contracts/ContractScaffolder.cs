using Detent.Core.Capture;
using Detent.Core.Diff;

namespace Detent.Core.Contracts;

/// <summary>
/// Builds a starter <see cref="Contract"/> from an observed snapshot, for
/// <c>detent init</c>.
/// </summary>
/// <remarks>
/// Deliberately the most permissive contract that still says something: every
/// tool the server advertises, every top-level input property in
/// <c>sends</c>, every top-level output property in <c>reads</c>. A scaffold
/// that under-declares would drop findings on properties the consumer
/// actually depends on without anyone noticing; one that over-declares only
/// costs a few unnecessary findings until a human narrows it, which is what
/// the generated file's comments ask them to do. <c>exhaustiveEnums</c> and
/// <c>assumes</c> are never guessed - both require knowledge of what the
/// consumer's own code does, which nothing about the server can tell you.
/// </remarks>
public static class ContractScaffolder
{
    /// <summary>
    /// Scaffolds a contract naming every tool in <paramref name="snapshot"/>.
    /// </summary>
    /// <param name="snapshot">The observed server surface.</param>
    /// <param name="consumer">Who this contract is for. Not inferrable from a snapshot.</param>
    /// <param name="providerUrl">
    /// Where the snapshot was captured from, recorded so the generated file
    /// documents its own origin. <see langword="null"/> if scaffolding from a
    /// snapshot file rather than a live capture.
    /// </param>
    public static Contract FromSnapshot(Snapshot snapshot, string consumer, string? providerUrl)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(consumer);

        return new Contract
        {
            ApiVersion = ContractYamlReader.SupportedApiVersion,
            Consumer = consumer,
            Provider = providerUrl is null ? null : new ContractProvider { Transport = "http", Url = providerUrl },
            Tools = [.. snapshot.Tools
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(ToRequirement)],
        };
    }

    private static ToolRequirement ToRequirement(ToolDescriptor tool) => new()
    {
        Name = tool.Name,
        Sends = TopLevelProperties(tool.InputSchema),
        Reads = TopLevelProperties(tool.OutputSchema),
    };

    /// <summary>
    /// Normalises first so a schema built from <c>$ref</c>/<c>$defs</c> still
    /// yields its real property names, the same as every diff rule sees them.
    /// A schema too deep or too broken to normalise simply scaffolds no
    /// properties for that tool rather than failing the whole command - an
    /// incomplete scaffold costs a human a few minutes; failing init over one
    /// bad tool costs them the whole file.
    /// </summary>
    private static HashSet<string> TopLevelProperties(System.Text.Json.Nodes.JsonObject? schema)
    {
        var normalised = SchemaNormaliser.Normalise(schema).Schema;

        return normalised?["properties"] is System.Text.Json.Nodes.JsonObject properties
            ? new HashSet<string>(properties.Select(p => p.Key), StringComparer.Ordinal)
            : [];
    }
}
