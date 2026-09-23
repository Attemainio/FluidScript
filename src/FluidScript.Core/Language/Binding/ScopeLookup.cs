using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>What a name in an expression turned out to be.</summary>
public abstract record ScopeLookup
{
    private ScopeLookup()
    {
    }

    /// <summary>A value that is already known.</summary>
    /// <param name="Quantity">The value.</param>
    /// <param name="IsBare">Whether it came from a bare number.</param>
    /// <param name="Id">Its identity in the dependency graph.</param>
    public sealed record Value(Quantity Quantity, bool IsBare, ValueId Id) : ScopeLookup;

    /// <summary>A value that will not exist until sizing or the solve has run.</summary>
    /// <param name="Id">What to wait for.</param>
    public sealed record Deferred(ValueId Id) : ScopeLookup;

    /// <summary>Nothing of that name exists.</summary>
    /// <param name="Suggestion">The closest name, or <see langword="null"/> when nothing is close.</param>
    public sealed record UnknownName(string? Suggestion) : ScopeLookup;

    /// <summary>The component exists; the property does not.</summary>
    /// <param name="Kind">The component's kind, for the message.</param>
    /// <param name="Available">What it does have.</param>
    public sealed record UnknownProperty(string Kind, ImmutableArray<string> Available) : ScopeLookup;
}
