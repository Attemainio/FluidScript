using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Layout.Drawing;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// Every picture the layout has reached, pinned (P6.10 R6): the ladder steps, their variants, the stress plants and the
/// samples, each drawn and compared with its placements, routes and groups as checked in under <c>Layout/Goldens</c>.
/// </summary>
/// <remarks>
/// Until the switch the parity harness compared the composed engine with the engine the ladder was first drawn by
/// (<c>28</c> E5); the switch deleted that engine, and this is what now catches a picture that moves while every audit
/// stays clean -- the way step 10 drifted on the old engine. E5's gate stays: every case is hard 0, H11 included. A
/// golden is regenerated only with <c>FLUIDSCRIPT_UPDATE_GOLDENS=1</c>, and a changed accepted picture is shown to the
/// user before its commit (<c>D-153</c>). Each case's picture is written to <c>diagnostics/layout-pictures/</c> first,
/// so a failure is read from the drawing.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LayoutPictureTests
{
    private const string UpdateVariable = "FLUIDSCRIPT_UPDATE_GOLDENS";

    private static readonly string[] Samples =
    [
        "m1-syntax-tour", "m2-cooling-loop", "m2-distribution-header", "m2-simple-loop", "m2-substation", "m4-demand-step", "m4-storage-header",
    ];

    private static string Layout => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout");

    private static string Goldens => Path.Combine(Layout, "Goldens");

    /// <summary>Every case by name: a ladder step or variant by its file name, a stress plant as <c>stress-</c> and its name, a sample as <c>sample-</c> and its name, and the 200-component header.</summary>
    public static TheoryData<string> Cases
    {
        get
        {
            var data = new TheoryData<string>();
            var names = Directory.GetFiles(Path.Combine(Layout, "Ladder"), "step-*.fluid")
                .Concat(Directory.GetFiles(Path.Combine(Layout, "Variants"), "*.fluid"))
                .Select(static f => Path.GetFileNameWithoutExtension(f))
                .Concat(Directory.GetFiles(Path.Combine(Layout, "Stress"), "*.fluid").Select(static f => "stress-" + Path.GetFileNameWithoutExtension(f)))
                .Concat(Samples.Select(static s => "sample-" + s))
                .Append("header-200")
                .Order(StringComparer.Ordinal);

            foreach (var name in names)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryPictureIsDrawnAsCheckedIn(string name)
    {
        var input = ContractFixture.Compile(Source(name));
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var scene = LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model));
        var findings = SceneAudit.Findings(scene, input.Model);
        var actual = Picture(name, scene, findings);

        var diagnostics = Path.Combine(RepositoryLayout.Diagnostics, "layout-pictures");
        Directory.CreateDirectory(diagnostics);
        File.WriteAllText(Path.Combine(diagnostics, name + ".svg"), SceneSvg.Render(scene));
        File.WriteAllText(Path.Combine(diagnostics, name + ".txt"), actual);

        var hard = findings.Where(static f => f.Hard).ToList();
        Assert.True(hard.Count == 0, name + ":\n" + string.Join("\n", hard));

        var path = Path.Combine(Goldens, name + ".txt");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Goldens);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"No picture golden for '{name}'. Run once with {UpdateVariable}=1 to create it, then look at the picture in diagnostics/layout-pictures/.");

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");

        if (expected != actual)
        {
            var was = expected.Split('\n');
            var now = actual.Split('\n');
            var gone = was.Except(now, StringComparer.Ordinal).Take(12).Select(static l => "- " + l);
            var came = now.Except(was, StringComparer.Ordinal).Take(12).Select(static l => "+ " + l);

            Assert.Fail(
                $"'{name}' is drawn differently from its checked-in picture:\n{string.Join("\n", gone.Concat(came))}\n"
                + $"If the change is intended, show the picture (diagnostics/layout-pictures/{name}.svg), then run once with {UpdateVariable}=1 and review the diff.");
        }
    }

    /// <summary>The script a case names.</summary>
    private static string Source(string name)
    {
        if (name == "header-200")
        {
            return ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers);
        }

        if (name.StartsWith("sample-", StringComparison.Ordinal))
        {
            return ContractFixture.Sample(name["sample-".Length..] + ".fluid");
        }

        if (name.StartsWith("stress-", StringComparison.Ordinal))
        {
            return File.ReadAllText(Path.Combine(Layout, "Stress", name["stress-".Length..] + ".fluid"));
        }

        var ladder = Path.Combine(Layout, "Ladder", name + ".fluid");
        return File.ReadAllText(File.Exists(ladder) ? ladder : Path.Combine(Layout, "Variants", name + ".fluid"));
    }

    /// <summary>
    /// A scene as the text its golden holds: the audit counts, then every placement, route and group sorted by id, so
    /// only a change in the picture -- never in the order the engine decided it -- changes the text.
    /// </summary>
    private static string Picture(string name, Scene scene, ImmutableArray<SceneAudit.Finding> findings)
    {
        var soft = findings.Where(static f => !f.Hard).GroupBy(static f => f.Kind).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key} {g.Count()}");
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"# {name}: hard {findings.Count(static f => f.Hard)}, soft {findings.Count(static f => !f.Hard)}");
        text.Append(findings.Any(static f => !f.Hard) ? " (" + string.Join(", ", soft) + ")\n" : "\n");

        foreach (var p in scene.Placements.OrderBy(static p => p.ComponentId, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"placement {p.ComponentId} {p.SymbolId} {Text(p.Inner.Centre)} {p.Arrangement} rot {p.Rotation}{(p.Mirrored ? " mirrored" : string.Empty)} label {Text(p.LabelAt)}\n");
        }

        foreach (var r in scene.Routes.OrderBy(static r => r.ConnectionId, StringComparer.Ordinal).ThenBy(static r => r.Kind, StringComparer.Ordinal).ThenBy(static r => string.Join(" ", r.Points.Select(Text)), StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"route {r.ConnectionId} {r.Kind} {string.Join(" ", r.Points.Select(Text))}\n");
        }

        foreach (var g in scene.Groups.OrderBy(static g => g.Id, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"group {g.Id} {g.Kind} {g.Orientation} [{N(g.Bounds.X)}, {N(g.Bounds.Y)}, {N(g.Bounds.Width)}, {N(g.Bounds.Height)}] {string.Join(" ", g.Members)}\n");
        }

        return text.ToString();
    }

    private static string Text(Point p) => $"({N(p.X)}, {N(p.Y)})";

    private static string N(double v)
    {
        var rounded = Math.Round(v, 6);
        return (rounded == 0 ? 0 : rounded).ToString("0.######", CultureInfo.InvariantCulture);
    }
}
