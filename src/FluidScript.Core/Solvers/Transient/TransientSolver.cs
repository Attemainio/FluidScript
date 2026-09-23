using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Construction;

namespace FluidScript.Core.Solvers.Transient;

/// <summary>Heun's method on the differential states, the steady Newton on everything else (<c>33</c>).</summary>
/// <param name="solver">The algebraic solver every step calls, warm-started from the step before.</param>
/// <remarks>
/// <para>
/// The step is <c>33</c> §The step, once: clip the step to the CFL limit, the next scheduled time and
/// the next frame time; <c>k1</c> from the balances at the current state; predictor; <c>k2</c> from
/// the balances at the predictor; corrector; the local error is their difference, scaled by the
/// enthalpy scale; a rejected step halves and retries. Controllers are stepped before <c>k1</c> once
/// P6.3 builds them; until then every promotion holds its design value (<c>D-140</c>).
/// </para>
/// <para>
/// A derivative is the energy balance the pin overwrote, divided by the state's reference mass
/// (<see cref="EquationSystem.TryEvaluateRates"/>): the same evaluation the algebraic rows use, so the
/// integrator and the constraints cannot disagree about a flow or an arriving enthalpy. The drift
/// accumulator (<c>FS3106</c>) integrates the injected power and the boundary streams with the same
/// Heun weights and never the balances it checks, which is what makes it a check.
/// </para>
/// <para>
/// The run builds its own <see cref="EquationSystem"/> from the snapshot rather than pinning the
/// snapshot's, so two runs of one snapshot do not share a mutable system.
/// </para>
/// </remarks>
public sealed class TransientSolver(ISolver solver) : ITransientSolver
{
    private readonly ISolver _solver = solver ?? throw new ArgumentNullException(nameof(solver));

    /// <inheritdoc/>
    public async IAsyncEnumerable<TransientFrame> RunAsync(
        RunSnapshot snapshot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = snapshot.Settings;
        var system = EquationSystem.Build(snapshot.Graph, snapshot.Posedness, snapshot.Initial);
        var layout = system.Unknowns;
        var count = layout.Differential.Length;
        var masses = snapshot.ReferenceMasses;

        var x = snapshot.DifferentialInitial.ToArray();
        var predictor = new double[count];
        var k1 = new double[count];
        var k2 = new double[count];
        var inflows = new double[count];
        var atFrame = (double[])x.Clone();

        // The last state whose algebraic solve converged, paired with that solve: what a failing run
        // emits as its final frame, so a differential state is never shown beside flows from another
        // instant (33 invariant 10a).
        var verified = (double[])x.Clone();
        var verifiedTime = 0.0;

        for (var index = 0; index < snapshot.PromotionInitial.Length; index++)
        {
            system.Freeze(index, snapshot.PromotionInitial[index]);
        }

        system.SetLayerMasses(masses.AsSpan());

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var time = 0.0;
        var sequence = 0L;
        var stepsSinceFrame = 0;
        var smallestStep = double.PositiveInfinity;
        var drift = 0.0;
        var settledFrames = 0;
        var limiter = -1;
        var drifted = false;

        // t = 0: the design state, with the schedule's t = 0 entries applied and the tank profile pinned.
        system.Pin(x);
        Apply(system, snapshot.Schedule, time, inclusive: true);

        var solve = await _solver.SolveAsync(system, snapshot.Initial, null, cancellationToken).ConfigureAwait(false);

        if (!solve.Converged)
        {
            diagnostics.Add(NotBalanced(time, solve));
            yield return Frame(snapshot, sequence, time, solve.Solution, x, diagnostics, 0, 0, false, 0);
            yield break;
        }

        var algebraic = solve.Solution;
        var (injected1, boundary1) = Rates(system, algebraic, masses, k1, inflows);
        var stored0 = Stored(masses, x);
        var accumulated = 0.0;
        var absolute = 0.0;
        var cfl = Cfl(masses, inflows, settings.CflSafety, out var limiting);

        yield return Frame(snapshot, sequence, time, algebraic, x, diagnostics, 0, 0, false, 0);
        diagnostics.Clear();

        var step = Math.Min(Math.Min(settings.MaxStep, cfl), settings.FrameInterval);

        while (time < settings.Horizon)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextFrame = (sequence + 1) * settings.FrameInterval;
            var nextEvent = NextEvent(snapshot.Schedule, time);
            var target = Math.Min(Math.Min(nextFrame, nextEvent), settings.Horizon);
            var landing = step >= target - time;
            var tried = landing ? target - time : step;
            var arrival = landing ? target : time + tried;

            // Predictor, solved at the far end of the step with the schedule's left limit there: a
            // change scheduled exactly at the arrival belongs to the next step, never to this one's k2.
            for (var index = 0; index < count; index++)
            {
                predictor[index] = x[index] + (tried * k1[index]);
            }

            system.Pin(predictor);
            Apply(system, snapshot.Schedule, arrival, inclusive: false);

            solve = await _solver.SolveAsync(system, algebraic, null, cancellationToken).ConfigureAwait(false);

            if (!solve.Converged)
            {
                if (tried / 2 >= settings.MinStep)
                {
                    step = tried / 2;
                    continue;
                }

                diagnostics.Add(NotBalanced(arrival, solve));
                yield return Frame(snapshot, sequence + 1, verifiedTime, algebraic, verified, diagnostics, Math.Min(smallestStep, tried), stepsSinceFrame, false, drift);
                yield break;
            }

            var (injected2, boundary2) = Rates(system, solve.Solution, masses, k2, inflows);
            var error = 0.0;

            for (var index = 0; index < count; index++)
            {
                error = Math.Max(error, Math.Abs(tried / 2 * (k2[index] - k1[index])) / Tolerances.EnthalpyScale);
            }

            if (error > settings.LocalErrorTolerance)
            {
                if (tried / 2 >= settings.MinStep)
                {
                    step = tried / 2;
                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    TransientDiagnostics.StepTooSmall,
                    null,
                    new DiagnosticArgument("time", Seconds(time))));
                yield return Frame(snapshot, sequence + 1, verifiedTime, algebraic, verified, diagnostics, Math.Min(smallestStep, tried), stepsSinceFrame, false, drift);
                yield break;
            }

            // Accepted.
            for (var index = 0; index < count; index++)
            {
                x[index] += tried / 2 * (k1[index] + k2[index]);
            }

            accumulated += tried / 2 * (injected1 + boundary1 + injected2 + boundary2);
            absolute += tried / 2 * (Math.Abs(injected1) + Math.Abs(boundary1) + Math.Abs(injected2) + Math.Abs(boundary2));
            time = arrival;
            smallestStep = Math.Min(smallestStep, tried);
            stepsSinceFrame++;

            if (Array.Exists(x, static value => !double.IsFinite(value)))
            {
                diagnostics.Add(Invariant(time, "a finite state", sequence));
                yield return Frame(snapshot, sequence + 1, verifiedTime, algebraic, verified, diagnostics, Math.Min(smallestStep, tried), stepsSinceFrame, false, drift);
                yield break;
            }

            // Natural convection, applied as a correction rather than a flux: it turns a stack over in
            // seconds, far faster than any step here (33 §Stratified tank). It conserves mass and
            // `Σ mh` exactly, so the drift accumulator below is blind to it, which is the check that
            // it is doing what it claims.
            if (system.Remix(x, out _) is { } stirred)
            {
                diagnostics.Add(Invariant(time, $"a layer of '{stirred}' inside the property domain", sequence));
                yield return Frame(snapshot, sequence + 1, verifiedTime, algebraic, verified, diagnostics, Math.Min(smallestStep, tried), stepsSinceFrame, false, drift);
                yield break;
            }

            // The accepted state, with the schedule's right limit: a change at this instant applies
            // now, so the frame here shows the plant after it. This solve is the next step's k1.
            system.Pin(x);
            Apply(system, snapshot.Schedule, time, inclusive: true);

            solve = await _solver.SolveAsync(system, solve.Solution, null, cancellationToken).ConfigureAwait(false);

            if (!solve.Converged)
            {
                diagnostics.Add(NotBalanced(time, solve));
                yield return Frame(snapshot, sequence + 1, verifiedTime, algebraic, verified, diagnostics, Math.Min(smallestStep, tried), stepsSinceFrame, false, drift);
                yield break;
            }

            algebraic = solve.Solution;
            Array.Copy(x, verified, count);
            verifiedTime = time;
            (injected1, boundary1) = Rates(system, algebraic, masses, k1, inflows);
            cfl = Cfl(masses, inflows, settings.CflSafety, out limiting);

            if (cfl < settings.FrameInterval && limiting != limiter)
            {
                limiter = limiting;
                diagnostics.Add(Diagnostic.Create(
                    TransientDiagnostics.StepLimited,
                    null,
                    new DiagnosticArgument("step", Seconds(cfl)),
                    new DiagnosticArgument("component", layout.Differential[limiting].Name)));
            }

            drift = Math.Abs(Stored(masses, x) - stored0 - accumulated) / Math.Max(absolute, Math.Abs(stored0));

            if (drift >= Tolerances.TransientEnergyDriftFail)
            {
                diagnostics.Add(Invariant(time, $"the energy balance ({drift * 100:0.##} % drift)", sequence));
                yield return Frame(snapshot, sequence + 1, verifiedTime, algebraic, verified, diagnostics, Math.Min(smallestStep, tried), stepsSinceFrame, false, drift);
                yield break;
            }

            if (drift >= Tolerances.TransientEnergyDriftWarn && !drifted)
            {
                drifted = true;
                diagnostics.Add(Diagnostic.Create(
                    TransientDiagnostics.EnergyDrift,
                    null,
                    new DiagnosticArgument("pct", (drift * 100).ToString("0.##", CultureInfo.InvariantCulture))));
            }

            // Next step: the controller of 33 §Adaptive control, capped by stability and the frame.
            var growth = Math.Clamp(0.9 * Math.Sqrt(settings.LocalErrorTolerance / Math.Max(error, 1e-16)), 0.5, 2.0);

            step = Math.Min(Math.Min(tried * growth, settings.MaxStep), cfl);

            if (time >= nextFrame)
            {
                sequence++;

                var moved = 0.0;

                for (var index = 0; index < count; index++)
                {
                    moved = Math.Max(moved, Math.Abs(x[index] - atFrame[index]) / Tolerances.EnthalpyScale);
                }

                settledFrames = moved < Tolerances.TransientSettleTol ? settledFrames + 1 : 0;
                Array.Copy(x, atFrame, count);

                var settled = settledFrames >= Tolerances.TransientSettleFrames;

                if (time >= settings.Horizon && !settled)
                {
                    diagnostics.Add(Diagnostic.Create(
                        TransientDiagnostics.NotSettled,
                        null,
                        new DiagnosticArgument("horizon", Seconds(settings.Horizon))));
                }

                yield return Frame(snapshot, sequence, time, algebraic, x, diagnostics, smallestStep, stepsSinceFrame, settled, drift);
                diagnostics.Clear();
                stepsSinceFrame = 0;
                smallestStep = double.PositiveInfinity;
            }
        }
    }

    /// <summary>Reads every balance at a solved pinned state and turns it into a rate.</summary>
    /// <returns>The injected power and the boundary enthalpy flow at that state, W.</returns>
    private static (double Injected, double Boundary) Rates(
        EquationSystem system,
        StateVector solution,
        ImmutableArray<double> masses,
        double[] rates,
        double[] inflows)
    {
        if (!system.TryEvaluateRates(solution.Values.AsSpan(), rates, inflows, out var injected, out var boundary))
        {
            Array.Fill(rates, double.NaN);
            return (double.NaN, double.NaN);
        }

        for (var index = 0; index < rates.Length; index++)
        {
            rates[index] /= masses[index];
        }

        return (injected, boundary);
    }

    /// <summary>The CFL limit: the safety factor times the shortest residence time among the states with inflow.</summary>
    private static double Cfl(ImmutableArray<double> masses, double[] inflows, double safety, out int limiting)
    {
        var limit = double.PositiveInfinity;

        limiting = -1;

        for (var index = 0; index < inflows.Length; index++)
        {
            if (inflows[index] > 0 && masses[index] / inflows[index] < limit)
            {
                limit = masses[index] / inflows[index];
                limiting = index;
            }
        }

        return safety * limit;
    }

    private static double Stored(ImmutableArray<double> masses, double[] states)
    {
        var stored = 0.0;

        for (var index = 0; index < states.Length; index++)
        {
            stored += masses[index] * states[index];
        }

        return stored;
    }

    /// <summary>The earliest scheduled edge strictly after a time, or infinity.</summary>
    private static double NextEvent(ImmutableArray<ScheduledChange> schedule, double time)
    {
        var next = double.PositiveInfinity;

        foreach (var change in schedule)
        {
            if (change.From > time)
            {
                next = Math.Min(next, change.From);
            }

            if (change.To > time)
            {
                next = Math.Min(next, change.To);
            }
        }

        return next;
    }

    /// <summary>The steady system after a run's schedule has finished: every promotion frozen at its design value and every scheduled change applied (<c>33</c> invariant 8).</summary>
    /// <param name="snapshot">The run's snapshot.</param>
    /// <param name="time">s from t = 0; the horizon, for the state a settled run must match.</param>
    /// <returns>A fresh system, frozen but not pinned, whose Newton solution is the post-step equilibrium.</returns>
    public static EquationSystem SteadyAt(RunSnapshot snapshot, double time)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var system = EquationSystem.Build(snapshot.Graph, snapshot.Posedness, snapshot.Initial);

        for (var index = 0; index < snapshot.PromotionInitial.Length; index++)
        {
            system.Freeze(index, snapshot.PromotionInitial[index]);
        }

        Apply(system, snapshot.Schedule, time, inclusive: true);

        return system;
    }

    /// <summary>Writes every scheduled value as it stands at a time (<c>33</c> §Disturbances).</summary>
    /// <param name="system">The system the values are written into.</param>
    /// <param name="schedule">The run's schedule.</param>
    /// <param name="time">s from t = 0.</param>
    /// <param name="inclusive">
    /// Whether a change scheduled exactly at <paramref name="time"/> has happened: the right limit, for
    /// the state a frame shows; the left limit, for the far end of a step, so no step straddles one.
    /// </param>
    private static void Apply(EquationSystem system, ImmutableArray<ScheduledChange> schedule, double time, bool inclusive)
    {
        foreach (var change in schedule)
        {
            if (change.FromValue is not { } start)
            {
                // A step: nothing until its instant, the value from then on.
                if (time > change.To || (inclusive && time >= change.To))
                {
                    system.Schedule(change.Component, change.Parameter, change.ToValue);
                }

                continue;
            }

            if (!(time > change.From || (inclusive && time >= change.From)))
            {
                continue;
            }

            var span = change.To - change.From;
            var fraction = span > 0 ? Math.Clamp((time - change.From) / span, 0, 1) : 1;

            system.Schedule(change.Component, change.Parameter, start + (fraction * (change.ToValue - start)));
        }
    }

    private static TransientFrame Frame(
        RunSnapshot snapshot,
        long sequence,
        double time,
        StateVector state,
        double[] differential,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        double stepTaken,
        int steps,
        bool settled,
        double drift) => new()
        {
            SnapshotId = snapshot.SnapshotId,
            Sequence = sequence,
            Time = time,
            State = state,
            Differential = [.. differential],
            Diagnostics = diagnostics.ToImmutable(),
            StepTaken = double.IsFinite(stepTaken) ? stepTaken : 0,
            Steps = steps,
            Settled = settled,
            EnergyDrift = drift,
        };

    private static Diagnostic NotBalanced(double time, SolveResult solve) => Diagnostic.Create(
        TransientDiagnostics.StepNotBalanced,
        null,
        new DiagnosticArgument("time", Seconds(time)),
        new DiagnosticArgument("inner", solve.Termination.ToString()));

    private static Diagnostic Invariant(double time, string invariant, long sequence) => Diagnostic.Create(
        TransientDiagnostics.InvariantFailed,
        null,
        new DiagnosticArgument("time", Seconds(time)),
        new DiagnosticArgument("invariant", invariant),
        new DiagnosticArgument("sequence", sequence.ToString(CultureInfo.InvariantCulture)));

    private static string Seconds(double time) => time.ToString("0.###", CultureInfo.InvariantCulture);
}
