using FluidScript.Core.Catalogs;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Layout;
using FluidScript.Core.Model;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Model;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// The layout ladder (<c>29-layout-ladder</c>): one script per step under <c>Layout/Ladder</c>, each one component
/// further than the last, drawn to <c>diagnostics/layout-ladder</c> as SVG and as <c>28</c> §31 text for the user to correct.
/// A step asserts only what its rules have established so far; the picture is the judge.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LayoutLadderTests
{
    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    public static TheoryData<string> Steps
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Directory.GetFiles(Ladder, "step-*.fluid").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileNameWithoutExtension(file));
            }

            return data;
        }
    }

    private static Scene Solve(string step, out ModelContractInput input)
    {
        input = ContractFixture.Compile(File.ReadAllText(Path.Combine(Ladder, step + ".fluid")));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model));
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void EveryStepIsDrawnAndAuditClean(string step)
    {
        var scene = Solve(step, out var input);

        // The diagnostic is written before anything is asserted: it is what a failure is read from (28 §31).
        var directory = Path.Combine(RepositoryLayout.Diagnostics, "layout-ladder");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, step + ".svg"), SceneSvg.Render(scene));
        File.WriteAllText(Path.Combine(directory, step + ".txt"), SceneText.Render(scene, input.Graph, input.Model));

        foreach (var component in input.Graph.Components)
        {
            Assert.Contains(scene.Placements, p => p.ComponentId == component.Name);
        }

        var hard = SceneAudit.Findings(scene, input.Model).Where(static f => f.Hard).ToList();
        Assert.True(hard.Count == 0, step + ":\n" + string.Join("\n", hard));
    }

    /// <summary>
    /// A ladder script is a circuit before it is a picture: it must bind, size and converge (the experiment protocol in
    /// <c>CLAUDE.md</c>), and its solve report is written beside its picture so a step's physics can be read, not assumed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Steps))]
    public async Task EveryStepSolvesAndSettles(string step)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog));

        var source = File.ReadAllText(Path.Combine(Ladder, step + ".fluid"));

        if (source.StartsWith("# fragment", StringComparison.Ordinal) || source.StartsWith("# unsolved on purpose", StringComparison.Ordinal))
        {
            // A component alone is not a circuit; steps 1 and 2 say so on their first line and are judged by their picture only.
            // A step that is unsolved on purpose (3b, C-100) draws what the engine makes of a script under editing, and is judged the same way.
            return;
        }

        var result = await loop.RunAsync(GraphFixture.Bind(source), Water.Instance, step, TestContext.Current.CancellationToken);

        if (source.StartsWith("# does not bind: C-", StringComparison.Ordinal))
        {
            // A script that names an open binding defect on its first line is expected to be refused; when it binds, the defect is closed and the marker must go.
            Assert.False(result.IsSuccess, step + " now binds; close the defect its first line names and drop the marker");
            return;
        }

        Assert.True(result.IsSuccess, step + ": " + result.Error?.Message);

        var directory = Path.Combine(RepositoryLayout.Diagnostics, "layout-ladder");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, step + ".solve.txt"), SolveExplanation.Render(result.Value, step));

        if (source.StartsWith("# does not settle: S-", StringComparison.Ordinal))
        {
            // A script that names an open solver defect on its first line is expected to stall; when it settles, the defect is closed and the marker must go.
            Assert.False(result.Value.Settled, step + " settled: the defect its first line names is closed, remove the marker");
            return;
        }

        Assert.True(result.Value.Settled, step + " did not settle in " + result.Value.Passes + " passes; read " + step + ".solve.txt");
    }
}
