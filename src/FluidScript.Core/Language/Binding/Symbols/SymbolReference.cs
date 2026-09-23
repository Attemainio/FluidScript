namespace FluidScript.Core.Language.Binding.Symbols;

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
