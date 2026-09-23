using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>What deriving layout hints has to say: the <c>FS24xx</c> range (<c>25</c>).</summary>
/// <remarks>
/// Hints are advisory, so nothing here is an error: a hint that cannot be computed is omitted. The
/// three codes exist because the user can see each consequence on the canvas and would otherwise
/// wonder why.
/// </remarks>
public static class LayoutDiagnostics
{
    /// <summary>The graph has no clean topological order, which is always true of a closed loop.</summary>
    /// <value><c>FS2401</c>, informational and suppressed by default.</value>
    public static DiagnosticDescriptor NoTopologicalOrder { get; } = new(
        "FS2401",
        DiagnosticSeverity.Info,
        "The circuit closes on itself, so components are ordered by a depth-first walk from the pressure datum.");

    /// <summary>A group is large enough, or the scene is, that it will render collapsed.</summary>
    /// <value><c>FS2402</c>, informational.</value>
    public static DiagnosticDescriptor RendersCollapsed { get; } = new(
        "FS2402",
        DiagnosticSeverity.Info,
        "'{group}' has {count} members and will start collapsed; expand it on the canvas to see them.");

    /// <summary>A circuit's role contradicts its stated duty direction; the duty is used.</summary>
    /// <value><c>FS2403</c>, informational.</value>
    /// <remarks>
    /// A role is evidence and never an override (<c>D-35</c>): a circuit named <c>radiators</c> whose
    /// duties all give heat away is a source, and is placed as one.
    /// </remarks>
    public static DiagnosticDescriptor RoleContradictsDuty { get; } = new(
        "FS2403",
        DiagnosticSeverity.Info,
        "'{circuit}' is named as a {role} circuit but its stated duties make it a {stage}; the duties decide where it is drawn.");

    /// <summary>The solved layout breaks one of its own hard rules (<c>28</c> B); the drawing is unreliable where it says.</summary>
    /// <value><c>FS5002</c>, a warning (<c>53</c> error cases).</value>
    /// <remarks>
    /// The audit that the ladder's gates run over fixtures runs over every solved layout (<c>C-101</c>):
    /// a rule the router holds by construction is only as good as the shapes it was built against, and
    /// a picture that breaks the standard must say so rather than arrive as if correct.
    /// </remarks>
    public static DiagnosticDescriptor LayoutBreach { get; } = new(
        "FS5002",
        DiagnosticSeverity.Warning,
        "The drawing breaks its own {rule} rule between '{first}' and '{second}' ({detail}); the picture is unreliable there.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        NoTopologicalOrder,
        RendersCollapsed,
        RoleContradictsDuty,
        LayoutBreach,
    ];
}
