using System.Collections.Immutable;

using FluidScript.Core.Physics.Units;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;

namespace FluidScript.Core.Language.Binding;

/// <summary>One component: a name, a kind, and the parameters the user actually wrote.</summary>
public sealed record ComponentSymbol
{
    /// <summary>Gets the user's identifier, or a generated one for an inferred component.</summary>
    public required string Name { get; init; }

    /// <summary>Gets how this component came to exist.</summary>
    /// <remarks>
    /// Carried as data rather than derived from a null span: the canvas must show which components the
    /// user wrote and which the language created, and write-back must refuse to edit one with no
    /// declaration.
    /// </remarks>
    public required Origin Origin { get; init; }

    /// <summary>Gets the resolved kind, or <see langword="null"/> when the kind name resolved to nothing.</summary>
    /// <value>
    /// Null is a normal result, not a failure: an unresolved kind still produces a component so later
    /// stages can skip it without the script collapsing.
    /// </value>
    public ComponentKindInfo? Kind { get; init; }

    /// <summary>Gets the kind exactly as the user wrote it.</summary>
    public required string WrittenKind { get; init; }

    /// <summary>Gets the parameter values, keyed by canonical parameter name.</summary>
    /// <value>
    /// A parameter the user did not write is <strong>absent from this map</strong> — it is not present
    /// with a default. The kind's omission policy then selects sizing or a visible default
    /// (<c>D-02</c>). Absence, never null, is what makes "stated" and "defaulted" distinguishable.
    /// </value>
    public required ImmutableDictionary<string, ParameterValue> Parameters { get; init; }

    /// <summary>Gets the source span of the declaration, or <see langword="null"/> for an inferred component.</summary>
    public TextSpan? DeclarationSpan { get; init; }

    /// <summary>Gets the name of the circuit this component was declared in (<c>D-33</c>).</summary>
    public required string CircuitName { get; init; }

    /// <summary>Gets the node this component observes, from its <c>at</c> clause (<c>D-61</c>).</summary>
    /// <value>
    /// <see langword="null"/> for everything that carries flow, which is everything but an instrument.
    /// An observer attaches to a node and stays out of the hydraulic graph entirely: a pass-through
    /// sensor would carry two ports, gain an inserted node from rule I2, and contribute equations that
    /// are all identities.
    /// </value>
    public string? AttachedTo { get; init; }

    /// <summary>Gets the ports that exist on this component.</summary>
    /// <value>
    /// The kind's fixed ports, plus any indexed port a qualified endpoint or a level parameter
    /// evidenced. A port nothing named does not exist here or in the model contract: a tank has
    /// sixteen possible inlets and however many the script actually used.
    /// </value>
    public ImmutableArray<string> Ports { get; init; } = [];

    /// <summary>Gets the derived equipment tag, such as <c>400PU01</c> (<c>D-34</c>).</summary>
    /// <value><see langword="null"/> for an inferred component, or a kind with no tag code.</value>
    /// <remarks>
    /// <strong>Metadata, never identity.</strong> <see cref="Name"/> is what every consumer keys on —
    /// selection, diagnostics, write-back, export. A tag changes whenever a declaration is inserted
    /// above this one; a name does not, and that difference is the whole content of <c>D-34</c>.
    /// Nothing may index by this field, and no binder stage may read it: it is computed last, from the
    /// finished declaration set, so a stage that read one would make identity circular.
    /// </remarks>
    public string? Tag { get; init; }

    /// <summary>Gets the style the declaration carries: the circuit's in force where it was written, then its own <c>style=</c> (<c>D-104</c>).</summary>
    /// <value><see langword="null"/> when nothing was stated; an inferred component has none and takes its neighbour's.</value>
    public StyleSpec? Style { get; init; }

    /// <summary>Gets the component's own sizing point, from its <c>sized_at</c> clause (<c>D-94</c>).</summary>
    /// <value>
    /// Empty for a component that reads every curve where the file's <c>design</c> line says. Keyed by
    /// canonical driver name like <see cref="ProjectSettings.Design"/>, with the value and the number
    /// a curve is read at filled in once evaluated. A component whose parameters read a curve at this
    /// point takes the curve's value there as its <em>capacity</em>: below the point it is flat out and
    /// the rest of the plant carries the remainder, which is what a bivalent heat pump is.
    /// </value>
    public ImmutableDictionary<string, DesignValue> SizingPoint { get; init; } =
        ImmutableDictionary<string, DesignValue>.Empty;

    /// <summary>Gets each parameter's capacity: its value at the component's own sizing point (<c>D-175</c>).</summary>
    /// <value>
    /// Keyed by canonical parameter name, for the parameters that read a curve on a component with a
    /// <see cref="SizingPoint"/>; empty otherwise. In the parameter's own dimension and SI unit, signed as its value
    /// is. The parameter is held to it in magnitude in every case and at every step of a run: a bivalent heat pump
    /// sized at −5 °C gives the curve's 27.2 kW on the design day and the whole of a mild day's 16.3 kW.
    /// </value>
    public ImmutableDictionary<string, Quantity> Capacities { get; init; } =
        ImmutableDictionary<string, Quantity>.Empty;
}
