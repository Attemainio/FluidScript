using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The diagonal that makes one convergence tolerance mean one thing.</summary>
/// <remarks>
/// <c>22</c>'s invariant 5 asks every residual to be comparable across component kinds, and a component
/// evaluated alone has nothing to be comparable to — which is why the property is asserted here and not
/// there (<c>S-3</c>). Unscaled, a pascal residual and a kg/s residual differ by five orders before
/// either is wrong, and the norm measures the pressure equation alone.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ResidualScalesTests
{
    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory.GetFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryRowHasAPositiveScale(string sample)
    {
        // A zero or negative scale divides a residual into nonsense, and both are reachable from a
        // seed: a flow scale comes from the seed's own magnitude, and an initial guess of zero flow
        // is the ordinary case rather than a strange one.
        var (graph, layout, scales) = Scale(sample, static _ => 0);

        Assert.Equal(layout.Count, scales.Length);
        Assert.All(scales, static scale => Assert.True(scale > 0));
        Assert.NotEmpty(graph.Components);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void APressureRowIsScaledByThePressureReference(string sample)
    {
        var (_, layout, scales) = Scale(sample, static _ => 1);

        for (var row = 0; row < layout.Count; row++)
        {
            if (layout.Rows[row].ResidualSiUnit == "Pa")
            {
                Assert.Equal(Tolerances.PressureScale, scales[row]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void AnEnergyRowIsScaledByAFlowTimesAnEnthalpy(string sample)
    {
        // The one scale 36 does not tabulate, and deliberately: an energy balance is the sum of
        // m-dot times h, so a power scale is a flow scale times an enthalpy scale and a fourth
        // constant would only be a fourth thing to keep consistent with three that determine it.
        var (_, layout, scales) = Scale(sample, static _ => 1);

        for (var row = 0; row < layout.Count; row++)
        {
            if (layout.Rows[row].ResidualSiUnit == "W")
            {
                Assert.Equal(Tolerances.EnthalpyScale, scales[row] / 1.0, 6);
            }
        }
    }

    [Fact]
    public void ANodeJoiningTwoVeryDifferentBranchesTakesTheLargerOne()
    {
        // S-13, and the topology both rules were written for. The residual is a sum of the incident
        // flows, so its magnitude is set by the biggest of them; scaling by the smallest would inflate
        // a residual that can never get that small and the row would never converge.
        var graph = Lower("m2-cooling-loop.fluid");
        var posedness = WellPosedness.Check(graph);
        var system = SystemLayout.Build(graph, posedness.Counting);
        var layout = EquationLayout.Build(graph, posedness);
        var ports = PortMap.Build(graph);

        var values = new double[system.Count];
        Array.Fill(values, 1.0);

        // One branch at 10 kg/s beside three at 0.05 -- the primary and bypass 36 argues from.
        foreach (var branch in graph.Branches)
        {
            values[system.BranchFlow(branch.Index)] = branch.Index == 0 ? 10.0 : 0.05;
        }

        var unknowns = UnknownScales.Build(system, new StateVector([.. values]));
        var scales = ResidualScales.Build(graph, layout, ports, unknowns, system);

        var wide = graph.Components
            .Select((component, index) => (component, index))
            .Where(entry => Enumerable.Range(0, entry.component.Ports.Length)
                .Any(port => ports[entry.index, port].Branch == 0))
            .ToArray();

        Assert.NotEmpty(wide);

        foreach (var (component, index) in wide)
        {
            var rows = layout.Components[index];

            for (var row = rows.FirstRow; row < rows.FirstRow + rows.RowCount; row++)
            {
                if (layout.Rows[row].ResidualSiUnit == "kg/s")
                {
                    Assert.Equal(10.0, scales[row]);
                }
            }

            Assert.NotNull(component.Name);
        }
    }

    [Fact]
    public void ARowNoComponentOwnsTakesTheFixedReferenceForItsUnit()
    {
        // A stated pressure, a datum and an ideal link belong to the script rather than to a
        // component, so there is no branch to ask for a flow scale. They take the reference.
        var graph = Lower("m2-cooling-loop.fluid");
        var posedness = WellPosedness.Check(graph);
        var system = SystemLayout.Build(graph, posedness.Counting);
        var layout = EquationLayout.Build(graph, posedness);
        var seed = new StateVector([.. Enumerable.Repeat(1.0, system.Count)]);

        var scales = ResidualScales.Build(
            graph, layout, PortMap.Build(graph), UnknownScales.Build(system, seed), system);

        for (var row = layout.LinkOffset; row < layout.ConstraintOffset; row++)
        {
            Assert.Equal(Tolerances.PressureScale, scales[row]);
        }
    }

    private static (CircuitGraph Graph, EquationLayout Layout, ImmutableArray<double> Scales) Scale(
        string sample, Func<int, double> seed)
    {
        var graph = Lower(sample);
        var posedness = WellPosedness.Check(graph);
        var system = SystemLayout.Build(graph, posedness.Counting);
        var layout = EquationLayout.Build(graph, posedness);
        var values = new StateVector([.. Enumerable.Range(0, system.Count).Select(seed)]);

        return (
            graph,
            layout,
            ResidualScales.Build(graph, layout, PortMap.Build(graph), UnknownScales.Build(system, values), system));
    }

    private static CircuitGraph Lower(string sample) =>
        GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;
}
