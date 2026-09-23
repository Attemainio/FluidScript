using System.Collections.Immutable;

using FluidScript.Core.Binding;

namespace FluidScript.Core.Components;

/// <summary>Builds the instruments a bound model declares, and resolves what they read.</summary>
/// <remarks>
/// <para>
/// The step between binding and physics for the observer family. It is separate from lowering
/// (<c>23</c>) on purpose: lowering builds a <c>CircuitGraph</c>, and an observer is deliberately not
/// in it. Nothing here touches ports, connections or inference.
/// </para>
/// <para>
/// It is placed here rather than in <c>P3.4</c> so that the flow-graph package inherits observer
/// construction instead of inventing it while also building the graph — the same reason <c>08</c>
/// puts this whole package before the six flow kinds.
/// </para>
/// </remarks>
public static class ModelObservers
{
    /// <summary>Builds an instrument for every placed observer in a bound model.</summary>
    /// <param name="model">The bound model.</param>
    /// <returns>The instruments, in declaration order.</returns>
    /// <remarks>
    /// <strong>An observer that was never placed is skipped rather than reported.</strong> It has
    /// already produced <c>FS1533</c> at bind time, and a model under editing is malformed most of the
    /// time — a second complaint from a later stage would be noise, and throwing would break the rule
    /// that no pipeline stage throws on user input. The same goes for a kind that failed to resolve.
    /// </remarks>
    public static ImmutableArray<PlacedSensor> Collect(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var builder = ImmutableArray.CreateBuilder<PlacedSensor>();

        foreach (var component in model.Components)
        {
            if (component.Kind is { IsObserver: true, MeasuredProperty: not null } kind
                && component.AttachedTo is { } node)
            {
                builder.Add(new PlacedSensor(component.Name, kind, node));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Rewrites a property reference that names an instrument into the node property it reads.</summary>
    /// <param name="reference">What a <c>control</c> line named, such as <c>TE1.t</c> or <c>N2.t</c>.</param>
    /// <param name="observers">The instruments from <see cref="Collect"/>.</param>
    /// <returns>
    /// The node-level reference the solver can evaluate — <c>N2.t</c> for both of those — or
    /// <paramref name="reference"/> unchanged when it does not name an instrument.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>D-61</c>'s "<c>TE1.t</c> is <c>N2.t</c>", made executable. A sensor holds no state, so
    /// reading one is an alias for reading the node it sits on, resolved once here rather than at every
    /// controller step.
    /// </para>
    /// <para>
    /// <strong>The property the script wrote is discarded in favour of the instrument's own.</strong>
    /// A sensor measures exactly one thing, so <c>TE1.t</c> and a bare <c>TE1</c> resolve identically;
    /// a qualification naming something else was already rejected at bind time.
    /// </para>
    /// </remarks>
    public static PropertyReference Resolve(
        PropertyReference reference, ImmutableArray<PlacedSensor> observers)
    {
        foreach (var observer in observers)
        {
            if (string.Equals(observer.Name, reference.Component, StringComparison.Ordinal))
            {
                return observer.ObservedProperties[0];
            }
        }

        return reference;
    }
}
