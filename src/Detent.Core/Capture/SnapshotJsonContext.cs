using System.Text.Json.Serialization;

namespace Detent.Core.Capture;

/// <summary>
/// Source-generated serialisation metadata for <see cref="Snapshot"/>.
/// </summary>
/// <remarks>
/// Source generation rather than reflection because the product ships as
/// NativeAOT. A reflection-based serialiser would survive the build and fail in
/// the published binary.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext;
