using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Detent.Core.Capture;

/// <summary>
/// Turns a snapshot into the exact bytes committed to a repository.
/// </summary>
/// <remarks>
/// The Phase 1 exit criterion is that ten consecutive captures of an unchanged
/// server produce identical bytes. Everything here serves that.
/// </remarks>
public static class SnapshotWriter
{
    private const string DigestPrefix = "sha256:";

    /// <summary>
    /// Canonicalises, computes the digest, and serialises to canonical bytes.
    /// </summary>
    public static byte[] Write(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var canonical = Canonicalise(snapshot);
        return Serialise(canonical with { Digest = ComputeDigest(canonical) });
    }

    /// <summary>
    /// Applies every content normalisation the format requires, leaving the
    /// digest alone.
    /// </summary>
    /// <remarks>
    /// Idempotent, and a property test pins that. Canonicalising an already
    /// canonical snapshot must be a no-op, or a hand-edited file could skew a
    /// diff against a freshly captured one.
    /// </remarks>
    public static Snapshot Canonicalise(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Server = snapshot.Server with { Name = TextNormaliser.ForStorage(snapshot.Server.Name) },

            // Sorted by name so that a server reordering its tool list, or
            // paginating it differently, is not a diff.
            Tools = [.. snapshot.Tools.Select(CanonicaliseTool).OrderBy(t => t.Name, StringComparer.Ordinal)],
            Resources = [.. snapshot.Resources.Select(CanonicaliseResource).OrderBy(r => r.Uri, StringComparer.Ordinal)],
            Prompts = [.. snapshot.Prompts.Select(CanonicalisePrompt).OrderBy(p => p.Name, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// Computes the digest over the canonical form with the digest field absent.
    /// </summary>
    public static string ComputeDigest(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var bytes = Serialise(snapshot with { Digest = null });
        return DigestPrefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Verifies a snapshot's digest against its content.
    /// </summary>
    public static bool VerifyDigest(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.Digest is not null
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(snapshot.Digest),
                Encoding.UTF8.GetBytes(ComputeDigest(snapshot)));
    }

    private static byte[] Serialise(Snapshot snapshot)
        => CanonicalJson.Serialise(JsonSerializer.SerializeToNode(snapshot, SnapshotJsonContext.Default.Snapshot));

    private static ToolDescriptor CanonicaliseTool(ToolDescriptor tool) => tool with
    {
        Description = tool.Description is null ? null : TextNormaliser.ForStorage(tool.Description),
        DescriptionSha256 = tool.Description is null ? null : HashDescription(tool.Description),
        Title = tool.Title is null ? null : TextNormaliser.ForStorage(tool.Title),
    };

    private static ResourceDescriptor CanonicaliseResource(ResourceDescriptor resource) => resource with
    {
        Name = resource.Name is null ? null : TextNormaliser.ForStorage(resource.Name),
        Description = resource.Description is null ? null : TextNormaliser.ForStorage(resource.Description),
    };

    private static PromptDescriptor CanonicalisePrompt(PromptDescriptor prompt) => prompt with
    {
        Description = prompt.Description is null ? null : TextNormaliser.ForStorage(prompt.Description),
    };

    /// <summary>
    /// Hashes the comparison form, not the stored form, so that whitespace and
    /// Unicode spelling differences do not read as behaviour changes.
    /// </summary>
    private static string HashDescription(string description)
        => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(TextNormaliser.ForComparison(description))));
}
