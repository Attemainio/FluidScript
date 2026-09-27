using System.Collections.Immutable;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Topology.Hydraulics;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Topology.Hydraulics;

/// <summary>A circuit that states no fluid carries water, and is told so when nothing else says it (<c>D-181</c>, <c>L-87</c>).</summary>
[Trait("Category", "Unit")]
public sealed class CircuitFluidsTests
{
    private const string Loop = """
        fluidscript 2

        circuit "loop":
          HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50
          LOAD  heat_exchanger  power = -30  dp = 0
          PU1  pump
          P1  pipe  dn = 15  length = 10
          N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N1
        """;

    /// <summary>The substation with each side in its own circuit: only the district side states its fluid.</summary>
    private const string Substation = """
        fluidscript 2

        circuit "district":
          fluid = water

          NPS  inlet  t = 85  p = 600
          NPR  outlet  p = 350
          PCV  valve
          HX1  heat_exchanger  power = -150  in.t = 85  out.t = 45  secondary.in.t = 40  secondary.out.t = 60  u = 3300

          NPS - PCV
          PCV - HX1.in   12 m  DN25
          HX1.out - NPR

        circuit "heating":
          SP  pump
          LOAD  heat_exchanger  power = -150  dt = 20

          HX1.secondary.out - NSUP   30 m  DN32
          NSUP - LOAD - NRET
          NRET - SP   30 m  DN32
          SP - HX1.secondary.in
        """;

    private static ImmutableArray<Diagnostic> Unstated(string script)
    {
        var model = GraphFixture.Bind(script);
        var graph = GraphFixture.Lower(script).Graph;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        CircuitFluids.ReportUnstated(model, graph, Water.Instance, diagnostics);

        return diagnostics.ToImmutable();
    }

    [Fact]
    public void ACircuitThatStatesNoFluidIsToldItCarriesWater()
    {
        var model = GraphFixture.Bind(Loop);
        var chosen = CircuitFluids.Choose(model, SubstanceRegistry.Default, ImmutableArray.CreateBuilder<Diagnostic>());

        Assert.Same(Water.Instance, chosen);

        var note = Assert.Single(Unstated(Loop));
        Assert.Equal("FS2008", note.Code);
        Assert.Equal(DiagnosticSeverity.Info, note.Severity);
        Assert.Equal("'loop' states no fluid, so it carries water. Write 'fluid = water' to say so.", note.Message);
        Assert.Equal(Loop.IndexOf("circuit \"loop\"", StringComparison.Ordinal), note.Span!.Value.Start);
    }

    [Fact]
    public void AStatedFluidIsNotNoted() =>
        Assert.Empty(Unstated(Loop.Edited("circuit \"loop\":\n", "circuit \"loop\":\n  fluid = water\n")));

    [Fact]
    public void ABranchJoinedToAStatedCircuitCarriesItsWaterUnremarked()
    {
        // The header: "AHU" and "radiators" state nothing, and draw the water "heating" states through its headers.
        var header = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"));

        Assert.Empty(Unstated(header));
    }

    [Fact]
    public void TheFarSideOfAnExchangerIsNotJoinedByIt()
    {
        // HX1 is listed in both partitions and carries neither side's fluid into the other: the heating loop is on
        // its own, and water is its default, not the district's.
        var note = Assert.Single(Unstated(Substation));

        Assert.Equal("'heating' states no fluid, so it carries water. Write 'fluid = water' to say so.", note.Message);
    }

    /// <summary>A node rule I2 puts on a line belongs to the circuit the line is written in.</summary>
    /// <remarks>
    /// Before, it took the circuit of the line's first component: <c>HX1.secondary.out - NSUP</c> in the heating loop
    /// starts at the district's exchanger, and put a node of the heating loop in the district, which joined the two
    /// sides' partitions for this check and drew the node in the wrong circuit.
    /// </remarks>
    [Fact]
    public void ANodeBetweenTwoComponentsIsInTheCircuitThatWroteTheLine() =>
        Assert.Equal("heating", GraphFixture.Lower(Substation).Graph.CircuitOf["HX1__NSUP__in"]);

    [Fact]
    public void CircuitsThatStateDifferentFluidsAreSolvedAsTheFirstAndTold()
    {
        var model = GraphFixture.Bind(Substation.Edited("circuit \"heating\":\n", "circuit \"heating\":\n  fluid = air\n"));
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var chosen = CircuitFluids.Choose(model, SubstanceRegistry.Default, diagnostics);

        Assert.Same(Water.Instance, chosen);
        var warning = Assert.Single(diagnostics);
        Assert.Equal("FS2009", warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(
            "'heating' carries air, and this version solves a file with one fluid: every circuit is solved as water.",
            warning.Message);
    }

    [Fact]
    public void AnUnknownFluidIsReportedOnItsCircuitAndWaterStandsIn()
    {
        var model = GraphFixture.Bind(Loop.Edited("circuit \"loop\":\n", "circuit \"loop\":\n  fluid = brine\n"));
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var chosen = CircuitFluids.Choose(model, SubstanceRegistry.Default, diagnostics);

        Assert.Same(Water.Instance, chosen);
        var error = Assert.Single(diagnostics);
        Assert.Equal("FS2001", error.Code);
        Assert.NotNull(error.Span);
    }
}
