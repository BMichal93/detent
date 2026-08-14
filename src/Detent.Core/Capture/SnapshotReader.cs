using System.Text.Json;

namespace Detent.Core.Capture;

/// <summary>
/// Reads a snapshot back from its canonical bytes.
/// </summary>
/// <remarks>
/// A snapshot is a file in a repository, which means it can arrive from a pull
/// request written by someone you do not trust. It is parsed under the same
/// suspicion as a server response rather than as something we wrote ourselves.
/// </remarks>
public static class SnapshotReader
{
    /// <summary>
    /// Deepest nesting accepted when reading a snapshot.
    /// </summary>
    /// <remarks>
    /// The same value the transport applies to a wire response, and for the same
    /// reason: a document deep enough to matter is deep enough to overflow the
    /// stack of whatever walks it, so it must never be materialised.
    /// </remarks>
    public const int MaxJsonDepth = 64;

    private static readonly JsonSerializerOptions _options = new()
    {
        MaxDepth = MaxJsonDepth,
        TypeInfoResolver = SnapshotJsonContext.Default,
    };

    /// <summary>Parses canonical snapshot bytes.</summary>
    /// <exception cref="SnapshotFormatException">
    /// The bytes are not a snapshot this build can read.
    /// </exception>
    public static Snapshot Read(ReadOnlySpan<byte> utf8)
    {
        Snapshot? snapshot;

        try
        {
            snapshot = JsonSerializer.Deserialize(utf8, SnapshotJsonContext.Default.Snapshot);
        }
        catch (JsonException ex)
        {
            throw new SnapshotFormatException($"Not valid snapshot JSON: {ex.Message}", ex);
        }

        if (snapshot is null)
        {
            throw new SnapshotFormatException("The snapshot is empty.");
        }

        // Named explicitly rather than parsed partially. A reader that limps on
        // through a format it does not understand produces a diff nobody can
        // trust, which is worse than refusing.
        if (snapshot.SchemaVersion != Snapshot.CurrentSchemaVersion)
        {
            throw new SnapshotFormatException(
                $"Snapshot schemaVersion is {snapshot.SchemaVersion}, and this build reads "
                + $"{Snapshot.CurrentSchemaVersion}. Upgrade detent, or re-capture the snapshot.");
        }

        return snapshot;
    }

    /// <summary>Parses, and verifies the digest matches the content.</summary>
    /// <remarks>
    /// Separate from <see cref="Read"/> because a hand-edited snapshot is a
    /// legitimate thing to diff, while a snapshot whose digest was supposed to
    /// hold and does not is a different situation entirely.
    /// </remarks>
    public static Snapshot ReadVerified(ReadOnlySpan<byte> utf8)
    {
        var snapshot = Read(utf8);

        if (!SnapshotWriter.VerifyDigest(snapshot))
        {
            throw new SnapshotFormatException(
                "The snapshot's digest does not match its content. It has been edited by hand "
                + "or truncated in transit.");
        }

        return snapshot;
    }
}

/// <summary>A snapshot that cannot be read as one.</summary>
public sealed class SnapshotFormatException : Exception
{
    public SnapshotFormatException()
    {
    }

    public SnapshotFormatException(string message)
        : base(message)
    {
    }

    public SnapshotFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
