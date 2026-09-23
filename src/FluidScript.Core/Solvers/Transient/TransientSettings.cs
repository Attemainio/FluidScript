namespace FluidScript.Core.Solvers.Transient;

/// <summary>What a run is asked for: how long, how often a frame, and the integrator's bounds (<c>33</c>).</summary>
/// <remarks>
/// The horizon and the frame interval come from the <c>start</c> message, not the script (<c>43</c>):
/// a stated exception to P5, because a run's length is a question about this viewing and not about
/// the plant. The step bounds default to <c>36</c>'s tolerance table and are exposed for a test that
/// needs a coarser or finer integrator, never to a user.
/// </remarks>
public sealed record TransientSettings
{
    /// <summary>Simulated duration.</summary>
    /// <value>s. Default 600.</value>
    public double Horizon { get; init; } = 600;

    /// <summary>Simulated time between emitted frames.</summary>
    /// <value>s. Default 1.</value>
    public double FrameInterval { get; init; } = 1.0;

    /// <summary>The longest integration step, whatever stability would allow.</summary>
    /// <value>s. <c>transient.max_step</c>.</value>
    public double MaxStep { get; init; } = Tolerances.TransientMaxStep;

    /// <summary>The shortest step before the run stops with <c>FS3102</c>.</summary>
    /// <value>s. <c>transient.min_step</c>.</value>
    public double MinStep { get; init; } = Tolerances.TransientMinStep;

    /// <summary>The margin the step keeps below the CFL limit.</summary>
    /// <value>Dimensionless. <c>transient.cfl_safety</c>.</value>
    public double CflSafety { get; init; } = Tolerances.TransientCflSafety;

    /// <summary>The scaled per-step local error a step must stay under.</summary>
    /// <value>Dimensionless. <c>transient.local_error_tol</c>.</value>
    public double LocalErrorTolerance { get; init; } = Tolerances.TransientLocalError;
}
