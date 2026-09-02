using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Language;
using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>An instrument placed on a node, reading one property of it.</summary>
/// <remarks>
/// <para>
/// One class serves all three v1 instruments, because <strong>which property an instrument reads is
/// registry data</strong> (<see cref="ComponentKindInfo.MeasuredProperty"/>) rather than behaviour.
/// This is not the design <c>D-61</c> rejected: that rejection was about the <em>script</em> — one
/// <c>sensor</c> keyword with <c>measures=t</c> costs two tokens where one would do and throws away
/// the natural tag codes. Three keywords, three tag codes and one implementation is what that
/// decision asks for, not three near-identical classes.
/// </para>
/// <para>
/// A future kind measuring something new is a registry row plus a case in <see cref="Read"/>, and
/// <c>ObserverReadingTests</c> fails until the case exists.
/// </para>
/// </remarks>
public sealed class PlacedSensor : IObserver
{
    private readonly ComponentKindInfo _kind;

    /// <summary>Initializes a sensor of a kind, placed on a node.</summary>
    /// <param name="name">The user's identifier for the instrument.</param>
    /// <param name="kind">The registry entry, which must be an observer kind naming a measured property.</param>
    /// <param name="attachedNode">The name of the node the <c>at</c> clause placed it on.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> observes nothing.</exception>
    public PlacedSensor(string name, ComponentKindInfo kind, string attachedNode)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(attachedNode);

        if (!kind.IsObserver || kind.MeasuredProperty is null)
        {
            throw new ArgumentException(
                $"'{kind.Keyword}' is not an observer kind, or names no measured property.", nameof(kind));
        }

        _kind = kind;
        Name = name;
        AttachedNode = attachedNode;
        ObservedProperties = [new PropertyReference(attachedNode, kind.MeasuredProperty)];
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Kind => _kind.Keyword;

    /// <inheritdoc/>
    /// <value>Always <see langword="null"/>: an instrument has nothing to be in a mode about.</value>
    public string? Mode => null;

    /// <inheritdoc/>
    /// <value>Always empty. No v1 instrument takes a parameter; dynamics and error bands are post-v1.</value>
    public ImmutableDictionary<string, Quantity> StatedParameters => ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    /// <value>Always empty: there is nothing on an instrument to size.</value>
    public ImmutableDictionary<string, Quantity> SizedParameters => ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    /// <value>Always empty.</value>
    public ImmutableDictionary<string, Quantity> DefaultParameters => ImmutableDictionary<string, Quantity>.Empty;

    /// <inheritdoc/>
    public string AttachedNode { get; }

    /// <inheritdoc/>
    public ImmutableArray<PropertyReference> ObservedProperties { get; }

    /// <summary>Gets the canonical name of the property this instrument measures.</summary>
    /// <value><c>t</c>, <c>p</c> or <c>flow</c> in v1.</value>
    public string MeasuredProperty => _kind.MeasuredProperty!;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// The kind measures a property this method has no case for. Unreachable from any script: it means
    /// a registry row was added without a reading, which <c>ObserverReadingTests</c> catches.
    /// </exception>
    public Quantity Read(in NodeObservation observation) => MeasuredProperty switch
    {
        "t" => observation.State.Temperature,
        "p" => observation.State.Pressure,
        "flow" => observation.MassFlow,
        var other => throw new NotSupportedException(
            $"'{Kind}' measures '{other}', which no reading is defined for."),
    };
}
