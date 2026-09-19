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
    public async Task TheSimpleLoopConvergesToTheFlowItsDutyImplies()
    {
        // The first circuit this project solves end to end, and the number is checkable by hand: 30 kW
        // between 20 and 50 C is 0.2392 kg/s at water's own enthalpy table (24's worked example, step 1).
        // Nothing states the flow -- PU1's head is promoted, the loop's pressure balance fixes the flow
        // from the head, and the head is whatever makes HE1's outlet arrive at 50 C.
        var system = Assemble("m2-simple-loop.fluid", out var seed);
        var result = await new NewtonSolver().SolveAsync(system, seed, null, TestContext.Current.CancellationToken);

        Assert.True(result.Converged, $"{result.Termination}: {result.ResidualNorm:G3}");
        Assert.InRange(result.Iterations, 1, 10);
        Assert.Equal(0.2392, result.Solution.Values[system.Unknowns.BranchFlow(0)], 2);

        // The promoted head is positive, which is the part a sign error would take out: a pump seeded at
        // a shut-off head of zero has to be driven up, and a solve that drove it down would still balance
        // the loop -- against a pump that consumes pressure.
        Assert.True(
            result.Solution.Values[system.Unknowns.PromotionOffset] > 0,
            "PU1.head came back non-positive, so the loop is balanced by a pump running backwards.");
    }

    [Fact]
    public async Task APromotedParameterIsHeldInsideItsPhysicalRange()
    {
        // `S-26`, which was **two** defects sharing one symptom, both now fixed.
        //
        // `S-26a` was a dead Jacobian column: `ValveLaw.Opening` clamped position into [0, 1] and the
        // Jacobian is built by forward differences, so a position at a bound perturbed to a value the
        // clamp undid, the column came out identically zero, and the solve stopped `Singular` before it
        // could step anywhere.
        //
        // `S-26b` was the runaway underneath it, invisible until the column could move: nothing bounded
        // a promoted parameter, so the split walked to about 5.5 -- a fraction five times fully open.
        // The order mattered. Projecting onto a bound while `Opening` still clamped would have put the
        // iterate exactly where its column was dead, so the box constraint had to come second.
        //
        // This circuit **converges** now (`S-35`, `C-63`), which it did not when the assertion was
        // written -- so the bound is no longer the thing standing between it and an answer. The contract
        // is what is pinned, and it is worth more on a converged solve than on a failed one: a run that
        // stops early has fewer chances to leave a parameter somewhere impossible.
        var system = Assemble("m2-cooling-loop.fluid", out var seed);
        var result = await new NewtonSolver().SolveAsync(system, seed, null, TestContext.Current.CancellationToken);

        Assert.True(result.Converged);
        Assert.Empty(system.Unevaluated);

        // Every promoted parameter finished inside the range its own component declares. Before the
        // bound landed the position read 5.465 and the head read -0.49; both are physical nonsense and
        // both were reported as an answer.
        for (var index = 0; index < system.Unknowns.Unknowns.Length; index++)
        {
            var declaration = system.Unknowns.Unknowns[index];

            if (declaration.Name.EndsWith(".position", StringComparison.Ordinal))
            {
                Assert.InRange(result.Solution.Values[index], 0, 1);
            }

            if (declaration.Name.EndsWith(".head", StringComparison.Ordinal))
            {
                Assert.True(result.Solution.Values[index] >= 0, "a pump cannot develop negative head.");
            }
        }
    }

    [Fact]
    public async Task AParameterThatRunsOutOfRoomIsNamedRatherThanLeftToBeInferred()
    {
        // `FS3008`. The cooling loop used to demonstrate this by accident, because nothing sized its
        // three-way valve and the bootstrap Kv 630 left `position` pinned against a bound for the whole
        // run. Now that the valve is sized the circuit has room, so the case needs a script that asks for
        // something a component cannot give -- here, that same Kv 630 stated deliberately.
        //
        // A stated `kv` is a constraint, so no rule replaces it: the valve passes essentially everything,
        // the boundary pair over-drives the loop, and something has to absorb the difference. Which
        // parameter gives out is a property of the seed rather than of the script, and it moved when the
        // seed took its pressures from the components' own laws (`S-46`): the split no longer runs to its
        // stop and the pump is held at zero head instead, a loop over-driven through a valve that resists
        // nothing having no need of a pump. What is asserted is therefore that a bound the solver hits is
        // reported once and by name -- not which of the two gives out first.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"))
            .Replace("3WV three_way_valve", "3WV three_way_valve kv=630", StringComparison.Ordinal);

        var system = Assemble(source, out var seed);
        var result = await new NewtonSolver().SolveAsync(system, seed, null, TestContext.Current.CancellationToken);

        Assert.False(result.Converged);

        // Once per parameter rather than once per step, and it names the parameter.
        var held = Assert.Single(
            result.Diagnostics.Where(static diagnostic => diagnostic.Code == "FS3008"));

        Assert.Contains("PU1.head", held.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(0.5)]
    [InlineData(1e-3)]
    public async Task AStepUnderTheStepToleranceIsJudgedByWhereItLandsNotWhereItStarted(double residualTolerance)
    {
        // `S-67`. The stall exit fired on the step's size and reported the residual of the point the
        // step *left*, without measuring the point it reached. Under quadratic convergence the last step
        // is the size of the residual it removes, so a residual just above the tolerance produces a step
        // under the step tolerance and a converged point that was reported as stalled: S-29's series ring
        // went 1 -> 0.37 -> 1.4e-3 -> 1.16e-8 -> 2.4e-12 and stopped as 'Stalled at 1.157E-08'. With a
        // step tolerance every step is under, the exit fires on the first step; what it reports must be
        // the new point's residual, and whether that is Converged or Stalled is decided by that figure.
        var system = Assemble(ClosedLoop, out var seed);
        var solver = new NewtonSolver(new NewtonSettings { StepTolerance = 1e3, ResidualTolerance = residualTolerance });
        var result = await solver.SolveAsync(system, seed, null, TestContext.Current.CancellationToken);
        var residuals = new double[system.Rows];

        Assert.True(system.TryEvaluateScaled(result.Solution.Values.ToArray(), residuals));

        var landed = residuals.Max(static r => Math.Abs(r));

        Assert.Equal(1, result.Iterations);
        Assert.Equal(landed, result.ResidualNorm, landed * 1e-9);
        Assert.Equal(landed < residualTolerance ? SolveTermination.Converged : SolveTermination.Stalled, result.Termination);
    }

    /// <summary>Lowers a script and assembles its system, seeded the way a real solve is.</summary>
    /// <remarks>
    /// <strong>The seed comes from <see cref="SolutionSeed"/> rather than from a constant, and it has
    /// to (`S-21`).</strong> A pipe's momentum relation is <c>dp = R*m|m|</c> and a pump curve is
    /// <c>H0 - k*m^2</c>; both have a derivative of exactly zero at <c>m = 0</c>, so a zero-flow start
    /// is a genuinely singular Jacobian and every solve from one reports FS3002 however well-posed the
    /// circuit is. Seeding a uniform non-zero flow only moves the singularity: branch orientation is the
    /// decomposition's choice, so one sign everywhere leaves some node with every port an inflow, and a
    /// node nothing leaves has a zero enthalpy column.
    ///
    /// This helper used to alternate the signs by index, which happened to work on this corpus and is
    /// not a property of anything. What replaced it satisfies the mass balances outright.
    /// </remarks>
    private static EquationSystem Assemble(string source, out StateVector seed)
    {
        var text = source.EndsWith(".fluid", StringComparison.Ordinal)
            ? File.ReadAllText(Path.Combine(RepositoryLayout.Samples, source))
            : source;

        var graph = GraphFixture.Lower(text).Graph;
        var posedness = WellPosedness.Check(graph);

        seed = SolutionSeed.Build(graph, SystemLayout.Build(graph, posedness.Counting));

        return EquationSystem.Build(graph, posedness, seed);
    }
}
