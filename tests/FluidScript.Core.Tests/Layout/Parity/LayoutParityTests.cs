using System.Text;

using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Layout.Drawing;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout.Parity;

/// <summary>
/// The parity harness of package P6.10 (<c>D-153</c>, <c>28</c> E5): the ladder engine and the composed engine on
/// every ladder step and sample, a report per case in <c>diagnostics/layout-parity/</c> and one summary line each.
/// </summary>
/// <remarks>
/// Until the switch it asserts only that both engines draw every case without failing, so the report is always
/// written; what the composed engine has not yet built reads as <c>missing</c> in the report, not as a failure. At the
/// switch the gate becomes the standard: composed hard 0 on every case, soft no worse than the ladder's.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LayoutParityTests
{
    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    private static readonly string[] Samples =
    [
        "m2-cooling-loop", "m2-simple-loop", "m2-substation", "m4-storage-header", "m2-distribution-header", "m1-syntax-tour", "m4-demand-step", "header-200",
    ];

    /// <summary>The scripts the ladder engine cannot draw and the composed engine must: the ladder's next steps, gated only here until the switch.</summary>
    private static string Pending => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Parity");

    /// <summary>Every case: the ladder steps in order, the pending scripts, then the samples.</summary>
    private static IEnumerable<(string Name, string Source)> Cases()
    {
        foreach (var file in Directory.GetFiles(Ladder, "step-*.fluid").Order(StringComparer.Ordinal))
        {
            yield return (Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
        }

        foreach (var file in Directory.GetFiles(Pending, "*.fluid").Order(StringComparer.Ordinal))
        {
            yield return ("pending-" + Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
        }

        foreach (var name in Samples)
        {
            yield return (name, name == "header-200" ? ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers) : ContractFixture.Sample(name + ".fluid"));
        }
    }

    [Fact]
    public void EveryCaseIsDrawnByBothEnginesAndCompared()
    {
        var directory = Path.Combine(RepositoryLayout.Diagnostics, "layout-parity");
        Directory.CreateDirectory(directory);
        var summary = new StringBuilder();
        var cases = 0;

        foreach (var (name, source) in Cases())
        {
            var input = ContractFixture.Compile(source);
            var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
            var margin = LayoutSolver.MarginOf(input.Model);
            var ladder = LayoutSolver.Solve(input.Graph, input.Model, hints, margin, LayoutEngineKind.Ladder);
            var composed = LayoutSolver.Solve(input.Graph, input.Model, hints, margin, LayoutEngineKind.Composed);
            var difference = new SceneDifference(name, ladder, composed, input.Model);

            File.WriteAllText(Path.Combine(directory, name + ".txt"), difference.Report());
            File.WriteAllText(Path.Combine(directory, name + ".svg"), SceneSvg.Render(composed));
            summary.AppendLine(difference.Line());
            cases++;
        }

        File.WriteAllText(Path.Combine(directory, "summary.txt"), summary.ToString());
        Assert.True(cases >= 30, $"the harness compared {cases} cases; the ladder's steps are missing");
    }
}
