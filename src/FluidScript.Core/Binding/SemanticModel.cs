using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>A bound script: every name resolved, every value typed, ready for lowering.</summary>
/// <remarks>
/// This is the stage boundary that matters most in the pipeline: above it nothing knows physics, below
/// it nothing knows a script existed.
/// </remarks>
public sealed record SemanticModel
{
    /// <summary>Gets every circuit in the script, in declaration order (<c>D-33</c>).</summary>
    /// <value>
    /// Never empty. A script with no <c>circuit</c> header binds a single implicit circuit named for
    /// the file, so consumers never special-case an empty collection.
    /// </value>
    public required ImmutableArray<CircuitSymbol> Circuits { get; init; }

    /// <summary>Gets the file-wide settings from the <c>project</c> directive (<c>D-37</c>).</summary>
    public required ProjectSettings Project { get; init; }

    /// <summary>Gets every component, declared and inferred, in declaration order.</summary>
    public required ImmutableArray<ComponentSymbol> Components { get; init; }

    /// <summary>Gets every <c>let</c> binding, in declaration order.</summary>
    public required ImmutableArray<BindingSymbol> Bindings { get; init; }

    /// <summary>Gets the presentation values Core carries and never interprets.</summary>
    public required StyleSettings Style { get; init; }

    /// <summary>Gets the expressions held until sizing or solving supplies their inputs.</summary>
    /// <value>
    /// Empty for a script whose every value is computable from literals, bindings and declared
    /// parameters. An entry here is not an error: <c>PU1 pump head=1.2*HE1.dp</c> is the design intent
    /// the deferral exists to support.
    /// </value>
    public required ImmutableArray<DeferredExpression> Deferred { get; init; }
}

/// <summary>One circuit and everything settled about it before topology.</summary>
public sealed record CircuitSymbol
{
    /// <summary>Gets the identifier written in the header, which is also the source of the role.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the circuit's designation, the leading part of every tag it owns.</summary>
    /// <value>
    /// As written, or resolved as the lowest unused multiple of 100 in declaration order when the
    /// header omitted it (<c>D-33</c>).
    /// </value>
    public required int Number { get; init; }

    /// <summary>Gets whether the number was written or resolved.</summary>
    /// <remarks>
    /// The printer needs this to reproduce the source byte for byte: printing a resolved number would
    /// rewrite <c>circuit coolingLoop</c> as <c>circuit coolingLoop 100</c> the first time anything
    /// touched the file.
    /// </remarks>
    public required bool NumberIsExplicit { get; init; }

    /// <summary>Gets the working fluid's name as written, or <see langword="null"/> when unstated.</summary>
    public string? Substance { get; init; }

    /// <summary>Gets whether this circuit is solved as an equilibrium or in time.</summary>
    /// <value>
    /// The circuit's own <c>fluid dynamic|static</c> wins; otherwise the project default; otherwise
    /// static (<c>D-37</c>). A circuit contradicting the project gets <c>FS1517</c> and keeps its own.
    /// </value>
    public required FluidMode Mode { get; init; }

    /// <summary>Gets the circuit's role, resolved from its name (<c>D-35</c>).</summary>
    /// <value>Neutral when the name matches no role — never an error.</value>
    public required CircuitRole Role { get; init; }

    /// <summary>Gets where the header sits in the source.</summary>
    /// <value>The whole file's span for the implicit circuit a headerless script gets.</value>
    public required TextSpan DeclarationSpan { get; init; }
}

/// <summary>A circuit's thermal classification, feeding <c>D-31</c>'s staging.</summary>
/// <param name="CanonicalName">The registered role name, or <c>neutral</c>.</param>
/// <param name="Stage">Where a circuit of this role sits in the thermal chain.</param>
/// <remarks>
/// Registry data, not a closed set in the language: adding a role is a registry change, never a
/// grammar change (<c>D-35</c>).
/// </remarks>
public sealed record CircuitRole(string CanonicalName, ThermalStageRole Stage);

/// <summary>Where a circuit sits in the chain from heat source to heat consumer.</summary>
public enum ThermalStageRole
{
    /// <summary>No classification — the honest answer for a name the registry does not know.</summary>
    Neutral = 0,

    /// <summary>Brings heat into the plant.</summary>
    Source,

    /// <summary>Moves heat between circuits, or changes its grade.</summary>
    Conversion,

    /// <summary>Holds heat over time.</summary>
    Storage,

    /// <summary>Takes useful heat out of the plant.</summary>
    Consumer,
}

/// <summary>File-wide settings from the <c>project</c> directive (<c>D-37</c>).</summary>
/// <param name="Name">The project's name, or <see langword="null"/> when no directive states one.</param>
/// <param name="DefaultMode">
/// The default solve mode for every circuit, or <see langword="null"/> when the directive states none.
/// </param>
/// <remarks>
/// Spacing is deliberately not here. <c>D-37</c> puts it in <see cref="StyleSettings"/>, and a second
/// home would create two paths for one value: the one that gets serialized and the one that does not.
/// </remarks>
public sealed record ProjectSettings(string? Name, FluidMode? DefaultMode);

/// <summary>Presentation values Core carries and never interprets.</summary>
/// <param name="Tokens">The <c>style</c> directives' positional tokens, verbatim and in order.</param>
/// <param name="Spacing">
/// The <c>spacing</c> directive's value in world units, or <see langword="null"/> when the script
/// states none, in which case the renderer's own default applies.
/// </param>
public sealed record StyleSettings(ImmutableArray<StyleTokenSyntax> Tokens, double? Spacing);

/// <summary>How a component came to exist.</summary>
public abstract record Origin
{
    private Origin()
    {
    }

    /// <summary>The user wrote a declaration for it.</summary>
    public sealed record Declared : Origin;

    /// <summary>An inference rule created it.</summary>
    /// <param name="Rule">The rule's id — <c>I1</c>, <c>I2</c> or <c>I3</c>.</param>
    /// <param name="StableKey">
    /// What the name is derived from, so the same script always produces the same name.
    /// </param>
    public sealed record Inferred(string Rule, string StableKey) : Origin;
}

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
}

/// <summary>A parameter the user supplied. Its mere presence is a constraint (<c>D-02</c>).</summary>
public sealed record ParameterValue
{
    /// <summary>Gets the exact parameter spelling in source, retained for lossless write-back.</summary>
    /// <remarks>
    /// <c>T1 tank v=300</c> keeps its <c>v</c>. Write-back edits the value and never replaces the
    /// spelling with the canonical name (<c>D-32</c>).
    /// </remarks>
    public required string WrittenName { get; init; }

    /// <summary>Gets the evaluated value, or <see langword="null"/> when the expression was deferred.</summary>
    public Quantity? Value { get; init; }

    /// <summary>Gets the accepted name, for a symbol-valued parameter.</summary>
    public string? Symbol { get; init; }

    /// <summary>Gets what this parameter names, for a reference-valued parameter.</summary>
    public PropertyReference? Reference { get; init; }

    /// <summary>Gets the expression, retained for deferred re-evaluation and for write-back.</summary>
    public required ExpressionSyntax Expression { get; init; }

    /// <summary>Gets where the assignment sits in the source.</summary>
    public required TextSpan Span { get; init; }
}

/// <summary>A <c>let</c> binding: a name for a value used more than once.</summary>
/// <param name="Name">The bound name.</param>
/// <param name="Expression">What it was bound to, retained for write-back.</param>
/// <param name="Id">This binding's identity in the dependency graph.</param>
/// <param name="Value">
/// The evaluated value, or <see langword="null"/> when the expression was deferred or failed.
/// </param>
/// <param name="DeclarationSpan">Where the binding sits in the source.</param>
public sealed record BindingSymbol(
    string Name,
    ExpressionSyntax Expression,
    ValueId Id,
    Quantity? Value,
    TextSpan DeclarationSpan);

/// <summary>A reference to a component's property, such as <c>N2.t</c>.</summary>
/// <param name="Component">The component's name, as written.</param>
/// <param name="Property">The property's canonical name.</param>
public readonly record struct PropertyReference(string Component, string Property);
