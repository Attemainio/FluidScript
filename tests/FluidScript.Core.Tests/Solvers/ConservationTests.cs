using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>`62`'s V1, V2 and V3 on every sample that converges, held to `07`'s conservation row.</summary>
/// <remarks>
/// <para>
/// <strong>These are physical claims, not solver claims.</strong> `NewtonSolver` reports a scaled norm
/// and stops at `Tolerances.NewtonResidual`; what an engineer wants to know is how many kilograms per
/// second are unaccounted for at a junction and how many watts are missing from a balance, in the
/// units the equations are written in. So the residuals are re-evaluated <em>unscaled</em> at the
/// solution and held to the absolute-plus-relative bounds `07` states, not to the solver's own norm.
/// </para>
/// <para>
/// <strong>V1 is also checked without the residual function.</strong> One mass balance per closed
/// hydraulic is dropped as redundant (`S-9`), so a row-by-row check would never look at that node.
/// Summing the branch flows at every junction node by hand covers the dropped one too, and does not
/// share the assembler's bookkeeping.
/// </para>
/// </remarks>
public sealed class ConservationTests
{
    /// <summary>Every sample `CorpusStatusTests` records as converged.</summary>
    public static TheoryData<string> Converged => new()
    {
        "m2-simple-loop.fluid",
        "m2-cooling-loop.fluid",
        "m2-distribution-header.fluid",
        "m4-storage-header.fluid",
    };

    [Theory]
    [MemberData(nameof(Converged))]
    public async Task MassIsConservedAtEveryJunctionByDirectSummation(string sample)
    {
        // V1: sum of flows at every node = 0, to 1e-9 of the circuit's flow. A node that admits an external
        // flux (a `supply` or a `return`) is balanced against that flux; every other junction against zero.
        var (run, layout, values) = await Solve(sample);
        var scale = Largest(layout, values, UnknownKind.BranchFlow);

        foreach (var node in run.Graph.Nodes)
        {
            if (!CircuitGraph.IsJunctionElement(node.Component))
            {
                continue;
            }

            var net = 0.0;

            for (var index = 0; index < run.Graph.Branches.Length; index++)
            {
                var branch = run.Graph.Branches[index];
                var flow = values[layout.BranchFlow(index)];

                if (ReferenceEquals(branch.From.Element, node.Component))
                {
                    net -= flow;
                }

                if (ReferenceEquals(branch.To.Element, node.Component))
                {
                    net += flow;
                }
            }

            // A boundary's flux is an unknown when nothing states it and a stated `flow=` otherwise.
            var flux = layout.FluxNodes.IndexOf(node);
            var external = flux >= 0
                ? Math.Abs(values[layout.ExternalFluxOffset + flux])
                : node.Component.StatedParameters.TryGetValue("flow", out var stated) ? Math.Abs(stated.SiValue) : 0;

            Assert.True(
                Math.Abs(Math.Abs(net) - external) <= Math.Max(1e-8, 1e-9 * scale),
                $"{sample}: {node.Name} nets {net:G6} kg/s against an external {external:G6} kg/s.");
        }
    }

    [Theory]
    [MemberData(nameof(Converged))]
    public async Task EveryBalanceMeetsTheConservationRowUnscaled(string sample)
    {
        // `07`: mass residual <= max(1e-8 kg/s, 1e-6 of circuit flow); energy residual <= max(0.1 W, 1e-6 of
        // circuit duty). V3's loop closure is the pressure rows: with nodal pressures as unknowns, the sum
        // of drops round any loop is exactly the sum of the branch relations' residuals along it, so every
        // pressure row inside 1e-6 of the circuit's pressure span is every loop closed to `62`'s bound.
        var (run, layout, values) = await Solve(sample);
        var posedness = WellPosedness.Check(run.Graph);
        var system = EquationSystem.Build(run.Graph, posedness, run.Solve.Solution);
        var residuals = new double[system.Rows];

        Assert.True(system.TryEvaluateResiduals(values, residuals));

        var flow = Largest(layout, values, UnknownKind.BranchFlow);
        var enthalpySpan = Span(layout, values, UnknownKind.NodeEnthalpy);
        var pressureSpan = Span(layout, values, UnknownKind.NodePressure);
        var duty = flow * enthalpySpan;

        for (var row = 0; row < system.Rows; row++)
        {
            var equation = system.Equations.Rows[row];
            var bound = equation.Kind switch
            {
                EquationKind.Mass => Math.Max(1e-8, 1e-6 * flow),
                EquationKind.Energy => Math.Max(0.1, 1e-6 * duty),
                EquationKind.Pressure => 1e-6 * pressureSpan,
                _ => double.PositiveInfinity,
            };

            Assert.True(
                Math.Abs(residuals[row]) <= bound,
                $"{sample}: {equation.Name} misses by {residuals[row]:G6} {equation.ResidualSiUnit}, bound {bound:G3}.");
        }
    }

    private static async Task<(OuterLoopResult Run, SystemLayout Layout, double[] Values)> Solve(string sample)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample));
        var result = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, sample, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged, $"{sample} stopped at {result.Value.Solve.Termination}.");

        var layout = SystemLayout.Build(
            result.Value.Graph, WellPosedness.Check(result.Value.Graph).Counting);

        return (result.Value, layout, [.. result.Value.Solve.Solution.Values]);
    }

    private static double Largest(SystemLayout layout, double[] values, UnknownKind kind)
    {
        var largest = 0.0;

        for (var index = 0; index < layout.Count; index++)
        {
            if (layout.Unknowns[index].Kind == kind)
            {
                largest = Math.Max(largest, Math.Abs(values[index]));
            }
        }

        return largest;
    }

    private static double Span(SystemLayout layout, double[] values, UnknownKind kind)
    {
        var least = double.PositiveInfinity;
        var most = double.NegativeInfinity;

        for (var index = 0; index < layout.Count; index++)
        {
            if (layout.Unknowns[index].Kind == kind)
            {
                least = Math.Min(least, values[index]);
                most = Math.Max(most, values[index]);
            }
        }

        return most - least;
    }
}
