using System.Collections.Immutable;
using FluidScript.Core.Language.Binding.Symbols;

namespace FluidScript.Core.Language.Binding;

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

    /// <summary>Gets every connection, in source order.</summary>
    public required ImmutableArray<ConnectionSymbol> Connections { get; init; }

    /// <summary>Gets the controller bindings, in declaration order (<c>D-40</c>).</summary>
    public required ImmutableArray<ControlBindingSymbol> ControlBindings { get; init; }

    /// <summary>Gets every scheduled change, in declaration order.</summary>
    public required ImmutableArray<DisturbanceSymbol> Disturbances { get; init; }

    /// <summary>Gets the map from a source position to the symbol it names.</summary>
    public required ISymbolMap SymbolMap { get; init; }

    /// <summary>Gets the expressions held until sizing or solving supplies their inputs.</summary>
    /// <value>
    /// Empty for a script whose every value is computable from literals, bindings and declared
    /// parameters. An entry here is not an error: <c>PU1 pump head=1.2*HE1.dp</c> is the design intent
    /// the deferral exists to support.
    /// </value>
    public required ImmutableArray<DeferredExpression> Deferred { get; init; }

    /// <summary>Gets every <c>curve</c> declared in the file, in declaration order (<c>D-57</c>).</summary>
    /// <value>
    /// File-wide rather than per circuit, which is the one place <c>D-52</c> does not apply: a curve is
    /// a named table, and every circuit that names it reads the same one.
    /// </value>
    public ImmutableArray<CurveSymbol> Curves { get; init; } = [];

    /// <summary>Gets where every component sits, once the script's heights have been propagated (<c>D-70</c>).</summary>
    /// <value>
    /// Derived, never stated: the result of flooding each written <c>elevation</c> through everything
    /// wired to it without a pipe in between. Lowering reads a node's height and a pipe's rise from
    /// here, so the hydrostatic terms around a closed loop cancel by construction.
    /// </value>
    public HeightMap Heights { get; init; } = HeightMap.Empty;

    /// <summary>Gets every run the file holds, in the order written (<c>D-169</c>).</summary>
    /// <value>Empty for a language 1 file, whose schedule and dynamic circuits are the file's own.</value>
    public ImmutableArray<RunSymbol> Runs { get; init; } = [];
}
