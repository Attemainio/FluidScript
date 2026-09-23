namespace FluidScript.Core.Physics.Fluids;

/// <summary>What phase a substance is in at a state.</summary>
public enum Phase
{
    /// <summary>The backend did not say, or the substance has no phase behaviour worth naming.</summary>
    Unknown = 0,

    /// <summary>Liquid — the only phase a v1 hydronic circuit is validated over.</summary>
    Liquid,

    /// <summary>Gas or vapour.</summary>
    Gas,

    /// <summary>Two phases at once.</summary>
    /// <remarks>
    /// Ordinary rather than exceptional since <c>D-78</c>: an expansion valve discharges into the dome
    /// by definition, and an evaporator is here over almost its whole length. Three of the seven
    /// properties do not exist at such a point and are blanked rather than guessed (<c>C-52</c>); what
    /// is always well posed is <c>(p, h)</c>, which is why the solver's node unknown is enthalpy.
    /// </remarks>
    TwoPhase,

    /// <summary>Above the critical point.</summary>
    Supercritical,

    /// <summary>Solid.</summary>
    Solid,
}
