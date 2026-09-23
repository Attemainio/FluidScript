namespace FluidScript.Core.Sizing.Flows;

/// <summary>What determined a branch's flow estimate.</summary>
/// <remarks>
/// Ordered by authority, weakest first: a later basis overrides an earlier one, and the enum's order
/// is what <see cref="BranchFlows"/> compares. Extending it means deciding where the new rule ranks.
/// </remarks>
public enum FlowBasis
{
    /// <summary>Nothing determined it, so the estimate is <see cref="BranchFlows.Nominal"/>.</summary>
    /// <remarks>This is the case <c>24</c>'s <c>FS2304</c> reports once a sizer asks for the number.</remarks>
    Nominal = 0,
    /// <summary>A junction element the branch reaches shares its estimate.</summary>
    Propagated = 1,

    /// <summary>A three-way valve's leg, partitioned from a common leg a duty fixed; the two legs sum to that duty.</summary>
    /// <remarks>Above <see cref="Propagated"/> because the seed's forest keeps its strongest estimates as chords (<c>S-68</c>): two partitioned legs kept, and the coil they sum to comes out at its rating.</remarks>
    Partitioned = 2,

    /// <summary>An exchanger's stated duty and terminal temperatures fix it (<c>24</c>, step 1).</summary>
    Duty = 3,

    /// <summary>The script stated a flow on the branch.</summary>
    Stated = 4,
}
