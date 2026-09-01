using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace FluidScript.Fixtures;

/// <summary>
/// Locates the repository root and the directories the plan gives a fixed meaning to, so that tests
/// asserting a repository-wide rule address real paths rather than paths relative to a test host's
/// working directory.
/// </summary>
/// <remarks>
/// The root is found by walking upwards for <c>FluidScript.slnx</c>, starting from this file's own
/// compile-time path and falling back to the executing assembly's location. Neither a relative path
/// nor the current directory would do: the test host's working directory differs between
/// <c>dotnet test</c>, an IDE runner and CI, and the output directory's depth below the root is not
/// fixed either. See <c>plan/00-foundation/03-repository-layout.md</c>.
/// </remarks>
public static class RepositoryLayout
{
    private const string SolutionFileName = "FluidScript.slnx";

    /// <summary>Gets the absolute path of the repository root.</summary>
    /// <value>The directory containing <c>FluidScript.slnx</c>. Never <see langword="null"/>.</value>
    /// <exception cref="DirectoryNotFoundException">
    /// Neither this file's compile-time path nor the executing assembly's location has an ancestor
    /// containing the solution file, which means the tests are running outside a checkout.
    /// </exception>
    public static string Root { get; } = FindRoot();

    /// <summary>Gets the absolute path of the backend source directory.</summary>
    /// <value>The <c>src</c> directory. The path is not checked for existence.</value>
    public static string Source => Path.Combine(Root, "src");

    /// <summary>Gets the absolute path of the backend test directory.</summary>
    /// <value>The <c>tests</c> directory. The path is not checked for existence.</value>
    public static string Tests => Path.Combine(Root, "tests");

    /// <summary>Gets the absolute path of the shared sample-script directory.</summary>
    /// <value>The <c>samples</c> directory holding the demo scripts every milestone is checked against.</value>
    public static string Samples => Path.Combine(Root, "samples");

    /// <summary>Enumerates every C# source file tracked in the repository.</summary>
    /// <returns>
    /// Absolute paths, excluding build output, so a rule about "every source file" is not defeated by
    /// generated code under <c>bin</c> or <c>obj</c>. Ordered by path so a failure names the same file
    /// on every platform.
    /// </returns>
    public static IEnumerable<string> EnumerateSourceFiles() => LazySourceFiles.Value;

    /// <summary>Enumerates every MSBuild project file in the repository.</summary>
    /// <returns>Absolute paths to <c>.csproj</c> files, excluding build output, ordered by path.</returns>
    public static IEnumerable<string> EnumerateProjectFiles() => LazyProjectFiles.Value;

    private static readonly Lazy<ImmutableArray<string>> LazySourceFiles = new(() => Enumerate("*.cs"));

    private static readonly Lazy<ImmutableArray<string>> LazyProjectFiles = new(() => Enumerate("*.csproj"));

    // Pruned during the walk rather than filtered after it, and walked once rather than once per
    // caller. `Directory.EnumerateFiles(Root, pattern, AllDirectories)` descends into
    // frontend/node_modules and every bin and obj before anything can reject them, which costs about
    // half a second per call on a checkout mounted from Windows -- paid five times by the architecture
    // tests alone, and 08's invariant 3 gives the whole unit tier two seconds.
    private static ImmutableArray<string> Enumerate(string pattern)
    {
        var found = ImmutableArray.CreateBuilder<string>();
        var pending = new Stack<string>();
        pending.Push(Root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(directory, pattern));

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!IsExcluded(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }

        found.Sort(StringComparer.Ordinal);

        return found.ToImmutable();
    }

    private static bool IsExcluded(string directoryName) =>
        directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || directoryName.Equals("node_modules", StringComparison.Ordinal)
        || directoryName.Equals(".git", StringComparison.Ordinal);

    /// <summary>Renders a path for a failure message.</summary>
    /// <param name="absolutePath">A path at or below <see cref="Root"/>.</param>
    /// <returns>
    /// The path relative to the repository root using forward slashes, so an assertion message reads
    /// the same on Windows and Linux and can be pasted into a search.
    /// </returns>
    public static string ToRelative(string absolutePath) =>
        Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');



    private static string FindRoot([CallerFilePath] string callerFilePath = "")
    {
        // Two starting points, in order. The compile-time path of this very file comes first because it
        // is independent of where the build put its output: walking up from AppContext.BaseDirectory
        // alone breaks under ArtifactsPath, a private output directory, or a shadow-copying test host,
        // and it breaks by throwing from a static initializer -- which surfaces as every
        // repository-wide test failing at once, with no message naming the real cause.
        //
        // BaseDirectory stays as the fallback for the case the source path cannot serve: sources that
        // are no longer where they were compiled, such as a published or deterministic-path build.
        foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start))
            {
                continue;
            }

            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"Neither '{callerFilePath}' nor '{AppContext.BaseDirectory}' has an ancestor "
            + $"containing '{SolutionFileName}'.");
    }
}
