using System.Collections.Immutable;

namespace FluidScript.Core.Language.Binding;

/// <summary>The order values may be evaluated in, or the cycle that prevents one.</summary>
public abstract record OrderResult
{
    private OrderResult()
    {
    }

    /// <summary>Every value, in an order where each follows what it depends on.</summary>
    /// <param name="Order">The evaluation order.</param>
    public sealed record Ordered(ImmutableArray<ValueId> Order) : OrderResult;

    /// <summary>A value depends on itself.</summary>
    /// <param name="Cycle">The participating ids in cycle order, the first repeated at the end.</param>
    public sealed record Cyclic(ImmutableArray<ValueId> Cycle) : OrderResult;
}
