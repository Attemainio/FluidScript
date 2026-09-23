namespace FluidScript.Core.Solvers.Results;

/// <summary>What determined a pump's head at the solution.</summary>
public enum PumpHeadBasis
{
    /// <summary>The pump's curve at the solved flow, with a stated or sized shut-off head.</summary>
    Curve,

    /// <summary>The solver found it, to hold a stated constraint (<c>23</c>'s promotion).</summary>
    Promoted,

    /// <summary>A stated <c>dp</c>, flat in flow (<c>C-109</c>).</summary>
    StatedRise,
}
