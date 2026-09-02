using System.Text.RegularExpressions;

using FluidScript.Core;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Architecture;

/// <summary>
/// The repository-wide boundaries from <c>plan/00-foundation/03-repository-layout.md</c> and
/// <c>plan/60-docs-and-devex/63-ci-and-repo-hygiene.md</c>, asserted as ordinary tests so a breach
/// fails <c>dotnet test</c> with a readable message rather than a shell script's exit code.
/// </summary>
/// <remarks>
/// Several of these pass trivially today because the namespaces they constrain hold no code yet, and
/// that is the point: an architecture test added before a boundary can be broken is a guard rail,
/// while the same test added afterwards is a refactor. The remaining assertions in
/// <c>63-ci-and-repo-hygiene</c>'s table arrive with the code they constrain.
/// </remarks>
public sealed class ArchitectureTests
{
    /// <summary>
    /// Assembly-name fragments that would mean Core had reached the hosting, UI or transport layers.
    /// </summary>
    private static readonly string[] ForbiddenCoreReferences =
    [
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "System.Text.Json",
        "System.Net.Http",
        "Newtonsoft.Json",
    ];

    /// <summary>
    /// Namespaces no source file in Core may name, whatever its package closure happens to contain.
    /// </summary>
    private static readonly string[] ForbiddenCoreNamespaces =
    [
        "Newtonsoft.Json",
        "System.Text.Json",
        "System.Runtime.Serialization",
        "Microsoft.AspNetCore",
    ];

    [Fact]
    [Trait("Category", "Unit")]
    public void Core_ReferencesNoHostingUiOrSerializationAssembly()
    {
        var referenced = CoreAssembly.Reference
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        var breaches = referenced
            .Where(name => ForbiddenCoreReferences.Any(
                forbidden => name.StartsWith(forbidden, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            breaches.Length == 0,
            $"R-16: FluidScript.Core must not reach hosting, UI or transport. Found: {string.Join(", ", breaches)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Core_ContainsNoSourceFileThatReachesASerializer()
    {
        // D-47. SharpProp declares Newtonsoft.Json as a direct dependency, so it is in Core's package
        // closure and no packaging trick removes it: ExcludeAssets severs the compile surface while
        // SharpProp's own dependency edge puts the assembly back in the build output. What the rule
        // actually protects is that Core never shapes its model contract around a serializer, and that
        // is a property of Core's own code -- so Core's own code is what gets asserted.
        var offenders = RepositoryLayout.EnumerateSourceFiles()
            .Where(static path => RepositoryLayout.ToRelative(path)
                .StartsWith("src/FluidScript.Core/", StringComparison.Ordinal))
            .Where(static path =>
            {
                var text = File.ReadAllText(path);
                return ForbiddenCoreNamespaces.Any(ns => text.Contains(ns, StringComparison.Ordinal));
            })
            .Select(RepositoryLayout.ToRelative)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"D-47: no file in Core may reach a serializer. Found: {string.Join(", ", offenders)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Core_HasNoProjectReference()
    {
        var coreProject = Path.Combine(
            RepositoryLayout.Source, "FluidScript.Core", "FluidScript.Core.csproj");

        var content = File.ReadAllText(coreProject);

        Assert.DoesNotContain("<ProjectReference", content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SharpProp_IsReferencedFromAtMostOneFileUnderSrc()
    {
        // Scoped to src/ because the invariant in 21-fluid-and-state is about Core: exactly one class
        // depends on SharpProp, so that the blast radius of a package change is one file. The M0 spike
        // under tests/ deliberately reaches for it directly and is not what the rule constrains.
        var referencing = RepositoryLayout.EnumerateSourceFiles()
            .Where(static path => RepositoryLayout.ToRelative(path)
                .StartsWith("src/", StringComparison.Ordinal))
            .Where(static path => File.ReadAllText(path).Contains("SharpProp", StringComparison.Ordinal))
            .Select(RepositoryLayout.ToRelative)
            .ToArray();

        // "Exactly one" is the invariant; today that file does not exist yet, so the reachable half of
        // the rule is the one that catches a regression -- a second type reaching for the package
        // directly. P3.1 replaces this with the equality once the adapter is written.
        Assert.True(
            referencing.Length <= 1,
            $"Exactly one type in src/ may reference SharpProp. Found {referencing.Length}: {string.Join(", ", referencing)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoSourceFileLivesOutsideSrcOrTests()
    {
        var stray = RepositoryLayout.EnumerateSourceFiles()
            .Select(RepositoryLayout.ToRelative)
            .Where(static relative =>
                !relative.StartsWith("src/", StringComparison.Ordinal)
                && !relative.StartsWith("tests/", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            stray.Length == 0,
            $"Every .cs file lives under src/ or tests/. Found: {string.Join(", ", stray)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryPackageVersionIsManagedCentrally()
    {
        // NU1008 already fails the build on an inline version, but only for a package the build
        // resolves. Asserting the text keeps the rule legible in a failure message and catches a
        // commented-out or conditionally-included pin that never reaches restore.
        var inlineVersion = new Regex(
            """<PackageReference\b[^>]*\bVersion\s*=""", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        var offenders = RepositoryLayout.EnumerateProjectFiles()
            .Where(path => inlineVersion.IsMatch(File.ReadAllText(path)))
            .Select(RepositoryLayout.ToRelative)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Versions belong in Directory.Packages.props. Pinned inline by: {string.Join(", ", offenders)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoBinderStageReadsAComponentTag()
    {
        // `15`'s step 11 is a contract, not a convenience. A tag is derived from the finished
        // declaration set, so a stage that resolved anything by one would reintroduce exactly the
        // identity `D-34` removed — and would do it silently, because the tag is usually right.
        //
        // Asserted on the text rather than on a call graph: `Tag` is a property of a record the
        // binder constructs, so every read is a `.Tag` in one of these files, and a reference test
        // could not tell a read apart from the assignment that step 11 makes.
        var read = new Regex(
            @"\.Tag\b\s*(?![=,)]|\s*=[^=])", RegexOptions.None, TimeSpan.FromSeconds(1));

        var offenders = RepositoryLayout.EnumerateSourceFiles()
            .Where(static path => RepositoryLayout.ToRelative(path)
                .StartsWith("src/FluidScript.Core/Binding/", StringComparison.Ordinal))
            .Where(path => read.IsMatch(File.ReadAllText(path)))
            .Select(RepositoryLayout.ToRelative)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Nothing in binding may read ComponentSymbol.Tag; step 11 only writes it. "
            + $"Read by: {string.Join(", ", offenders)}");
    }
}
