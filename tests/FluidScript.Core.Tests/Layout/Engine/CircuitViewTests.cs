using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout.Engine;

/// <summary>
/// The composed engine's circuit view (<c>28</c> E1, <c>D-153</c>), read from its trace: the runs between boxed elements
/// with the inline elements collapsed onto them, oriented with the flow, and the fragments.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CircuitViewTests
{
    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    private static string Variants => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Variants");

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

    private static Scene Solve(string source)
    {
        var input = ContractFixture.Compile(source);
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model));
    }

    private static List<string> Runs(Scene scene) =>
        [.. scene.Provenance.Where(static n => n.Rule == "E1" && n.Subject.StartsWith("run ", StringComparison.Ordinal)).Select(static n => n.Reason)];

    [Fact]
    public void AZoneIsOneRunFromItsLoadThroughItsNodeToTheHeaderWithTheFlow()
    {
        // The three-zone plant (C-126): LD3 drains through the two-connection node NR3 into the return header's
        // junction NB2, so NR3 is a point on one run from LD3's outlet to NB2 -- never a boxed element of its own.
        var runs = Runs(Solve(File.ReadAllText(Path.Combine(Ladder, "step-12-zones.fluid"))));

        Assert.Equal(14, runs.Count);
        Assert.Contains("LD3.out > NR3 > NB2.1", runs);
        Assert.Contains("NA1.2 > NA2.0", runs);
        Assert.Contains("HE1.out > NS > NA1.0", runs);
    }

    [Fact]
    public void AChainOfInlineElementsIsOneRun()
    {
        // Step 5's primary: the declared pipe between two inferred nodes is three points on one run into in2.
        var runs = Runs(Solve(File.ReadAllText(Path.Combine(Ladder, "step-05-primary.fluid"))));

        Assert.Contains("PCV.out > PCV__HE1__in > PCV__HE1 > PCV__HE1__out > HE1.in2", runs);
    }

    [Fact]
    public void AMemberJoinedToItselfIsOneRunFromItsOutletToItsInlet()
    {
        // C20: PU1 - PU1 binds through one inferred node.
        var runs = Runs(Solve(File.ReadAllText(Path.Combine(Ladder, "step-03c-self-loop.fluid"))));

        Assert.Equal(["PU1.out > PU1__PU1 > PU1.in"], runs);
    }

    [Fact]
    public void ACycleOfInlineElementsAloneKeepsItsFirstElementBoxed()
    {
        // Two bare nodes joined twice: each has two connections, so both would be points on a line that ends
        // nowhere. The first keeps its box and the other is a point on the one run from it back to itself.
        var runs = Runs(Solve("fluidscript 1\n\ncircuit plant\n\nconnections\nN1 - N2 - N1\n"));
        var run = Assert.Single(runs).Split(" > ");

        Assert.Equal(3, run.Length);
        Assert.Equal(run[0].Split('.')[0], run[^1].Split('.')[0]);
    }

    [Fact]
    public void TwoCircuitsInOneScriptAreTwoFragmentsInScriptOrder()
    {
        var scene = Solve(File.ReadAllText(Path.Combine(Ladder, "step-11a-two-loops.fluid")));
        var fragments = scene.Provenance.Where(static n => n.Rule == "E1" && n.Subject.StartsWith("fragment ", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, fragments.Count);
        Assert.Contains("HS_H", fragments[0].Reason, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void EveryConnectedComponentIsOnExactlyOneRunOrEndsOne(string step)
    {
        // A5 as the view builds it: an inline element lies on exactly one run, and a boxed element ends at least one;
        // a component alone in its fragment has no run.
        var source = File.ReadAllText(Path.Combine(Ladder, step + ".fluid"));
        var input = ContractFixture.Compile(source);
        var scene = Solve(source);
        var runs = Runs(scene).Select(static r => r.Split(" > ")).ToList();
        var alone = scene.Provenance
            .Where(static n => n.Rule == "E1" && n.Subject.StartsWith("fragment ", StringComparison.Ordinal) && !n.Reason.Contains(',', StringComparison.Ordinal))
            .Select(static n => n.Reason["members ".Length..])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var component in input.Graph.Components.Where(c => !alone.Contains(c.Name)))
        {
            var inline = runs.Count(r => r.Skip(1).SkipLast(1).Contains(component.Name));
            var ends = runs.Count(r => r[0].StartsWith(component.Name + ".", StringComparison.Ordinal) || r[^1].StartsWith(component.Name + ".", StringComparison.Ordinal));
            Assert.True(inline == 1 ^ ends > 0, $"{step}: {component.Name} lies on {inline} run(s) and ends {ends}");
        }
    }
}
