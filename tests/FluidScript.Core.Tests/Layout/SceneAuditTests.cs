using System.Collections.Immutable;

using FluidScript.Core.Layout;
using FluidScript.Core.Model;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// The audit measures what <c>28</c> B says it measures, shown on scenes bent to break one constraint at a time
/// (<c>C-95</c>): a mirrored loop runs counter-clockwise, a pipe through its own component's body is a pipe through a
/// box, and a signal line is held to boxes and pipes like any other line.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SceneAuditTests
{
    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    private static (Scene Scene, ModelContractInput Input) Solve(string source)
    {
        var input = ContractFixture.Compile(source);
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return (LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model)), input);
    }

    [Theory]
    [InlineData("m2-simple-loop")]
    [InlineData("m2-cooling-loop")]
    [InlineData("m2-distribution-header")]
    public void EveryLoopOfAMirroredSceneRunsCounterClockwise(string name)
    {
        // H9 is measured on every simple directed cycle, through the inline nodes on the rails (C-95's first gap):
        // the same scene mirrored left to right has every loop running the wrong way, and the audit says so for each.
        var (scene, input) = Solve(ContractFixture.Sample(name + ".fluid"));
        var mirrored = Mirror(scene);

        Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Kind == "loop-counter-clockwise"));
        var findings = SceneAudit.Findings(mirrored, input.Model).Where(static f => f.Kind == "loop-counter-clockwise").ToList();
        Assert.True(findings.Count >= scene.Groups.Length && findings.Count >= 1, name + ": " + string.Join("\n", findings));
    }

    [Fact]
    public void APipeThroughItsOwnComponentIsAPipeThroughABox()
    {
        // H3 excuses a pipe only from the two boxes its run joins at its ends -- and then only the stub, not the body
        // (C-95's second gap): a route bent back through its own component's interior is found.
        var (scene, input) = Solve(ContractFixture.Sample("m2-simple-loop.fluid"));
        var (route, owner) = scene.Routes.Where(static r => r.Kind == "pipe")
            .Select(r => (Route: r, Owner: scene.Placements.FirstOrDefault(p => !p.IsInline && p.Anchors.Values.Any(a => a.At == r.Points[0]))))
            .First(static x => x.Owner is not null);
        Assert.NotNull(owner);
        var detour = route with { Points = [route.Points[0], owner.Inner.Centre, route.Points[0], .. route.Points.Skip(1)] };
        var bent = scene with { Routes = [.. scene.Routes.Select(r => r.ConnectionId == route.ConnectionId ? detour : r)] };

        var findings = SceneAudit.Findings(bent, input.Model).Where(static f => f.Kind == "pipe-in-inner").ToList();
        Assert.Contains(findings, f => f.First == route.ConnectionId && f.Second == owner.ComponentId && f.Hard);
    }

    [Fact]
    public void ASignalThroughABoxIsHardAndASignalAlongAPipeIsSoft()
    {
        // C-95's third gap: a signal line is measured against every inner box but its own two ends' (hard) and against
        // running along a pipe (soft); crossing a pipe stays free, as C16 hops it.
        var (scene, input) = Solve(File.ReadAllText(Path.Combine(Ladder, "step-10-instruments.fluid")));
        var signal = scene.Routes.First(static r => r.Kind == "signal" && r.ConnectionId.EndsWith(":measures", StringComparison.Ordinal));
        var victim = scene.Placements.First(static p => p.ComponentId == "HE1");
        var first = signal.Points[0];
        var last = signal.Points[^1];
        var centre = victim.Inner.Centre;
        var through = signal with { Points = [first, new Point(centre.X, first.Y), centre, new Point(centre.X, last.Y), last] };
        var pipe = scene.Routes.First(static r => r.Kind == "pipe");
        var along = signal with { Points = [pipe.Points[0], pipe.Points[1]] };

        Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Kind is "signal-in-inner" or "signal-along-pipe"));

        var hard = SceneAudit.Findings(scene with { Routes = Swap(scene.Routes, through) }, input.Model).Where(static f => f.Kind == "signal-in-inner").ToList();
        Assert.Contains(hard, f => f.First == signal.ConnectionId && f.Second == "HE1" && f.Hard);

        var soft = SceneAudit.Findings(scene with { Routes = Swap(scene.Routes, along) }, input.Model).Where(static f => f.Kind == "signal-along-pipe").ToList();
        Assert.Contains(soft, f => f.First == signal.ConnectionId && f.Second == pipe.ConnectionId && !f.Hard);
    }

    [Fact]
    public void APipeExactlyOnAnUnrelatedBoxsClearanceIsSoftAndASensorsClearanceIsNot()
    {
        // C-87. The clearance rule is >= m between inner boxes, so a pipe exactly one margin from a box
        // it does not serve satisfied a strict interior test and drew as a line brushing the clearance.
        // A pipe past a box it does not serve wants > m. A sensor stands one margin off the pipe it
        // measures by rule (28 §29), so its clearance touching that pipe is not a finding.
        var (scene, input) = Solve(File.ReadAllText(Path.Combine(Ladder, "step-10-instruments.fluid")));
        var sensor = scene.Placements.First(static p => p.ComponentId == "TE1");
        var measured = scene.Routes.First(r => r.Kind == "pipe"
            && r.Points.Zip(r.Points.Skip(1)).Any(s => Math.Abs(s.First.Y - sensor.Outer.Y) < 1e-9 && Math.Abs(s.Second.Y - sensor.Outer.Y) < 1e-9));

        Assert.Empty(SceneAudit.Findings(scene, input.Model).Where(static f => f.Kind == "pipe-in-outer"));

        // The same pipe laid along the *valve's* outer top edge, a box it does not serve (c2/c3 run
        // HE1 -> N1 -> LOAD; CV1 is on the return).
        var victim = scene.Placements.First(static p => p.ComponentId == "CV1");
        var brushing = measured with { Points = [new Point(victim.Outer.X - 1, victim.Outer.Top), new Point(victim.Outer.Right + 1, victim.Outer.Top)] };

        var soft = SceneAudit.Findings(scene with { Routes = Swap(scene.Routes, brushing) }, input.Model).Where(static f => f.Kind == "pipe-in-outer").ToList();
        Assert.Contains(soft, f => f.First == measured.ConnectionId && f.Second == "CV1" && !f.Hard);
    }

    private static ImmutableArray<Route> Swap(ImmutableArray<Route> routes, Route replacement) =>
        [.. routes.Select(r => r.ConnectionId == replacement.ConnectionId ? replacement : r)];

    /// <summary>The scene reflected in the <c>y</c> axis: every loop reverses its sense and nothing else changes.</summary>
    private static Scene Mirror(Scene scene)
    {
        static Point M(Point p) => new(-p.X, p.Y);
        static Box MB(Box b) => new(-b.Right, b.Y, b.Width, b.Height);
        static PlacedAnchor MA(PlacedAnchor a) => new(M(a.At), M(a.Direction), new Direction(-a.Flow.X, a.Flow.Y));

        return scene with
        {
            Placements = [.. scene.Placements.Select(p => p with
            {
                Inner = MB(p.Inner),
                Outer = MB(p.Outer),
                LabelAt = M(p.LabelAt),
                Anchors = p.Anchors.ToImmutableSortedDictionary(static a => a.Key, static a => MA(a.Value), StringComparer.Ordinal),
            })],
            Routes = [.. scene.Routes.Select(r => r with { Points = [.. r.Points.Select(M)], Hops = [.. r.Hops.Select(M)] })],
            Extent = MB(scene.Extent),
            Groups = [.. scene.Groups.Select(g => g with { Bounds = MB(g.Bounds) })],
        };
    }
}
