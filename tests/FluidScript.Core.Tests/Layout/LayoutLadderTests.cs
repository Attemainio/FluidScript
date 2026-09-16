using FluidScript.Core.Layout;
using FluidScript.Core.Model;
using FluidScript.Core.Tests.Model;
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
}
