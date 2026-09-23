using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>What evaluating an expression produced.</summary>
public abstract record EvaluationResult
{
    private EvaluationResult()
    {
    }

    /// <summary>A value.</summary>
    /// <param name="Quantity">The value, in SI.</param>
    /// <param name="IsBare">
    /// Whether no unit symbol took part anywhere in the expression. A bare result is reinterpreted in
    /// the target's canonical unit when it is assigned, which is what makes <c>power=30</c> mean 30 kW
    /// and <c>length=45</c> mean 45 m (<c>D-14</c>).
    /// </param>
    public sealed record Value(Quantity Quantity, bool IsBare) : EvaluationResult;

    /// <summary>The expression reads something no stage has computed yet.</summary>
    /// <param name="Dependencies">Every value it reads, so the outer loop knows what to wait for.</param>
    public sealed record Deferred(ImmutableHashSet<ValueId> Dependencies) : EvaluationResult;

    /// <summary>The expression could not be evaluated, and the reason has been reported.</summary>
    public sealed record Failed : EvaluationResult;
}
