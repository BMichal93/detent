using System.Text;
using System.Text.Json.Nodes;
using Detent.Core.Capture;

namespace Detent.Core.Tests;

public sealed class SnapshotWriterTests
{
    private static Snapshot Sample(params ToolDescriptor[] tools) => new()
    {
        SchemaVersion = Snapshot.CurrentSchemaVersion,
        Server = new ServerIdentity
        {
            Name = "example-mcp",
            Version = "2.3.1",
            ProtocolRevision = "2026-07-28",
        },
        Capabilities = JsonNode.Parse("""{"tools":{"listChanged":true}}""")!.AsObject(),
        Tools = tools,
        Resources = [],
        Prompts = [],
    };

    private static ToolDescriptor Tool(string name, string? description = null) => new()
    {
        Name = name,
        Description = description,
        InputSchema = JsonNode.Parse("""{"type":"object"}""")!.AsObject(),
    };

    /// <summary>The Phase 1 exit criterion, as a permanent test.</summary>
    [Fact]
    public void Ten_consecutive_writes_are_byte_identical()
    {
        var snapshot = Sample(Tool("search", "Search the catalogue."));

        var first = SnapshotWriter.Write(snapshot);

        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(first, SnapshotWriter.Write(snapshot));
        }
    }

    [Fact]
    public void Tools_are_sorted_by_name()
    {
        var snapshot = Sample(Tool("zebra"), Tool("alpha"), Tool("mango"));

        var canonical = SnapshotWriter.Canonicalise(snapshot);

        Assert.Equal(["alpha", "mango", "zebra"], canonical.Tools.Select(t => t.Name));
    }

    /// <summary>
    /// A server that paginates or reorders its tool list must not produce a diff.
    /// </summary>
    [Fact]
    public void Tool_order_does_not_affect_output()
    {
        var forwards = Sample(Tool("a", "one"), Tool("b", "two"));
        var backwards = Sample(Tool("b", "two"), Tool("a", "one"));

        Assert.Equal(SnapshotWriter.Write(forwards), SnapshotWriter.Write(backwards));
    }

    [Fact]
    public void Canonicalise_is_idempotent()
    {
        var snapshot = Sample(Tool("b", "  Messy   text\n\n"), Tool("a"));

        var once = SnapshotWriter.Canonicalise(snapshot);
        var twice = SnapshotWriter.Canonicalise(once);

        Assert.Equal(SnapshotWriter.Write(once), SnapshotWriter.Write(twice));
    }

    [Fact]
    public void Digest_is_present_and_prefixed()
    {
        var json = Encoding.UTF8.GetString(SnapshotWriter.Write(Sample(Tool("a"))));
        var parsed = JsonNode.Parse(json)!.AsObject();

        var digest = parsed["digest"]!.GetValue<string>();

        Assert.StartsWith("sha256:", digest, StringComparison.Ordinal);
        Assert.Equal(7 + 64, digest.Length);
    }

    [Fact]
    public void Digest_verifies_against_its_own_content()
    {
        var canonical = SnapshotWriter.Canonicalise(Sample(Tool("a", "text")));
        var withDigest = canonical with { Digest = SnapshotWriter.ComputeDigest(canonical) };

        Assert.True(SnapshotWriter.VerifyDigest(withDigest));
    }

    [Fact]
    public void Digest_changes_when_content_changes()
    {
        var before = SnapshotWriter.Canonicalise(Sample(Tool("a", "original")));
        var after = SnapshotWriter.Canonicalise(Sample(Tool("a", "mutated")));

        Assert.NotEqual(SnapshotWriter.ComputeDigest(before), SnapshotWriter.ComputeDigest(after));
    }

    [Fact]
    public void Tampering_with_content_invalidates_the_digest()
    {
        var canonical = SnapshotWriter.Canonicalise(Sample(Tool("a", "original")));
        var signed = canonical with { Digest = SnapshotWriter.ComputeDigest(canonical) };

        var tampered = signed with { Tools = [Tool("a", "mutated")] };

        Assert.False(SnapshotWriter.VerifyDigest(SnapshotWriter.Canonicalise(tampered) with
        {
            Digest = signed.Digest,
        }));
    }

    /// <summary>
    /// The point of storing text and hash separately: the reviewer sees the
    /// rewrap, the engine does not call it a behaviour change.
    /// </summary>
    [Fact]
    public void Reflowing_a_description_changes_stored_text_but_not_its_hash()
    {
        var flat = SnapshotWriter.Canonicalise(Sample(Tool("a", "One two three.")));
        var wrapped = SnapshotWriter.Canonicalise(Sample(Tool("a", "One  two\n  three.")));

        Assert.NotEqual(flat.Tools[0].Description, wrapped.Tools[0].Description);
        Assert.Equal(flat.Tools[0].DescriptionSha256, wrapped.Tools[0].DescriptionSha256);
    }

    [Fact]
    public void Description_hash_changes_when_wording_changes()
    {
        var before = SnapshotWriter.Canonicalise(Sample(Tool("a", "Read the catalogue.")));
        var after = SnapshotWriter.Canonicalise(Sample(Tool("a", "Delete the catalogue.")));

        Assert.NotEqual(before.Tools[0].DescriptionSha256, after.Tools[0].DescriptionSha256);
    }

    /// <summary>
    /// An absent annotation is a different claim from a false one. Conflating
    /// them would silently defeat MCPC310.
    /// </summary>
    [Fact]
    public void Absent_annotations_are_not_written_as_false()
    {
        var snapshot = Sample(Tool("a") with
        {
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        });

        var json = Encoding.UTF8.GetString(SnapshotWriter.Write(snapshot));

        Assert.Contains("\"readOnlyHint\": true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("destructiveHint", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Storage form is NFC composition only: unlike a description's comparison
    /// hash, nothing hashes instructions, so line structure must survive
    /// untouched or the committed file would misrepresent what the server sent.
    /// </summary>
    [Fact]
    public void Instructions_keep_their_line_structure_in_storage_form()
    {
        const string withStructure = "Search first.\n\n- Then confirm the market.\n- Then purchase.";

        var snapshot = SnapshotWriter.Canonicalise(Sample(Tool("a")) with { Instructions = withStructure });

        Assert.Equal(withStructure, snapshot.Instructions);
    }

    [Fact]
    public void Absent_instructions_stay_absent()
    {
        var json = Encoding.UTF8.GetString(SnapshotWriter.Write(Sample(Tool("a"))));

        Assert.DoesNotContain("instructions", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_contains_no_capture_timestamp()
    {
        var json = Encoding.UTF8.GetString(SnapshotWriter.Write(Sample(Tool("a"))));

        Assert.DoesNotContain("capturedAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timestamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Output_is_lf_terminated_canonical_json()
    {
        var bytes = SnapshotWriter.Write(Sample(Tool("a")));
        var json = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
    }
}
