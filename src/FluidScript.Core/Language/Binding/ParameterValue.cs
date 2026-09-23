using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

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

    /// <summary>Gets how the value was arrived at, when it was read at the component's own sizing point (<c>D-94</c>).</summary>
    /// <value>
    /// <see langword="null"/> for every parameter that read no curve or whose component wrote no
    /// <c>sized_at</c>. Otherwise a sentence for the report — <em>30 kW at tout=-5, 0.6 of the 50 kW
    /// the design day asks</em> — because the fraction is the number an engineer checks a bivalent
    /// choice by, and it is an outcome of the point, never something the file states.
    /// </value>
    public string? Basis { get; init; }

    /// <summary>Gets one value per declared scenario, when the parameter was written as a list (<c>D-143</c>).</summary>
    /// <value>
    /// Empty unless the file wrote <c>power=[30, 10]</c>, and then exactly as long as
    /// <see cref="ProjectSettings.Scenarios"/> -- any other length is <c>FS1540</c> and binds nothing.
    /// Each element is a whole <see cref="ParameterValue"/>, so it carries its own span, expression
    /// and basis and a dimension error is reported against the element that caused it.
    /// </value>
    /// <remarks>
    /// <strong><see cref="Value"/> stays a scalar whether or not this is empty</strong>, and holds the
    /// design scenario's element when it is not. That is what keeps the whole feature additive: the
    /// thirty-odd readers of <see cref="Value"/> -- the component factory, the contract, the scene
    /// audit -- never learn that scenarios exist, and projecting the model to scenario <c>i</c> is one
    /// pass rewriting <see cref="Value"/> from this array.
    /// </remarks>
    public ImmutableArray<ParameterValue> Scenarios { get; init; } = [];
}
