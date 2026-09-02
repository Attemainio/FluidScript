using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>One join between two components' ports.</summary>
/// <param name="From">The endpoint on the left of the dash.</param>
/// <param name="To">The endpoint on the right.</param>
/// <param name="SourceSpan">
/// The line the connection was written on. A chain of three endpoints produces two connections that
/// share one span, which is what makes a diagnostic point at the line the user can see.
/// </param>
public sealed record ConnectionSymbol(EndpointSymbol From, EndpointSymbol To, TextSpan SourceSpan);

/// <summary>One end of a connection: a component and the port it claimed.</summary>
/// <param name="Component">The component's name.</param>
/// <param name="Port">
/// The port's name, empty for a component whose ports are unlimited and unnamed — which is only
/// <c>node</c>.
/// </param>
public readonly record struct EndpointSymbol(string Component, string Port);

/// <summary>One end of a subcircuit's attachment to its parent (<c>D-33</c>).</summary>
/// <param name="ParentComponentName">The name the script wrote.</param>
/// <param name="ParentComponent">
/// The component it resolved to, or <see langword="null"/> while no such component exists — which is
/// <c>FS1518</c>, not an exception.
/// </param>
/// <param name="Span">Where the line sits in the source.</param>
public sealed record AttachmentSymbol(
    string ParentComponentName,
    ComponentSymbol? ParentComponent,
    TextSpan Span);

/// <summary>A controller bound to what it drives and what it reads (<c>D-40</c>).</summary>
/// <remarks>
/// Every field comes from a named argument, so transposing two of them is a binding error rather than
/// a silent reversal that drives the valve the wrong way.
/// </remarks>
public sealed record ControlBindingSymbol
{
    /// <summary>Gets the controller component named by <c>by=</c>.</summary>
    public required ComponentSymbol Controller { get; init; }

    /// <summary>Gets the settable parameter named by <c>actuate=</c>, such as <c>TV1.position</c>.</summary>
    /// <remarks>
    /// Always qualified. A bare component name is <c>FS1515</c> (<c>D-43</c>): there is no per-kind
    /// default actuator to fall back on, deliberately, because a valve has more than one thing that
    /// could move.
    /// </remarks>
    public required PropertyReference Actuator { get; init; }

    /// <summary>Gets the property named by <c>measure=</c>, such as <c>N2.t</c>.</summary>
    public required PropertyReference Measurement { get; init; }

    /// <summary>Gets the target value named by <c>setpoint=</c>, in the measurement's dimension.</summary>
    public Quantity? Setpoint { get; init; }

    /// <summary>Gets where the line sits in the source.</summary>
    public required TextSpan Span { get; init; }
}

/// <summary>One scheduled change: a value stepped at an instant or ramped over an interval.</summary>
/// <param name="Circuit">The circuit whose schedule this belongs to.</param>
/// <param name="Target">The component parameter being changed.</param>
/// <param name="From">When it starts.</param>
/// <param name="To">When it ends, equal to <paramref name="From"/> for a step.</param>
/// <param name="FromValue">The value at the start, or <see langword="null"/> for a step.</param>
/// <param name="ToValue">The value at the end, which is the whole change for a step.</param>
/// <param name="Span">Where the line sits in the source.</param>
/// <remarks>
/// <c>15</c>'s binding order never mentioned the schedule — eleven steps, and nothing consumed a
/// disturbance. This is the symbol the step that was missing produces.
/// </remarks>
public sealed record DisturbanceSymbol(
    string Circuit,
    PropertyReference Target,
    Quantity? From,
    Quantity? To,
    Quantity? FromValue,
    Quantity? ToValue,
    TextSpan Span);

/// <summary>What a source position points at.</summary>
public abstract record SymbolReference
{
    private SymbolReference()
    {
    }

    /// <summary>A circuit header.</summary>
    /// <param name="Value">The circuit.</param>
    public sealed record Circuit(CircuitSymbol Value) : SymbolReference;

    /// <summary>A component, at its declaration or at a mention of its name.</summary>
    /// <param name="Value">The component.</param>
    public sealed record Component(ComponentSymbol Value) : SymbolReference;

    /// <summary>A <c>let</c> binding, at its declaration or at a use.</summary>
    /// <param name="Value">The binding.</param>
    public sealed record Binding(BindingSymbol Value) : SymbolReference;

    /// <summary>One connection, at the line that wrote it.</summary>
    /// <param name="Value">The connection.</param>
    /// <remarks>
    /// A chain of three endpoints is two connections sharing one span, so a position on that line
    /// resolves to the first of them. The endpoints inside it are narrower and win on their own
    /// offsets, which is what makes clicking a name select the component rather than the line.
    /// </remarks>
    public sealed record Connection(ConnectionSymbol Value) : SymbolReference;
}

/// <summary>Maps a source position back to the symbol it declares or references.</summary>
/// <remarks>
/// Backs hover, go-to-definition, and write-back's need to find the line that owns a value. It is
/// built from spans the binder already has, so nothing has to re-parse to answer a question about
/// where a name came from.
/// </remarks>
public interface ISymbolMap
{
    /// <summary>Finds the symbol at a position.</summary>
    /// <param name="offset">A UTF-16 offset into the source.</param>
    /// <returns>The innermost symbol covering that position, or <see langword="null"/>.</returns>
    SymbolReference? AtOffset(int offset);

    /// <summary>Finds every place a symbol is named.</summary>
    /// <param name="symbol">The symbol to look for.</param>
    /// <returns>Its declaration and every reference, in source order.</returns>
    ImmutableArray<TextSpan> References(SymbolReference symbol);
}
