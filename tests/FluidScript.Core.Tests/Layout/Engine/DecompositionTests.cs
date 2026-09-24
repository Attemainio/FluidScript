using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout.Engine;

/// <summary>
/// The composed engine's decomposition (<c>28</c> E2, <c>D-153</c>), read from its trace: each fragment's form, its
/// body as series, headers and loops, and what hangs off it -- before any geometry.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DecompositionTests
{
    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    private static string Variants => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Variants");

    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var file in Directory.GetFiles(Ladder, "step-*.fluid").Concat(Directory.GetFiles(Variants, "*.fluid")).Order(StringComparer.Ordinal))
            {
                data.Add(file);
            }

            foreach (var sample in new[] { "m1-syntax-tour", "m2-cooling-loop", "m2-distribution-header", "m2-simple-loop", "m2-substation", "m4-demand-step", "m4-storage-header" })
            {
                data.Add(sample);
            }

            return data;
        }
    }

    private static string Source(string name) => File.Exists(name) ? File.ReadAllText(name) : ContractFixture.Sample(name + ".fluid");

    private static Scene Solve(string source)
    {
        var input = ContractFixture.Compile(source);
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model));
    }

    /// <summary>The plan's lines for one fragment, their indentation kept.</summary>
    private static List<string> Plan(Scene scene, int fragment = 1) =>
        [.. scene.Provenance.Where(n => n.Rule == "E2" && n.Subject == $"fragment {fragment}").Select(static n => n.Reason)];

    [Fact]
    public void ThreeZonesAreAHeaderWhoseLastZoneIsTheSpineAndTheOthersHang()
    {
        // C-126: NA1 splits to zone 1 and on to NA2, which splits to zones 2 and 3; the return collects at NB2, then NB1.
        // Every branch flows from its split to its merge, so both groups are headers. The spine -- the branch the ring
        // runs on through -- is the one declared last: at NA1 the way on to NA2, at NA2 zone 3.
        var plan = Plan(Solve(File.ReadAllText(Path.Combine(Ladder, "step-12-zones.fluid"))));

        Assert.Equal("sourced ring (C2), head HE1, cut at HE1", plan[0]);
        Assert.Contains("    header NA1 to NB1, 2 branches", plan);
        Assert.Contains("          header NA2 to NB2, 2 branches", plan);
        var spines = plan.Select((line, i) => (line, i)).Where(static l => l.line.EndsWith("(spine)", StringComparison.Ordinal)).Select(l => plan[l.i + 2].Trim()).ToList();
        Assert.Equal(["run NA1.2 > NA2.0", "run NA2.2 > CV3.in"], spines);
    }

    [Fact]
    public void AMixingBlockIsALoopBecauseItsBypassFlowsBack()
    {
        // Step 7: the valve's common port feeds the pump and the load down to NM_AHU, and NM_AHU's bypass returns to
        // the valve's b -- the two paths between the valve and the junction flow opposite ways, so the fluid
        // circulates: a loop, C11's block.
        var plan = Plan(Solve(File.ReadAllText(Path.Combine(Ladder, "step-07-ring-one-branch.fluid"))));

        Assert.Contains(plan, static l => l.Trim() == "loop TV_AHU to NM_AHU");
        Assert.Contains(plan, static l => l.Trim() == "run NM_AHU.0 > TV_AHU.b");
    }

    [Fact]
    public void TheMixedHeadersRightSideIsTheBranchDeclaredLast()
    {
        // 08e as the user accepted it: AHU hangs under N3, the radiators and the floor in series under N4, and DHW --
        // declared last -- is the ring's right side.
        var plan = Plan(Solve(File.ReadAllText(Path.Combine(Ladder, "step-08e-header-mixed.fluid"))));
        var spines = plan.Select((line, i) => (line, i)).Where(static l => l.line.EndsWith("(spine)", StringComparison.Ordinal)).Select(l => plan[l.i + 2].Trim()).ToList();

        Assert.Equal(["run N3.1 > N4.0", "run N4.1 > N5 > N5__TV_DHW > N5__TV_DHW__out > TV_DHW.a"], spines);
    }

    [Fact]
    public void AnExchangersOtherSideHangsOffItsPorts()
    {
        // The substation: HX1 is the secondary ring's source, and its primary side hangs off in2 and out2.
        var plan = Plan(Solve(ContractFixture.Sample("m2-substation.fluid")));

        Assert.Contains("  pendant at HX1.in2: NPS, PCV; 2 runs", plan);
        Assert.Contains("  pendant at HX1.out2: NPR; 1 run", plan);
    }

    [Fact]
    public void AnOpenFormIsTheHeaderBetweenItsInletAndOutlet()
    {
        // The tour's third circuit (C19): NB1 feeds NJ1, whose two paths -- HE2's chain and the ahu block -- meet at NJ2
        // before NB2; TV2, fed from NJ1's third port, mixes into RB1 and hangs off NJ1.
        var plan = Plan(Solve(ContractFixture.Sample("m1-syntax-tour.fluid")), 3);

        Assert.Equal("open form (C19), head NB1", plan[0]);
        Assert.Contains("    header NJ1 to NJ2, 2 branches", plan);
        Assert.Contains(plan, static l => l.StartsWith("  pendant at NJ1.", StringComparison.Ordinal) && l.Contains("TV2", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryRunIsReadOnceAndNothingIsLoose(string name)
    {
        // Every run of the fragment is either in the body once or in one pendant; every ladder step and sample is
        // series-parallel, so nothing is left loose, and whatever hangs off a body is a tree.
        var scene = Solve(Source(name));
        var runs = scene.Provenance.Count(static n => n.Rule == "E1" && n.Subject.StartsWith("run ", StringComparison.Ordinal));
        // An attached ring (D-157) re-reads a pendant's runs as a ring; they are counted once, in the pendant.
        var plan = scene.Provenance.Where(static n => n.Rule == "E2").Select(static n => n.Reason.Trim())
            .TakeWhile(static l => !l.StartsWith("attached ring ", StringComparison.Ordinal)).ToList();
        var inBody = plan.Count(static l => l.StartsWith("run ", StringComparison.Ordinal));
        var inPendants = plan
            .Where(static l => l.StartsWith("pendant at ", StringComparison.Ordinal))
            .Sum(static l => int.Parse(l[(l.LastIndexOf("; ", StringComparison.Ordinal) + 2)..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(runs, inBody + inPendants);
        Assert.DoesNotContain(plan, static l => l.StartsWith("loose ", StringComparison.Ordinal) || l.EndsWith("(not a tree)", StringComparison.Ordinal));
    }
}
