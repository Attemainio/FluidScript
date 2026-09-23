namespace FluidScript.Core.Components.Exchangers;

/// <summary>Which of the three exchanger modes lowering resolved.</summary>
/// <remarks>
/// <strong>There is no script <c>mode=</c> parameter.</strong> The mode is computed from what the
/// script connected and stated, in a fixed precedence, so that adding real connections to an
/// external-profile design has one predictable meaning (<c>D-19</c>).
/// </remarks>
public enum ExchangerMode
{
    /// <summary>A stated duty crossing the model boundary. No area, effectiveness or approach claim.</summary>
    Duty,

    /// <summary>Side 2 is an external stated or sized boundary profile, not a graph branch.</summary>
    Rated,

    /// <summary>Both sides are solved hydraulic streams, coupled by ε-NTU.</summary>
    Coupled,
}
