using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Binding;

/// <summary>Maps source positions to the symbols that own them, built once per bind.</summary>
/// <remarks>
/// <para>
/// Entries are keyed by a symbol's <em>identity</em> rather than by the symbol record itself. A
/// <see cref="ComponentSymbol"/> is a record with structural equality, and it changes after the map's
/// entries are collected — <c>Ports</c> in step 6, <c>Tag</c> in step 11 — so a symbol handed back to
/// <see cref="References"/> would not equal the one the map holds. A name does not change, and that
/// is what identity means here (<c>D-34</c>).
/// </para>
/// <para>
/// Innermost wins: the shortest span containing an offset is the symbol that position names, so a
/// component's identifier inside a connection line resolves to the component and not to the line.
/// </para>
/// </remarks>
internal sealed class SymbolMap : ISymbolMap
{
    private readonly ImmutableArray<Entry> _entries;
    private readonly ImmutableDictionary<string, ImmutableArray<TextSpan>> _references;

    private SymbolMap(
        ImmutableArray<Entry> entries,
        ImmutableDictionary<string, ImmutableArray<TextSpan>> references)
    {
        _entries = entries;
        _references = references;
    }

    /// <summary>Gets a map holding nothing, for a bind that never reached topology.</summary>
    public static SymbolMap Empty { get; } = new(
        [],
        ImmutableDictionary<string, ImmutableArray<TextSpan>>.Empty.WithComparers(StringComparer.Ordinal));

    /// <inheritdoc/>
    public SymbolReference? AtOffset(int offset)
    {
        SymbolReference? best = null;
        var narrowest = int.MaxValue;

        foreach (var entry in _entries)
        {
            var span = entry.Span;

            if (offset < span.Start || offset >= span.Start + span.Length || span.Length >= narrowest)
            {
                continue;
            }

            narrowest = span.Length;
            best = entry.Symbol;
        }

        return best;
    }

    /// <inheritdoc/>
    public ImmutableArray<TextSpan> References(SymbolReference symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        return _references.TryGetValue(KeyOf(symbol), out var spans) ? spans : [];
    }

    /// <summary>Identifies a symbol by what does not change about it.</summary>
    private static string KeyOf(SymbolReference symbol) => symbol switch
    {
        SymbolReference.Circuit circuit => $"circuit:{circuit.Value.Name}",
        SymbolReference.Component component => $"component:{component.Value.Name}",
        SymbolReference.Binding binding => $"binding:{binding.Value.Name}",
        SymbolReference.Connection connection =>
            $"connection:{connection.Value.From.Component}.{connection.Value.From.Port}"
            + $"-{connection.Value.To.Component}.{connection.Value.To.Port}",
        _ => symbol.GetType().Name,
    };

    private readonly record struct Entry(TextSpan Span, SymbolReference Symbol);

    /// <summary>Collects spans as the binder visits them, then freezes them into a map.</summary>
    internal sealed class Builder
    {
        private readonly List<Entry> _entries = [];
        private readonly Dictionary<string, List<TextSpan>> _references = new(StringComparer.Ordinal);

        /// <summary>Records that a span names a symbol.</summary>
        /// <param name="symbol">What the span names.</param>
        /// <param name="span">Where it is written.</param>
        public void Add(SymbolReference symbol, TextSpan span)
        {
            _entries.Add(new Entry(span, symbol));

            var key = KeyOf(symbol);

            if (!_references.TryGetValue(key, out var spans))
            {
                _references[key] = spans = [];
            }

            spans.Add(span);
        }

        /// <summary>Freezes what has been collected.</summary>
        /// <returns>A map whose reference lists are in source order.</returns>
        public SymbolMap Build() => new(
            [.. _entries],
            _references.ToImmutableDictionary(
                static entry => entry.Key,
                static entry => ImmutableArray.CreateRange(entry.Value.OrderBy(static span => span.Start)),
                StringComparer.Ordinal));
    }
}
