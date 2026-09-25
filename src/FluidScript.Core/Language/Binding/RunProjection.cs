using System.Collections.Immutable;

using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;

namespace FluidScript.Core.Language.Binding;

/// <summary>Turns a bound model and one of its runs into the model that run solves (<c>D-169</c>).</summary>
/// <remarks>
/// <para>
/// What <see cref="ScenarioProjection"/> is to a case, this is to a run, and it goes through it: the run starts
/// from its <c>from</c> case's steady solve. Then the run's own settings replace the file's: every circuit is
/// dynamic but those it holds <c>steady</c>, its events are the schedule, its <c>start</c> is the clock's, and
/// a driver it hands to a curve of time re-points every curve of that driver at that curve.
/// </para>
/// <para>
/// Lowering, the design solve and the transient then run on an ordinary model, and none of them learns that a
/// file can hold several runs — which is why the run is projected here rather than read at each of them.
/// </para>
/// </remarks>
public static class RunProjection
{
    /// <summary>Projects a model onto one of its runs.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="run">One of <see cref="SemanticModel.Runs"/>.</param>
    /// <returns>The model the run solves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="run"/> is <see langword="null"/>.</exception>
    public static SemanticModel Project(SemanticModel model, RunSymbol run)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(run);

        var projected = run.From is { } from && from < model.Project.Scenarios.Length
            ? ScenarioProjection.Project(model, from)
            : model;

        var steady = run.Steady.ToHashSet(StringComparer.Ordinal);

        return projected with
        {
            Circuits = [.. projected.Circuits.Select(circuit => circuit with { Mode = steady.Contains(circuit.Name) ? FluidMode.Static : FluidMode.Dynamic })],
            Project = projected.Project with { Start = run.Start },
            Disturbances = run.Events,
            Curves = [.. projected.Curves.Select(curve => Repointed(curve, run.DriverCurves))],
        };
    }

    /// <summary>A curve of a driver the run hands to a curve of time, driven by that curve instead.</summary>
    private static CurveSymbol Repointed(CurveSymbol curve, ImmutableDictionary<string, string> drivers) =>
        curve is { DriverKind: CurveDriverKind.Let, DriverName: { } driver } && drivers.TryGetValue(driver, out var clocked)
            ? curve with { DriverKind = CurveDriverKind.Curve, DriverName = clocked }
            : curve;
}
