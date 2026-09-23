using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

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
