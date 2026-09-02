using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything the substance layer can report.</summary>
/// <remarks>
/// <para>
/// The <c>FS20xx</c> range, whose subject is substances and properties
/// (<c>plan/20-core-domain/21-fluid-and-state.md</c>). Every one of these is <em>carried</em> by a
/// <c>Result</c> rather than emitted where it arises: a state request that fails during Newton
/// iteration is a normal step of a run that converges, and reporting it there would put hundreds of
/// errors in the log for a circuit that solved correctly. The caller that knows whether a state was a
/// trial point or the converged answer is the one that decides (<c>21</c>'s invariant 4).
/// </para>
/// <para>
/// One code this area owns is deliberately unregistered. <c>FS2005</c> (glycol concentration) needs
/// glycol, which is post-v1 by <c>D-28</c>: accepting a concentration before the mixture contract and
/// the freezing basis are validated would overstate the physics, and registering it would put a code
/// on the documentation page that nothing produces.
/// </para>
/// </remarks>
public static class FluidDiagnostics
{
    /// <summary>A <c>fluid</c> name no substance answers to.</summary>
    /// <value><c>FS2001</c>, an error.</value>
    public static DiagnosticDescriptor UnknownSubstance { get; } = new(
        "FS2001",
        DiagnosticSeverity.Error,
        "There is no fluid called '{name}'. Available: {list}.");

    /// <summary>A property pair that does not fix a state.</summary>
    /// <value><c>FS2002</c>, an error.</value>
    /// <remarks>
    /// Written off as unreachable and then measured: on the saturation line, pressure and temperature
    /// are <em>not</em> independent, and the backend says so — "Saturation pressure [101325 Pa]
    /// corresponding to T [373.124 K] is within 1e-4 % of given p [101325 Pa]". Water's validated
    /// domain contains that line, so the pair a script is most likely to write is the one that fails.
    /// </remarks>
    public static DiagnosticDescriptor PairDoesNotFixAState { get; } = new(
        "FS2002",
        DiagnosticSeverity.Error,
        "Cannot fix a state from {a} and {b}; they are not independent here.");

    /// <summary>A state outside the domain the substance's data is validated over.</summary>
    /// <value><c>FS2003</c>, an error.</value>
    /// <remarks>
    /// The check exists because the backend does not make it. Below the melting line CoolProp throws,
    /// which is at least loud; above the upper bound it returns a number, and water at 5000 °C comes
    /// back with a plausible density and no complaint. An extrapolated property is indistinguishable
    /// from a good one at the call site, so the range is enforced here or nowhere.
    /// </remarks>
    public static DiagnosticDescriptor StateOutsideValidRange { get; } = new(
        "FS2003",
        DiagnosticSeverity.Error,
        "{name} data covers {lo} to {hi}; this state is at {value}.");

    /// <summary>The property backend returned something that is not a finite number.</summary>
    /// <value><c>FS2004</c>, an error.</value>
    public static DiagnosticDescriptor PropertyNotEvaluable { get; } = new(
        "FS2004",
        DiagnosticSeverity.Error,
        "Could not evaluate {property} for {name} at {state}.");

    /// <summary>A relative humidity outside the 0 to 100 % a fraction can mean.</summary>
    /// <value><c>FS2006</c>, an error.</value>
    public static DiagnosticDescriptor RelativeHumidityOutOfRange { get; } = new(
        "FS2006",
        DiagnosticSeverity.Error,
        "Relative humidity must be between 0 and 100 %.");

    /// <summary>Gets every code the substance layer emits, for the registry to collect.</summary>
    /// <value>Five descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        UnknownSubstance,
        PairDoesNotFixAState,
        StateOutsideValidRange,
        PropertyNotEvaluable,
        RelativeHumidityOutOfRange,
    ];
}
