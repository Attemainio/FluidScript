using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Layout.Drawing;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>Draws every variant in <c>diagnostics/scratch/</c> as <c>28</c> §31 text and SVG.</summary>
/// <remarks>
/// <para>
/// The layout half of <see cref="CircuitDiagnostics"/>, and for the same reason: a layout question is
/// almost always <em>this script with one line changed</em> — a spacing, a second supply, a member
/// removed — and the ladder draws only its own steps while <c>LayoutSolverTests</c> draws only the
/// samples. Until this existed a variant's <c>SceneText</c> was read by editing a ladder step and
/// putting it back (<c>C-96</c> was filed from one such edit and could not be re-measured without
/// another). One scratch folder serves both harnesses, so a variant gets its solve report and its
/// picture from the same file.
/// </para>
/// <para>
/// Traited <c>Diagnostic</c>; writes <c>diagnostics/layout/scratch-&lt;name&gt;.txt</c> and <c>.svg</c>
/// and asserts nothing about the picture. It asserts only that it drew something when the folder holds
/// a script, so a harness pointed at an empty folder cannot read as a pass.
/// </para>
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class LayoutDiagnostics
{
    private static string Scratch => Path.Combine(RepositoryLayout.Diagnostics, "scratch");

    [Fact]
    public void EveryScratchScriptIsDrawnAndDescribed()
    {
        if (!Directory.Exists(Scratch))
        {
            return;
        }

        var scripts = Directory.EnumerateFiles(Scratch, "*.fluid").Order(StringComparer.Ordinal).ToList();
        var output = Path.Combine(RepositoryLayout.Diagnostics, "layout");

        Directory.CreateDirectory(output);

        foreach (var path in scripts)
        {
            var name = "scratch-" + Path.GetFileNameWithoutExtension(path);
            var input = ContractFixture.Compile(File.ReadAllText(path));
            var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
            var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model));

            File.WriteAllText(Path.Combine(output, name + ".svg"), SceneSvg.Render(scene));
            File.WriteAllText(Path.Combine(output, name + ".txt"), SceneText.Render(scene, input.Graph, input.Model));
        }

        Assert.True(
            scripts.Count == 0 || Directory.EnumerateFiles(output, "scratch-*.txt").Any(),
            "Drop a `.fluid` file in `diagnostics/scratch/` to have a variant drawn beside the samples.");
    }
}
