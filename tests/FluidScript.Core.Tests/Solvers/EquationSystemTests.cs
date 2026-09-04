using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The assembled residual function, evaluated at states whose answers are known by hand.</summary>
/// <remarks>
/// Nothing here solves anything — <c>P3.6b</c> brings Newton. What is checkable now is that the
/// function is the one the two layouts describe: the right shape, finite everywhere a state is legal,
/// and zero on the rows a hand-constructed state satisfies.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class EquationSystemTests
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
    public void TheSystemHasTheShapeTheTwoLayoutsAgreedOn(string sample)
    {
        var system = Assemble(sample, out var posedness, out _);

        Assert.Equal(system.Unknowns.Count, system.Columns);
        Assert.Equal(system.Equations.Count, system.Rows);
        Assert.Equal(system.Columns, system.UnknownScales.Length);
        Assert.Equal(system.Rows, system.ResidualScales.Length);

        // Every stated constraint carries a residual now, so nothing is left unevaluated -- and the
        // assertion is the empty set rather than a count, because a count would keep passing if a
        // constraint were dropped from the table and from the assembler together.
        Assert.Empty(system.Unevaluated);
        Assert.Equal(
            posedness.Counting.Constraints.Length,
            system.Rows - system.Equations.ConstraintOffset);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryResidualIsFiniteAtAStateTheFluidAccepts(string sample)
    {
        var system = Assemble(sample, out _, out var seed);
        var residuals = new double[system.Rows];

        Assert.True(system.TryEvaluateResiduals(seed.Values.AsSpan(), residuals));
        Assert.All(residuals, static residual => Assert.True(double.IsFinite(residual)));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void AtRestEveryBalanceIsZeroExceptWhereAComponentAddsEnergy(string sample)
    {
        // The strongest statement available before a solver exists. With one uniform state and no flow
        // anywhere, every mass balance is a sum of zeros and every energy balance is a sum of zeros
        // times enthalpies -- so the only non-zero balance is one a component injects into, which is
        // exactly D-69's claim about where a duty appears.
        //
        // A stated boundary flow is the second thing that survives being at rest, and it has to be: it
        // is a constant the script put in that node's balance, not a term the iterate scales (S-22).
        // The exemption is narrow on purpose -- AStatedBoundaryFlowReachesTheBalanceItNames asserts the
        // value the exemption steps over, so nothing here is merely being tolerated.
        var system = Assemble(sample, out _, out var seed);
        var graph = Graph(sample);
        var residuals = new double[system.Rows];

        Assert.True(system.TryEvaluateResiduals(seed.Values.AsSpan(), residuals));

        var injecting = graph.Components
            .Where(static component => component.InjectsEnergy)
            .Select(static component => component.Name)
            .ToHashSet(StringComparer.Ordinal);

        for (var row = 0; row < system.Rows; row++)
        {
            var declaration = system.Equations.Rows[row];

            if (declaration.Kind is not (EquationKind.Mass or EquationKind.Energy))
            {
                continue;
            }

            if (declaration.Kind == EquationKind.Energy && Reaches(graph, injecting, declaration.OwnerComponentId))
            {
                continue;
            }

            if (graph.Nodes.Any(node =>
                node.Name == declaration.OwnerComponentId
                && HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is not null))
            {
                continue;
            }

            Assert.Equal(0, residuals[row], 9);
        }
    }

    [Fact]
    public void AStatedBoundaryFlowReachesTheBalanceItNames()
    {
        // S-22. `S1 supply t=60 flow=0.12` puts 0.12 kg/s into that node's mass balance whatever the
        // iterate, and before the fix it entered no equation at all: well-posedness leaves a stated flow
        // out of FluxNodes because it declares no unknown, and the assembler read only that list. A
        // header whose every boundary is a stated flow then had a circuit at rest as an exact solution,
        // and the count was square the whole time -- which is why no other test saw it.
        //
        // Every stated boundary is checked rather than one named node, because one of them has no row
        // to check: a closed hydraulic component drops one redundant mass balance, and here that is S1's.
        var graph = Graph("m4-storage-header.fluid");
        var system = Assemble("m4-storage-header.fluid", out _, out var seed);
        var residuals = new double[system.Rows];

        Assert.True(system.TryEvaluateResiduals(seed.Values.AsSpan(), residuals));

        var checkedRows = 0;

        foreach (var node in graph.Nodes)
        {
            if (HydraulicPartition.Stated(node.Component, HydraulicPartition.Flow) is not { } given)
            {
                continue;
            }

            var row = Array.FindIndex(
                [.. system.Equations.Rows],
                declaration => declaration.Kind == EquationKind.Mass
                    && declaration.OwnerComponentId == node.Name);

            if (row < 0)
            {
                continue;
            }

            checkedRows++;

            Assert.Equal(
                node.Component.Boundary is BoundaryRole.Return ? -given : given,
                residuals[row],
                9);
        }

        Assert.True(checkedRows >= 3, $"only {checkedRows} stated boundaries carried a mass row");
    }

    [Fact]
    public void AnExchangersDutyAppearsInTheEnergyBalanceOfTheNodeItDischargesInto()
    {
        // D-69 made concrete. HE1 declares no energy row of its own; its 30 kW leaves through a port,
        // and the assembler adds it to the node that port touches. At rest the split is even -- the
        // smoothing has nothing to bias -- so each side carries half.
        var graph = Graph("m2-cooling-loop.fluid");
        var system = Assemble("m2-cooling-loop.fluid", out _, out var seed);
        var residuals = new double[system.Rows];

        Assert.True(system.TryEvaluateResiduals(seed.Values.AsSpan(), residuals));

        var exchanger = (HeatExchanger)graph.Components.Single(static c => c.Name == "HE1");
        var touched = new[] { "PU1__HE1", "HE1__3WV" };
        var total = 0.0;

        foreach (var name in touched)
        {
            var row = Array.FindIndex(
                [.. system.Equations.Rows],
                declaration => declaration.Kind == EquationKind.Energy
                    && declaration.OwnerComponentId == name);

            Assert.True(row >= 0, $"{name} has no energy row");
            total += residuals[row];
        }

        Assert.Equal(exchanger.Power, total, 6);
    }

    [Fact]
    public void AnIdealLinkAssertsThatTwoNodesAreOnePressure()
    {
        var graph = Graph("m2-cooling-loop.fluid");
        var system = Assemble("m2-cooling-loop.fluid", out var posedness, out var seed);
        var residuals = new double[system.Rows];
        var values = seed.Values.ToArray();

        var link = Assert.Single(posedness.Counting.IdealLinks);
        var from = Array.FindIndex([.. graph.Nodes], node => node.Name == link.From.Name);

        // Push one end 5 kPa up and the link's residual must be exactly that, in pascals.
        values[system.Unknowns.NodePressure(from)] += 5000;

        Assert.True(system.TryEvaluateResiduals(values, residuals));
        Assert.Equal(5000, Math.Abs(residuals[system.Equations.LinkOffset]), 6);
    }

    [Fact]
    public void AStatedPressureIsMetExactlyWhenTheIterateCarriesIt()
    {
        var graph = Graph("m2-cooling-loop.fluid");
        var system = Assemble("m2-cooling-loop.fluid", out var posedness, out var seed);
        var residuals = new double[system.Rows];
        var values = seed.Values.ToArray();

        for (var index = 0; index < posedness.Counting.PressureNodes.Length; index++)
        {
            var node = posedness.Counting.PressureNodes[index];
            var stated = HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure);

            Assert.NotNull(stated);

            var position = Array.FindIndex([.. graph.Nodes], candidate => candidate.Name == node.Name);

            values[system.Unknowns.NodePressure(position)] = stated.Value;
        }

        Assert.True(system.TryEvaluateResiduals(values, residuals));

        for (var index = 0; index < posedness.Counting.PressureNodes.Length; index++)
        {
            Assert.Equal(0, residuals[system.Equations.BoundaryOffset + index], 9);
        }
    }

    [Fact]
    public void ScalingDividesEachRowByItsOwnReferenceAndNothingElse()
    {
        var system = Assemble("m2-cooling-loop.fluid", out _, out var seed);
        var raw = new double[system.Rows];
        var scaled = new double[system.Rows];
        var values = seed.Values.ToArray();

        values[system.Unknowns.NodePressure(0)] += 12345;

        Assert.True(system.TryEvaluateResiduals(values, raw));
        Assert.True(system.TryEvaluateScaled(values, scaled));

        for (var row = 0; row < system.Rows; row++)
        {
            Assert.Equal(raw[row] / system.ResidualScales[row], scaled[row], 12);
        }
    }

    [Fact]
    public void AnIterateOutsideTheFluidDomainIsReportedRatherThanThrown()
    {
        // A Newton step that leaves the property domain is an ordinary event on the way to a solution.
        // The line search shortens the step; nothing throws, and no pipeline stage may.
        var system = Assemble("m2-cooling-loop.fluid", out _, out var seed);
        var residuals = new double[system.Rows];
        var values = seed.Values.ToArray();

        values[system.Unknowns.NodeEnthalpy(0)] = -1e12;

        Assert.False(system.TryEvaluateResiduals(values, residuals));
        Assert.InRange(system.OutOfDomainNode, 0, system.Columns);
    }

    /// <summary>Whether a node touches any component that injects energy.</summary>
    private static bool Reaches(CircuitGraph graph, HashSet<string> injecting, string node)
    {
        var element = Array.FindIndex([.. graph.Components], component => component.Name == node);

        for (var port = 0; port < graph.Components[element].Ports.Length; port++)
        {
            var peer = graph.Adjacency.Peer(element, port);

            if (peer.Exists && injecting.Contains(graph.Components[peer.Component].Name))
            {
                return true;
            }
        }

        return false;
    }

    private static CircuitGraph Graph(string sample) =>
        GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;

    /// <summary>Assembles a sample at rest: one uniform 20 °C state, no flow anywhere.</summary>
    private static EquationSystem Assemble(
        string sample, out WellPosednessResult posedness, out StateVector seed)
    {
        var graph = Graph(sample);

        posedness = WellPosedness.Check(graph);

        var layout = SystemLayout.Build(graph, posedness.Counting);
        var reference = graph.Substance.FromPressureTemperature(
            Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(293.15, Dimension.Temperature));

        Assert.True(reference.IsSuccess, reference.Error?.Message);

        var values = new double[layout.Count];

        for (var index = 0; index < layout.Count; index++)
        {
            values[index] = layout.Unknowns[index].Kind switch
            {
                UnknownKind.NodeEnthalpy => reference.Value.Enthalpy.SiValue,
                _ => 0,
            };
        }

        seed = new StateVector([.. values]);

        return EquationSystem.Build(graph, posedness, seed);
    }
}
