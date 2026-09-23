using System.Collections.Immutable;

namespace FluidScript.Core.Model.Contract;

/// <summary>A component's solved operating point. Fields a kind does not have are absent.</summary>
public sealed record ComponentStateWire
{
    /// <summary>Mass flow through the component's first flow group, positive from its first port toward its second.</summary>
    [AbsentWhenNull]
    public QuantityWire? Flow { get; init; }

    /// <summary>Temperature at the inlet port.</summary>
    [AbsentWhenNull]
    public QuantityWire? TIn { get; init; }

    /// <summary>
    /// Temperature of the stream leaving through the outlet port — the component's own outlet, not the
    /// node it discharges into. A valve passing 50 °C into a node where a colder return also arrives
    /// reports 50 °C; the node reports the mix. A port that is itself a mix (a mixing valve's common
    /// port, a vessel outlet) reports the node.
    /// </summary>
    [AbsentWhenNull]
    public QuantityWire? TOut { get; init; }

    /// <summary>Pressure at the inlet port, gauge in the canonical unit (<c>D-26</c>).</summary>
    [AbsentWhenNull]
    public QuantityWire? PIn { get; init; }

    /// <summary>Pressure at the outlet port, gauge in the canonical unit (<c>D-26</c>).</summary>
    [AbsentWhenNull]
    public QuantityWire? POut { get; init; }

    /// <summary>Pressure drop inlet to outlet; negative across a pump.</summary>
    [AbsentWhenNull]
    public QuantityWire? Dp { get; init; }

    /// <summary>A pipe's mean velocity, m/s: the mass flow over the mean of the two ports' densities and the bore's flow area -- the velocity its pressure drop was computed at (<c>A-6</c>).</summary>
    [AbsentWhenNull]
    public QuantityWire? Velocity { get; init; }

    /// <summary>A pipe's Reynolds number at that velocity, with the mean density and dynamic viscosity of its two ports; dimensionless. Below 2300 the flow is laminar, above 4000 turbulent, and the pressure drop blends between (<c>A-6</c>).</summary>
    [AbsentWhenNull]
    public QuantityWire? Re { get; init; }

    /// <summary>Heat into the fluid on the first side, positive when the fluid gains.</summary>
    [AbsentWhenNull]
    public QuantityWire? Power { get; init; }

    /// <summary>Mass flow on an exchanger's second side.</summary>
    [AbsentWhenNull]
    public QuantityWire? Flow2 { get; init; }

    /// <summary>Temperature at the second side's inlet.</summary>
    [AbsentWhenNull]
    public QuantityWire? TIn2 { get; init; }

    /// <summary>Temperature at the second side's outlet.</summary>
    [AbsentWhenNull]
    public QuantityWire? TOut2 { get; init; }

    /// <summary>A pump's delivered head.</summary>
    [AbsentWhenNull]
    public QuantityWire? Head { get; init; }

    /// <summary>A node's temperature.</summary>
    [AbsentWhenNull]
    public QuantityWire? T { get; init; }

    /// <summary>A node's pressure, gauge in the canonical unit (<c>D-26</c>).</summary>
    [AbsentWhenNull]
    public QuantityWire? P { get; init; }

    /// <summary>Solved parameters the solver was asked to find -- a promoted <c>kv</c>, a sized <c>head</c>.</summary>
    [AbsentWhenNull]
    public IReadOnlyDictionary<string, QuantityWire>? Solved { get; init; }

    /// <summary>A tank's layers, bottom to top, <c>1…N</c>.</summary>
    [AbsentWhenNull]
    public ImmutableArray<LayerWire>? Layers { get; init; }
}
