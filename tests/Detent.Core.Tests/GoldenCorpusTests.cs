using System.Text.Json;
using Detent.Core.Capture;
using Detent.Core.Diff;
using Detent.Core.Policy;

namespace Detent.Core.Tests;

/// <summary>
/// Runs every case in <c>tests/golden/</c>.
/// </summary>
/// <remarks>
/// One directory per rule row in <c>docs/arch/diff-rules.md</c>, named
/// <c>&lt;finding-id&gt;-&lt;slug&gt;</c>. Adding a rule is adding a directory,
/// which keeps the work parallelisable and makes the directory count a legible
/// measure of progress.
/// <para>
/// <b>Never edit an expected.json to make a test pass.</b> Doing so converts a
/// correctness failure into a silent green build, which is the exact failure mode
/// that destroys this product. If an expectation looks wrong, stop and ask. The
/// only legitimate way one changes is a deliberate rule change, and then
/// diff-rules.md changes in the same commit. See CLAUDE.md.
/// </para>
/// </remarks>
public sealed class GoldenCorpusTests
{
    private static readonly JsonSerializerOptions _expectations = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();

        foreach (var directory in Directory.EnumerateDirectories(CorpusRoot).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    private static string CorpusRoot => Path.Combine(RepositoryRoot(), "tests", "golden");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_classifies_exactly_as_expected(string caseName)
    {
        var directory = Path.Combine(CorpusRoot, caseName);

        var before = ReadSnapshot(directory, "before.json");
        var after = ReadSnapshot(directory, "after.json");
        var expected = ReadExpectations(directory);

        var actual = DiffEngine.Diff(before, after)
            .Select(f => new ExpectedFinding(f.Id, f.Severity.ToString().ToLowerInvariant(), f.Path))
            .ToList();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Structure is asserted separately so a malformed case fails as a malformed
    /// case, rather than as a confusing classification mismatch.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_is_well_formed(string caseName)
    {
        var directory = Path.Combine(CorpusRoot, caseName);

        foreach (var required in new[] { "before.json", "after.json", "expected.json", "README.md" })
        {
            Assert.True(
                File.Exists(Path.Combine(directory, required)),
                $"Golden case '{caseName}' is missing {required}.");
        }

        // The directory name carries the rule it pins, so a case cannot drift
        // away from the rule it claims to be about.
        Assert.Matches("^mcpc[0-9]{3}-[a-z0-9-]+$", caseName);

        foreach (var finding in ReadExpectations(directory))
        {
            Assert.True(
                Enum.TryParse<Severity>(finding.Severity, ignoreCase: true, out _),
                $"Golden case '{caseName}' expects unknown severity '{finding.Severity}'.");
        }
    }

    /// <summary>
    /// diff(x, x) is empty, from diff-rules.md §11.
    /// </summary>
    /// <remarks>
    /// Every fixture in the corpus is a free instance of this property, and it
    /// catches a whole class of bug the per-case expectations cannot: a rule
    /// that fires on a schema compared against itself is wrong no matter what
    /// the expectations say.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Comparing_a_snapshot_with_itself_finds_nothing(string caseName)
    {
        var directory = Path.Combine(CorpusRoot, caseName);

        foreach (var file in new[] { "before.json", "after.json" })
        {
            var snapshot = ReadSnapshot(directory, file);
            Assert.Empty(DiffEngine.Diff(snapshot, snapshot));
        }
    }

    /// <summary>
    /// An empty corpus would make every theory above pass vacuously, which is
    /// indistinguishable from a corpus that works.
    /// </summary>
    [Fact]
    public void Corpus_is_not_empty()
    {
        Assert.True(Directory.Exists(CorpusRoot), $"No golden corpus at {CorpusRoot}.");
        Assert.NotEmpty(Directory.EnumerateDirectories(CorpusRoot));
    }

    /// <summary>
    /// Fixtures are hand-written, so they carry no digest and are read without
    /// verifying one. Their content is the point, not their integrity.
    /// </summary>
    private static Snapshot ReadSnapshot(string directory, string file)
        => SnapshotReader.Read(File.ReadAllBytes(Path.Combine(directory, file)));

    private static List<ExpectedFinding> ReadExpectations(string directory)
    {
        var json = File.ReadAllBytes(Path.Combine(directory, "expected.json"));
        var document = JsonSerializer.Deserialize<ExpectationFile>(json, _expectations);

        return document?.Findings ?? [];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Detent.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Cannot locate the repository root from the test assembly.");
    }

    private sealed record ExpectationFile
    {
        public List<ExpectedFinding> Findings { get; init; } = [];
    }

    /// <summary>
    /// What a golden case pins: the rule, the class, and the location. Message
    /// wording is deliberately excluded, because pinning prose would make every
    /// reworded message a failing test and teach people to edit expectations.
    /// </summary>
    private sealed record ExpectedFinding(string Id, string Severity, string Path);
}
