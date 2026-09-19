using System.Text;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>Every script in the corpus, explained end to end, written where it can be read.</summary>
/// <remarks>
/// <para>
/// <strong><see cref="SolveExplanation"/> already answered every question this harness asks, and
/// nothing ran it anywhere a person could read the answer.</strong> The report carries the counting
/// table, the hydraulic partition, the constraints and what answers each, the unknowns seeded against
/// solved, the worst residuals and the sizing notes — and its only caller was a test asserting that the
/// sections exist. So diagnosing a circuit meant writing a throwaway test to print the part you wanted,
/// building, running it, reading a stack trace, and deleting it. Four of those were written in one
/// session before this file existed, each answering one question the report already contained.
/// </para>
/// <para>
/// <strong>Variants are the point, not the corpus.</strong> The corpus is asserted by
/// <c>CorpusStatusTests</c> and does not need explaining twice; what a diagnosis actually needs is *this
/// script with one line changed* — a stated outlet, an added datum, a different plant arrangement — and
/// nothing could do that without a code change. Anything dropped in <c>diagnostics/scratch/</c> is
/// picked up and reported beside the corpus, which is the whole workflow this replaces.
/// </para>
/// <para>
/// Traited <c>Diagnostic</c> so it stays out of the unit tier's two-second invariant, and it writes a
/// file rather than asserting: there is no pass criterion for "explain this circuit". The one thing it
/// does assert is that it found something to explain — a harness that silently reports on nothing reads
/// as a pass, which is the failure <c>62</c>'s own rule about checks that cannot fail is about.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class CircuitDiagnostics
{
    /// <summary>Where a variant under investigation is dropped, gitignored with the reports.</summary>
    private static string Scratch => Path.Combine(RepositoryLayout.Diagnostics, "scratch");

    [Fact]
    public async Task ExplainEveryCircuit()
    {
        var report = Path.Combine(RepositoryLayout.Diagnostics, "circuit-reports.md");
        var scripts = Scripts().ToArray();

        Assert.NotEmpty(scripts);

        var text = new StringBuilder()
            .AppendLine("# Circuit reports")
            .AppendLine()
            .AppendLine(
                "Written by `CircuitDiagnostics`. One `SolveExplanation` per script: the counting table,")
            .AppendLine(
                "the hydraulic partition, what answers each constraint, every unknown seeded against")
            .AppendLine("solved, the worst residuals, and what sizing chose.")
            .AppendLine()
            .AppendLine(
                "Drop a `.fluid` file in `diagnostics/scratch/` to have a variant explained beside the")
            .AppendLine("corpus. Neither this file nor that folder is committed.")
            .AppendLine();

        foreach (var (name, path) in scripts)
        {
            text.AppendLine("```").AppendLine(await Explain(path, name))
                .AppendLine("```").AppendLine();
        }

        Directory.CreateDirectory(RepositoryLayout.Diagnostics);
        File.WriteAllText(
            report, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>The corpus, then anything left in the scratch folder.</summary>
    /// <returns>A display name and a path for each script.</returns>
    private static IEnumerable<(string Name, string Path)> Scripts()
    {
        foreach (var path in Directory
            .EnumerateFiles(RepositoryLayout.Samples, "*.fluid")
            .Order(StringComparer.Ordinal))
        {
            yield return (Path.GetFileName(path), path);
        }

        if (!Directory.Exists(Scratch))
        {
            yield break;
        }

        foreach (var path in Directory
            .EnumerateFiles(Scratch, "*.fluid")
            .Order(StringComparer.Ordinal))
        {
            yield return ($"scratch/{Path.GetFileName(path)}", path);
        }
    }

    /// <summary>Renders one script's report, whether or not it reached the solver.</summary>
    /// <param name="path">The script's path.</param>
    /// <param name="name">The name to head the report with.</param>
    /// <returns>The report text.</returns>
    /// <remarks>
    /// A circuit refused before the solver is exactly the one whose report is worth reading, which is
    /// why the three-argument overload exists: it renders what it has rather than returning nothing.
    /// </remarks>
    private static async Task<string> Explain(string path, string name)
    {
        var source = File.ReadAllText(path);
        var resolved = PipeCatalogs.Resolve(pin: null);
        var run = await new OuterLoop(
                new NewtonSolver(),
                new CatalogBoreLookup(resolved.Value),
                OuterLoop.Rules(resolved.Value.Catalog),
                10)
            .RunAsync(
                GraphFixture.Bind(source), Water.Instance, name, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return SolveExplanation.Render(run, GraphFixture.Lower(source).Graph, name);
    }
}
