using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Primitives;
using FluidScript.Core.Sizing.Sizers;
using FluidScript.Core.Solvers.Passes;

namespace FluidScript.Core.Sizing.Scenarios;

/// <summary>Sizes one plant for every scenario it must work in (<c>D-143</c>, <c>24</c>'s four steps).</summary>
/// <remarks>
/// <para>
/// <strong>Sequentially</strong> (the user's call, 2026-09-22). A whole-pipeline solve is 7–17 ms
/// since <c>D-137</c>, and a scenario list is written by hand, so six cases are about 100 ms for both
/// passes — under <c>07</c>'s interactive budget with an order of magnitude to spare. <c>24</c> keeps
/// the chunked-worker design for the day the report says it is needed; the report carries the
/// per-scenario wall time so that stays a reading rather than an argument.
/// </para>
/// <para>
/// <strong>The merge is iterated, and whether it terminates is measured rather than asserted</strong>
/// (<c>08</c>'s P6.8 row). Sizes are coupled: a pipe one case enlarged lowers the resistance another
/// case's pump sees, which lowers the head that case asks for, which changes a valve's authority. So
/// round 2 re-sizes every case against the merged plant, and the rounds continue while the envelope
/// moves. It should settle because a capacity envelope only grows and the catalogues are finite — but
/// the head that falls when a pipe grows is exactly the term that could make it oscillate, so the
/// round count is reported and a cap is real rather than defensive.
/// </para>
/// </remarks>
public static class ScenarioSizing
{
    /// <summary>The most times the envelope is re-merged before the result is reported unsettled.</summary>
    /// <remarks>
    /// Small on purpose. A plant that has not settled in four rounds is not going to be talked into it
    /// by a fifth, and the honest answer is the envelope reached plus a note saying it was still
    /// moving — the same shape as the outer loop's own pass cap and its <c>FS2301</c>.
    /// </remarks>
    public const int MaxRounds = 4;

    /// <summary>Sizes a model for every scenario it declares, and solves each against the result.</summary>
    /// <param name="loop">The outer loop to solve with.</param>
    /// <param name="model">The bound model, carrying its scenario lists.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="name">The model's name, for diagnostics.</param>
    /// <param name="cancellationToken">Checked between scenarios, because a session re-sizes on every edit.</param>
    /// <returns>
    /// The merged plant and every case's state in it, or why no case could be solved. A file that
    /// declares no scenarios takes one ordinary run and reports it as a single case called
    /// <c>design</c>, so a caller needs no branch of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static async Task<Result<ScenarioSizingResult>> SizeAsync(
        OuterLoop loop,
        SemanticModel model,
        ISubstance substance,
        string name = "model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        var scenarios = model.Project.Scenarios;

        if (scenarios.IsEmpty)
        {
            return await SingleAsync(loop, model, substance, name, cancellationToken).ConfigureAwait(false);
        }

        var notes = ImmutableArray.CreateBuilder<string>();
        var merged = SizingOverlay.Empty;
        var governing = ImmutableDictionary<string, string>.Empty;
        var rounds = 0;
        var converged = false;

        // Steps 1 and 2, iterated. Round 1 sizes each case from the bootstrap; every round after it
        // sizes each case against the plant the merge built, which is what lets the coupling settle.
        while (rounds < MaxRounds)
        {
            rounds++;

            var candidates = new List<ScenarioEnvelope.Candidate>(scenarios.Length);

            for (var index = 0; index < scenarios.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var projected = ScenarioProjection.Project(model, index);
                var prepared = loop.Prepare(projected, substance, Named(name, scenarios[index]), rounds == 1 ? null : merged);
                var solved = await loop
                    .RunAsync(prepared, projected, substance, warmStart: null, Named(name, scenarios[index]), cancellationToken)
                    .ConfigureAwait(false);

                if (!solved.IsSuccess)
                {
                    return Result.Failure<ScenarioSizingResult>(solved.Error);
                }

                candidates.Add(new ScenarioEnvelope.Candidate(scenarios[index], solved.Value.Sizes));
            }

            var envelope = ScenarioEnvelope.Merge(candidates);

            if (!envelope.Unruled.IsEmpty)
            {
                // A sizer gained a parameter and nobody chose its envelope. Refused rather than
                // guessed: a maximum is right for a capacity and wrong for anything else, and the
                // wrong one produces a plant that looks sized.
                return Result.Failure<ScenarioSizingResult>(ResultError.From(
                    FluidScript.Core.Diagnostics.Descriptors.FluidDiagnostics.PropertyNotEvaluable,
                    ("property", "a merged size"),
                    ("name", name),
                    ("state", $"no envelope rule names {string.Join(", ", envelope.Unruled)}")));
            }

            governing = envelope.Governing;

            if (envelope.Sizes.Matches(merged))
            {
                merged = envelope.Sizes;
                converged = true;
                break;
            }

            merged = envelope.Sizes;
        }

        notes.Add(converged
            ? $"{scenarios.Length} scenarios merged in {rounds} round{(rounds == 1 ? string.Empty : "s")}"
            : $"{scenarios.Length} scenarios were still changing the envelope after {MaxRounds} rounds; the sizes are the last merge");

        // Step 3: every case against the merged plant, sizes frozen. None of the solves above is a
        // state of this plant — each used the sizes its own case asked for, and the merge exceeds them.
        var operating = ImmutableArray.CreateBuilder<ScenarioSolve>(scenarios.Length);

        for (var index = 0; index < scenarios.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projected = ScenarioProjection.Project(model, index);
            var scenarioName = Named(name, scenarios[index]);
            var clock = Stopwatch.StartNew();
            var solved = await loop
                .RunAsync(
                    loop.Freeze(projected, substance, merged, scenarioName),
                    projected,
                    substance,
                    warmStart: null,
                    scenarioName,
                    cancellationToken)
                .ConfigureAwait(false);

            clock.Stop();

            if (!solved.IsSuccess)
            {
                return Result.Failure<ScenarioSizingResult>(solved.Error);
            }

            operating.Add(new ScenarioSolve(scenarios[index], index, solved.Value, clock.Elapsed));
        }

        var solves = operating.MoveToImmutable();
        var (controlled, controlledBy, turnDown) = Controllability(solves, merged, governing, notes);

        return Result.Success(new ScenarioSizingResult(
            solves,
            controlled,
            controlledBy,
            rounds,
            converged,
            notes.ToImmutable())
        {
            DesignIndex = Math.Max(0, model.Project.DesignScenarioIndex),
            Said = Inert(solves, scenarios).AddRange(turnDown),
        });
    }

    /// <summary>Reports how well each control valve of the merged plant controls, across every case (<c>C-121</c>).</summary>
    /// <param name="solves">Each case against the merged plant, carrying its valve readings.</param>
    /// <param name="merged">The merged sizes, which carry no authority of their own.</param>
    /// <param name="governing">The case behind each merged size.</param>
    /// <param name="notes">Where <c>FS4006</c>'s note goes.</param>
    /// <returns>The sizes with each valve's authority, the case each was lowest in, and <c>FS4013</c> per valve that fails its turn-down.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The minimum across cases</strong> (the user's call, 2026-09-23). Every reading here is of
    /// the <em>same</em> plant, which is what makes a minimum honest now where it was not at the merge:
    /// there, each case's figure described a valve in pipework that was never built. Both drops go as
    /// ṁ², so on one geometry the cases agree to a percent or two — unless a stated value differs
    /// between them (an exchanger's <c>dp=[5, 60]</c>), which changes the branch per case and is
    /// exactly when the lowest is the one that matters.
    /// </para>
    /// <para>
    /// A stated <c>authority</c> is the script's target and stays what the report shows; the note
    /// still fires on the reading, as it does for a single run.
    /// </para>
    /// </remarks>
    private static (SizingOverlay Sizes, ImmutableDictionary<string, string> Governing, ImmutableArray<Diagnostics.Diagnostic> Said) Controllability(
        ImmutableArray<ScenarioSolve> solves,
        SizingOverlay merged,
        ImmutableDictionary<string, string> governing,
        ImmutableArray<string>.Builder notes)
    {
        var said = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();
        var valves = solves
            .SelectMany(static solve => solve.Result.Valves.Select(reading => (Case: solve.Name, Reading: reading, Given: solve.Result.Sizes)))
            .GroupBy(static read => read.Reading.Name, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal);

        foreach (var valve in valves)
        {
            var lowest = valve.MinBy(static read => read.Reading.Authority);
            var authority = lowest.Reading.Authority;

            if (lowest.Given.For(valve.Key, "authority") is not null)
            {
                merged = merged.With(valve.Key, "authority", FluidScript.Core.Physics.Units.Quantity.FromSi(authority, FluidScript.Core.Physics.Units.Dimension.Dimensionless));
                governing = governing.SetItem(Ownership.Key(valve.Key, "authority"), lowest.Case);
            }

            ValveSizer.Poor(valve.Key, authority, notes, lowest.Case);

            var light = valve.MinBy(static read => read.Reading.MassFlow);
            var heavy = valve.MaxBy(static read => read.Reading.MassFlow);

            if (valve.Count() < 2 || heavy.Reading.MassFlow <= 0)
            {
                continue;
            }

            var range = SizingDefaults.ValveRangeability(lowest.Reading.Characteristic);
            var limit = 1 / (range * Math.Sqrt(authority));
            var ratio = light.Reading.MassFlow / heavy.Reading.MassFlow;

            if (ratio >= limit)
            {
                continue;
            }

            said.Add(Diagnostics.Diagnostic.Create(
                FluidScript.Core.Diagnostics.Descriptors.DesignDiagnostics.TurnDownBeyondRange,
                span: null,
                new Diagnostics.DiagnosticArgument("name", valve.Key),
                new Diagnostics.DiagnosticArgument("light", Format(light.Reading.MassFlow, "0.###")),
                new Diagnostics.DiagnosticArgument("lightCase", light.Case),
                new Diagnostics.DiagnosticArgument("heavy", Format(heavy.Reading.MassFlow, "0.###")),
                new Diagnostics.DiagnosticArgument("heavyCase", heavy.Case),
                new Diagnostics.DiagnosticArgument("ratio", Format(ratio * 100, "0.#")),
                new Diagnostics.DiagnosticArgument("trim", Trim(lowest.Reading.Characteristic)),
                new Diagnostics.DiagnosticArgument("authority", Format(authority, "0.##")),
                new Diagnostics.DiagnosticArgument("limit", Format(limit * 100, "0.#")),
                new Diagnostics.DiagnosticArgument("range", Format(range, "0")))
                with
            { ComponentName = valve.Key });
        }

        return (merged, governing, said.ToImmutable());
    }

    /// <summary>Formats a number for a diagnostic argument, culture-invariant.</summary>
    /// <param name="value">The number.</param>
    /// <param name="format">The .NET format string.</param>
    /// <returns>The text.</returns>
    private static string Format(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Names a trim the way a datasheet does, with the article that opens <c>FS4013</c>'s second sentence.</summary>
    /// <param name="characteristic">The trim.</param>
    /// <returns>Its name in a sentence.</returns>
    private static string Trim(ValveCharacteristic characteristic) => characteristic switch
    {
        ValveCharacteristic.EqualPercentage => "An equal-percentage",
        ValveCharacteristic.QuickOpen => "A quick-opening",
        _ => "A linear",
    };

    /// <summary>Finds the components every declared case leaves with nothing to do (<c>FS2314</c>).</summary>
    /// <param name="solves">Each case against the merged plant.</param>
    /// <param name="scenarios">The declared case names.</param>
    /// <returns>One warning per inert component, in component-name order.</returns>
    /// <remarks>
    /// <para>
    /// <strong>This is the honest limit of a hand-written list, made visible.</strong> A list only
    /// checks what someone thought to name, and what that misses is not a component sized too small —
    /// it is one sized to nothing. `24`'s example: a recovery exchanger taking
    /// <c>min(Q_heat, Q_cool)</c> between a heating load that peaks in winter and a cooling load that
    /// peaks in summer is zero in both, and governed by a shoulder case nobody wrote.
    /// </para>
    /// <para>
    /// <strong>Scoped to duty-carrying components, deliberately.</strong> A zero duty is unambiguous
    /// and is the failure the case describes. A pipe or a valve that carries no flow in any case is a
    /// different observation — it still sizes, to the smallest row — and conflating the two would make
    /// this fire on every standby leg. The wider sweep is worth having and is not this.
    /// </para>
    /// <para>
    /// The other reason a component reads inert is that it is not needed at all, and the message does
    /// not pretend to tell the two apart: it reports the shape and names the likelier cause.
    /// </para>
    /// </remarks>
    private static ImmutableArray<Diagnostics.Diagnostic> Inert(
        ImmutableArray<ScenarioSolve> solves, ImmutableArray<string> scenarios)
    {
        if (solves.Length < 2)
        {
            // One case cannot be missing an interior one. A lone inert component is an observation
            // about the script rather than about the case list, and belongs to whatever reports that.
            return [];
        }

        var carried = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var solve in solves)
        {
            foreach (var component in solve.Result.Graph.Components)
            {
                if (component is not HeatExchangerComponent exchanger)
                {
                    continue;
                }

                var active = Math.Abs(exchanger.Power) > Solvers.Tolerances.FlowZero;

                carried[exchanger.Name] = carried.TryGetValue(exchanger.Name, out var before) ? before || active : active;
            }
        }

        var said = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        foreach (var (component, active) in carried.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (active)
            {
                continue;
            }

            said.Add(Diagnostics.Diagnostic.Create(
                FluidScript.Core.Diagnostics.Descriptors.SizingDiagnostics.InertInEveryScenario,
                span: null,
                new Diagnostics.DiagnosticArgument("name", component),
                new Diagnostics.DiagnosticArgument("count", scenarios.Length.ToString(CultureInfo.InvariantCulture)),
                new Diagnostics.DiagnosticArgument("names", string.Join(", ", scenarios)))
                with
            { ComponentName = component });
        }

        return said.ToImmutable();
    }

    /// <summary>Runs a file that declares no scenarios, reported as one case so a caller needs no branch.</summary>
    /// <param name="loop">The outer loop.</param>
    /// <param name="model">The bound model.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="name">The model's name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The single solve, wrapped.</returns>
    private static async Task<Result<ScenarioSizingResult>> SingleAsync(
        OuterLoop loop,
        SemanticModel model,
        ISubstance substance,
        string name,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var solved = await loop.RunAsync(model, substance, name, cancellationToken).ConfigureAwait(false);

        clock.Stop();

        return solved.IsSuccess
            ? Result.Success(new ScenarioSizingResult(
                [new ScenarioSolve("design", 0, solved.Value, clock.Elapsed)],
                solved.Value.Sizes,
                ImmutableDictionary<string, string>.Empty,
                Rounds: 1,
                Converged: true,
                Notes: []))
            : Result.Failure<ScenarioSizingResult>(solved.Error);
    }

    /// <summary>Names one scenario's model, so a diagnostic says which case it came from.</summary>
    /// <param name="name">The model's name.</param>
    /// <param name="scenario">The scenario's name.</param>
    /// <returns><c>name@scenario</c>.</returns>
    private static string Named(string name, string scenario) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}@{scenario}");
}
