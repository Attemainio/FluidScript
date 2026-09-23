namespace FluidScript.Core.Topology.Construction;

/// <summary>One scheduled change of a component parameter, as the run applies it (<c>33</c>).</summary>
/// <param name="Component">The component whose parameter changes.</param>
/// <param name="Parameter">The parameter's key, as the registry resolves it: <c>power</c>, <c>position</c>.</param>
/// <param name="From">s from t = 0 when the change starts.</param>
/// <param name="To">s when it ends; equal to <paramref name="From"/> for a step.</param>
/// <param name="FromValue">
/// The parameter's SI value at <paramref name="From"/> for a ramp, or <see langword="null"/> for a step,
/// which leaves the value where it was until <paramref name="To"/> and then sets it.
/// </param>
/// <param name="ToValue">The parameter's SI value at <paramref name="To"/>: the whole change for a step.</param>
/// <remarks>
/// <para>
/// A step is applied at its instant and a ramp is linear between its ends; the integrator lands a
/// step boundary on every <paramref name="From"/> and <paramref name="To"/> so no derivative
/// evaluation straddles one (<c>33</c> §The step, once). A scheduled parameter is stated from t = 0 at
/// its design value: scheduling a sized or promoted parameter freezes it there and then moves it
/// (<c>D-140</c>).
/// </para>
/// <para>
/// Times and values are SI here and nowhere else on the graph carries a unit, so the frame and the
/// wire convert once at their edge (<c>D-07</c>).
/// </para>
/// </remarks>
public sealed record ScheduledChange(
    string Component,
    string Parameter,
    double From,
    double To,
    double? FromValue,
    double ToValue);
