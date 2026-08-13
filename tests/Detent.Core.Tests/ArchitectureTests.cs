using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Detent.Core.Policy;

namespace Detent.Core.Tests;

/// <summary>
/// Enforces the purity rule for <c>Detent.Core</c>: two snapshots in, findings out.
/// No network, no filesystem, no clock, no randomness.
/// </summary>
/// <remarks>
/// This is asserted against the compiled metadata rather than against source,
/// because the point is what the assembly can actually reach. A comment saying
/// "do not add I/O here" is not enforcement, and the constraint is what makes
/// the engine testable to the standard docs/arch/testing.md demands.
/// </remarks>
public sealed class ArchitectureTests
{
    /// <summary>Namespaces no part of the engine may reach into, prefix-matched.</summary>
    private static readonly string[] _forbiddenNamespaces =
    [
        "System.Net",
        "System.Diagnostics.Process",
        "System.Runtime.InteropServices",
    ];

    /// <summary>
    /// Filesystem entry points. <c>System.IO</c> is not banned wholesale because
    /// <c>MemoryStream</c> and friends are in-memory and legitimate; it is
    /// touching a disk that is forbidden.
    /// </summary>
    private static readonly string[] _forbiddenTypes =
    [
        "System.IO.File",
        "System.IO.FileInfo",
        "System.IO.FileStream",
        "System.IO.FileSystemInfo",
        "System.IO.Directory",
        "System.IO.DirectoryInfo",
        "System.IO.DriveInfo",
        "System.IO.Path",
        "System.Random",
        "System.Environment",
    ];

    /// <summary>
    /// Ambient non-determinism. A diff that reads the clock cannot have a golden
    /// test, because the same inputs stop producing the same findings.
    /// </summary>
    private static readonly (string Type, string Member)[] _forbiddenMembers =
    [
        ("System.DateTime", "get_Now"),
        ("System.DateTime", "get_UtcNow"),
        ("System.DateTime", "get_Today"),
        ("System.DateTimeOffset", "get_Now"),
        ("System.DateTimeOffset", "get_UtcNow"),
        ("System.Guid", "NewGuid"),
    ];

    [Fact]
    public void Core_does_not_reference_forbidden_namespaces()
    {
        var violations = TypeReferences()
            .Where(t => _forbiddenNamespaces.Any(
                ns => t.StartsWith(ns + ".", StringComparison.Ordinal) || t == ns))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Detent.Core must have no network or process access. Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Core_does_not_touch_the_filesystem_or_randomness()
    {
        var violations = TypeReferences()
            .Where(_forbiddenTypes.Contains)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Detent.Core must have no filesystem or randomness access. Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Core_does_not_read_the_clock()
    {
        using var reader = OpenCore(out var metadata);

        var violations = new List<string>();

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);

            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var parent = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            var type = QualifiedName(metadata, parent);
            var name = metadata.GetString(member.Name);

            if (_forbiddenMembers.Contains((type, name)))
            {
                violations.Add($"{type}.{name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Detent.Core must not read the clock or generate identifiers. Found: {string.Join(", ", violations.Distinct().Order(StringComparer.Ordinal))}");
    }

    /// <summary>
    /// Guards the dependency rule in CLAUDE.md at the one place it matters most.
    /// Adding a package to the engine should fail a test, not just review.
    /// </summary>
    [Fact]
    public void Core_has_no_package_dependencies()
    {
        using var reader = OpenCore(out var metadata);

        var violations = metadata.AssemblyReferences
            .Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                           && name != "System"
                           && name != "netstandard"
                           && name != "mscorlib")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Detent.Core must depend on the base class library only. Found: {string.Join(", ", violations)}");
    }

    private static ImmutableArray<string> TypeReferences()
    {
        using var reader = OpenCore(out var metadata);

        return metadata.TypeReferences
            .Select(h => QualifiedName(metadata, metadata.GetTypeReference(h)))
            .ToImmutableArray();
    }

    private static string QualifiedName(MetadataReader metadata, TypeReference type)
    {
        var ns = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static PEReader OpenCore(out MetadataReader metadata)
    {
        // Locating the assembly through a type it owns keeps this working
        // regardless of configuration or target framework directory layout.
        var location = typeof(Severity).Assembly.Location;
        Assert.True(location.Length > 0, "Detent.Core must be a file-backed assembly for this test to inspect.");

        var reader = new PEReader(System.IO.File.OpenRead(location));
        metadata = reader.GetMetadataReader();
        return reader;
    }
}
