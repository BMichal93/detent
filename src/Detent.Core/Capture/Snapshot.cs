using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Detent.Core.Capture;

/// <summary>
/// The agent-facing surface of an MCP server at a point in time.
/// </summary>
/// <remarks>
/// Committed to the consuming repository and reviewed in pull requests, so it
/// carries no capture timestamp and no field that varies between runs. See
/// <c>docs/arch/snapshot-format.md</c>, which is normative for this type.
/// </remarks>
public sealed record Snapshot
{
    /// <summary>The format version. Bumped on any incompatible change.</summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("server")]
    public required ServerIdentity Server { get; init; }

    /// <summary>
    /// Free-text guidance the server sent for how an agent should use it.
    /// </summary>
    /// <remarks>
    /// A sibling of <c>capabilities</c> in the <c>initialize</c> result, not
    /// part of <see cref="ServerIdentity"/>: MCP does not nest it under
    /// <c>serverInfo</c>, and neither does this model. Server-derived like every
    /// other description field, so it is stored verbatim and sanitized only at
    /// the point of rendering; see <c>docs/arch/security-model.md</c>.
    /// </remarks>
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; init; }

    /// <summary>Capabilities as advertised, stored verbatim.</summary>
    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Capabilities { get; init; }

    [JsonPropertyName("tools")]
    public required IReadOnlyList<ToolDescriptor> Tools { get; init; }

    [JsonPropertyName("resources")]
    public required IReadOnlyList<ResourceDescriptor> Resources { get; init; }

    [JsonPropertyName("prompts")]
    public required IReadOnlyList<PromptDescriptor> Prompts { get; init; }

    /// <summary>
    /// <c>sha256:...</c> over the canonical form of this document with the
    /// digest field itself absent.
    /// </summary>
    [JsonPropertyName("digest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Digest { get; init; }

    /// <summary>The only schema version this build understands.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Identity of the captured server.</summary>
public sealed record ServerIdentity
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("protocolRevision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProtocolRevision { get; init; }
}

/// <summary>A single tool as advertised by the server.</summary>
public sealed record ToolDescriptor
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The description as the server supplied it, NFC-normalised but otherwise
    /// intact.
    /// </summary>
    /// <remarks>
    /// Stored in full rather than as a hash alone, because a description change
    /// is the rug-pull signal and a reviewer has to be able to read what changed.
    /// </remarks>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>
    /// SHA-256 over <see cref="TextNormaliser.ForComparison"/> of the
    /// description.
    /// </summary>
    /// <remarks>
    /// The engine compares this rather than the stored text, which is what makes
    /// a re-wrapped paragraph a non-event while still showing in the diff.
    /// </remarks>
    [JsonPropertyName("descriptionSha256")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DescriptionSha256 { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    /// <summary>
    /// The input schema. An absent schema and an empty one are different claims
    /// and must not be conflated.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? InputSchema { get; init; }

    [JsonPropertyName("outputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OutputSchema { get; init; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolAnnotations? Annotations { get; init; }
}

/// <summary>
/// The safety hints a server asserts about a tool.
/// </summary>
/// <remarks>
/// Every member is nullable and absent when null, deliberately. An absent
/// <c>readOnlyHint</c> is not the same claim as <c>readOnlyHint: false</c>, and
/// dropping an assertion is classified as a security finding (MCPC310). A
/// non-nullable bool defaulting to false would erase that distinction.
/// </remarks>
public sealed record ToolAnnotations
{
    [JsonPropertyName("readOnlyHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadOnlyHint { get; init; }

    [JsonPropertyName("destructiveHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DestructiveHint { get; init; }

    [JsonPropertyName("idempotentHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IdempotentHint { get; init; }

    [JsonPropertyName("openWorldHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OpenWorldHint { get; init; }
}

/// <summary>A resource the server exposes.</summary>
public sealed record ResourceDescriptor
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

/// <summary>A prompt the server exposes.</summary>
public sealed record PromptDescriptor
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>Arguments as advertised, stored verbatim.</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Arguments { get; init; }
}
