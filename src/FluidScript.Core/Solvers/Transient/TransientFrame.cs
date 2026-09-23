using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Solvers;

/// <summary>One solved instant of a run (<c>33</c> §Frame production).</summary>
/// <remarks>
/// Every frame is a solved state: the step lands on every frame time (<c>33</c> invariant 10b), so
/// nothing here is interpolated. A frame at a scheduled time carries the state after the change.
/// </remarks>
public sealed record TransientFrame
{
    /// <summary>The run this frame belongs to.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>The frame's position in the run, from 0 at t = 0.</summary>
    public required long Sequence { get; init; }

    /// <summary>Simulated time from t = 0.</summary>
    /// <value>s.</value>
    public required double Time { get; init; }

    /// <summary>Every algebraic unknown at this instant: flows, pressures, enthalpies, sizes and promotions.</summary>
    public required StateVector State { get; init; }

    /// <summary>Every differential state at this instant, in <see cref="SystemLayout.Differential"/> order.</summary>
    /// <value>J/kg. A pipe cell's value is also in <see cref="State"/> at its column; a tank layer's is only here.</value>
    public required ImmutableArray<double> Differential { get; init; }

    /// <summary>Diagnostics raised by the steps since the previous frame, and at the horizon what the run has to say about the whole.</summary>
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }

    /// <summary>The smallest integration step accepted since the previous frame.</summary>
    /// <value>s. What a slow run is diagnosed from: the step the error control or the CFL limit forced.</value>
    public required double StepTaken { get; init; }

    /// <summary>The energy drift so far: stored energy against the integrated heat and boundary flows (<c>33</c> §Error cases).</summary>
    /// <value>Relative, dimensionless. <c>FS3106</c> warns at <c>transient.energy_drift_tol</c>.</value>
    public required double EnergyDrift { get; init; }

    /// <summary>How many steps were accepted since the previous frame.</summary>
    public required int Steps { get; init; }

    /// <summary>Whether every differential state has stopped moving, by <c>36</c>'s settle rule.</summary>
    public required bool Settled { get; init; }
}
