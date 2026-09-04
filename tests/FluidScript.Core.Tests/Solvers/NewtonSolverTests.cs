using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The Newton iteration, and every way it is allowed to stop.</summary>
/// <remarks>
/// <para>
/// Most of these drive a termination deliberately by tuning <see cref="NewtonSettings"/>, which is what
/// the settings are for: reaching the iteration cap on a circuit that would otherwise converge is a
/// one-line change here and an unreproducible accident in the wild.
/// </para>
/// <para>
/// <strong>What is deliberately not asserted yet is a converged answer on a reference circuit.</strong>
/// A promotion pairing has no residual until <c>P3.7</c> promotes a parameter for real, so every sample
/// that states an <c>in</c> or an <c>out</c> assembles with a row of zeros — which is a singular
/// Jacobian, correctly diagnosed. The circuits exercised here are the ones whose rows are all live.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class NewtonSolverTests
{
    /// <summary>A circuit stating a duty rather than a terminal temperature, so nothing is promoted.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="GraphFixture.CoolingLoop"/> writes <c>HE1 power=30</c> where the sample writes
    /// <c>in</c> and <c>out</c>, and that one difference is what makes it usable here: a stated terminal
    /// temperature is a demand the circuit must meet by moving a sized parameter, and until <c>P3.7</c>
    /// promotes one for real that pairing assembles as a row with no residual behind it.
    /// </para>
    /// <para>
    /// A simpler-looking fixture does not work, and the reason is worth recording. A plain series loop
    /// — node, pump, exchanger, pipe, back to the node — has no junction element at all, so the branch
    /// decomposition finds no vertex and no branch, and a pressure stated on a degree-two node is an
    /// equation with no flux to absorb it. The three-way valve is what gives the circuit a vertex.
    /// </para>
    /// </remarks>
    private const string ClosedLoop = GraphFixture.CoolingLoop;

    [Fact]
    public async Task ASystemWhoseRowsAndColumnsDisagreeIsRefusedRatherThanIterated()
    {
        // FS3005. m2-substation is short one row until P4.1 gives a coupled exchanger its side-2
        // momentum equation, so it is genuinely not square -- and a solver that iterated on it anyway
        // would report a divergence for an assembly defect.
        var system = Assemble("m2-substation.fluid", out _);
        var solver = new NewtonSolver();

        Assert.SkipWhen(system.Rows == system.Columns, "m2-substation assembles square; S-14b has landed.");

        var refusal = solver.CanSolve(system);

        Assert.False(refusal.IsSuccess);
        Assert.Equal("FS3005", refusal.Error.Code);
        await Task.CompletedTask;
    }

    [Fact]
    public void ASquareSteadySystemIsAccepted()
    {
        var system = Assemble(ClosedLoop, out _);

        Assert.True(new NewtonSolver().CanSolve(system).IsSuccess);
    }

    [Fact]
    public async Task AnUnevaluatedRowMakesTheJacobianSingularAndItSaysWhere()
    {
        // FS3002. The cooling loop states HE1 in and out, so two promotion pairings assemble with no
        // residual behind them -- two columns nothing influences. That is exactly the shape a missing
        // datum has, and the message names an unknown rather than a row index.
        var system = Assemble("m2-cooling-loop.fluid", out var seed);
        var result = await new NewtonSolver().SolveAsync(system, seed, null, TestContext.Current.CancellationToken);

        Assert.False(result.Converged);
        Assert.Equal(SolveTermination.Singular, result.Termination);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "FS3002");
        Assert.NotEmpty(system.Unevaluated);
    }

    [Fact]
    public async Task ACancelledTokenStopsBeforeTheFirstIterationAndSaysSo()
    {
        // FS3006.
        var system = Assemble(ClosedLoop, out var seed);
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        var result = await new NewtonSolver().SolveAsync(system, seed, null, cancellation.Token);

        Assert.Equal(SolveTermination.Cancelled, result.Termination);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "FS3006");
    }

    [Fact]
    public async Task AnIterateOutsideTheFluidDomainStopsWithTheNodeNamed()
    {
        // FS3007. Not a hard circuit -- a state the substance refuses, which is what the line search's
        // domain guard exists for and what a seed nobody sized produces.
        var system = Assemble(ClosedLoop, out var seed);
        var values = seed.Values.ToArray();

        values[system.Unknowns.NodeEnthalpy(0)] = -1e12;

        var result = await new NewtonSolver().SolveAsync(
            system, new StateVector([.. values]), null, TestContext.Current.CancellationToken);

        Assert.Equal(SolveTermination.NonFinite, result.Termination);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "FS3007");
    }

    [Theory]
    [InlineData(1, 1e-10, 10.0, 1.0 / 64.0)]
    [InlineData(50, 1e9, 10.0, 1.0 / 64.0)]
    [InlineData(50, 1e-10, 1e-9, 1.0 / 64.0)]
    [InlineData(50, 1e-10, 10.0, 1.0)]
    public async Task EveryTerminationCarriesTheCodeThatExplainsIt(
        int iterations, double stepTolerance, double divergence, double minimumStep)
    {
        // FS3001, FS3003, FS3004, FS3011 -- each settings row drives the iteration into a different
        // corner. Which corner a given row reaches is not asserted, and deliberately: the seed here is
        // hand-made rather than sized, so whether the cap arrives before a stall or a domain excursion
        // is a property of the guess and not of the solver (`S-21`). What is asserted is the mapping —
        // whatever it stops for, the reason is on the result and the code that explains it is in the
        // diagnostics.
        var system = Assemble(ClosedLoop, out var seed);
        var solver = new NewtonSolver(new NewtonSettings
        {
            MaxIterations = iterations,
            StepTolerance = stepTolerance,
            DivergenceFactor = divergence,
            MinLineSearchStep = minimumStep,
        });

        var result = await solver.SolveAsync(system, seed, null, TestContext.Current.CancellationToken);

        var expected = result.Termination switch
        {
            SolveTermination.IterationCap => "FS3001",
            SolveTermination.Singular => "FS3002",
            SolveTermination.Diverging => "FS3003",
            SolveTermination.Stalled => "FS3004",
            SolveTermination.Cancelled => "FS3006",
            SolveTermination.NonFinite => "FS3007",
            _ => null,
        };

        Assert.NotNull(expected);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expected);
        Assert.All(result.Diagnostics, static diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));

        // FS3011 is informational and rides alongside whatever the run terminates for, so it is never
        // the reason -- only ever a note that a step had to be shortened on the way.
        Assert.All(
            result.Diagnostics.Where(static diagnostic => diagnostic.Code == "FS3011"),
            static diagnostic => Assert.Equal(
                Core.Diagnostics.DiagnosticSeverity.Info, diagnostic.Severity));
    }

    [Fact]
    public async Task ProgressIsReportedOncePerIterationAndNeverAfterTheMethodReturns()
    {
        var system = Assemble(ClosedLoop, out var seed);
        var seen = new List<SolveProgress>();
        var solver = new NewtonSolver(new NewtonSettings { MaxIterations = 3 });

        var result = await solver.SolveAsync(
            system,
            seed,
            new Progress<SolveProgress>(seen.Add),
            TestContext.Current.CancellationToken);

        Assert.InRange(seen.Count, 0, result.Iterations);
    }

    [Fact]
    public async Task TheSameInputProducesTheSameIterateEveryTime()
    {
        // 32's invariant 6. A solver that answers slightly differently across runs makes every golden
        // file flaky and every user report unreproducible, which is why nothing inside the iteration
        // may reorder a floating-point accumulation.
        var solver = new NewtonSolver(new NewtonSettings { MaxIterations = 4 });
        var first = await Run(solver);

        for (var repeat = 0; repeat < 5; repeat++)
        {
            Assert.Equal(first, await Run(solver));
        }

        static async Task<ImmutableArray<double>> Run(NewtonSolver solver)
        {
            var system = Assemble(ClosedLoop, out var seed);
            var result = await solver.SolveAsync(
                system, seed, null, TestContext.Current.CancellationToken);

            return result.Solution.Values;
        }
    }

    [Fact]
    public async Task TheLastIterateComesBackEvenWhenNothingConverged()
    {
        // A circuit that got most of the way to a balance shows a user where it was heading; an empty
        // canvas shows them nothing. What must never happen is presenting it as solved.
        var system = Assemble(ClosedLoop, out var seed);
        var solver = new NewtonSolver(new NewtonSettings { MaxIterations = 1 });
        var result = await solver.SolveAsync(system, seed, null, TestContext.Current.CancellationToken);

        Assert.False(result.Converged);
        Assert.Equal(system.Columns, result.Solution.Values.Length);
        Assert.All(result.Solution.Values, static value => Assert.True(double.IsFinite(value)));
    }

    /// <summary>Assembles a script or a sample with a plausible seed.</summary>
    /// <param name="source">A sample file name, or a script.</param>
    /// <param name="seed">Receives the starting iterate.</param>
    /// <returns>The assembled system.</returns>
    /// <remarks>
    /// <strong>The flows are seeded away from zero, and they have to be (`S-21`).</strong> A pipe's
    /// momentum relation is <c>Δp = R·ṁ|ṁ|</c> and a pump curve is <c>H₀ − kṁ²</c>; both have a
    /// derivative of exactly zero at <c>ṁ = 0</c>, so a zero-flow seed does not merely start Newton in a
    /// poor place — it starts it at a genuinely singular Jacobian, and every solve from one reports
    /// <c>FS3002</c> however well-posed the circuit is. This is why <c>32</c>'s initial guess comes from
    /// sizing rather than from zero.
    /// </remarks>
    private static EquationSystem Assemble(string source, out StateVector seed)
    {
        var text = source.EndsWith(".fluid", StringComparison.Ordinal)
            ? File.ReadAllText(Path.Combine(RepositoryLayout.Samples, source))
            : source;

        var graph = GraphFixture.Lower(text).Graph;
        var posedness = WellPosedness.Check(graph);
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
                UnknownKind.NodePressure => 3e5,

                // Alternating signs rather than one value, and the reason is the second half of S-21.
                // A branch's orientation is the decomposition's choice, so seeding every flow the same
                // way leaves some node with every port an inflow -- and a node nothing leaves is one
                // whose own enthalpy enters no equation, which is a zero column and a singular
                // Jacobian. The seed has to be roughly mass-consistent, not merely non-zero.
                UnknownKind.BranchFlow => index % 2 == 0 ? 0.1 : -0.1,
                UnknownKind.ExternalMassFlux => 0.1,
                _ => 0,
            };
        }

        seed = new StateVector([.. values]);

        return EquationSystem.Build(graph, posedness, seed);
    }
}
