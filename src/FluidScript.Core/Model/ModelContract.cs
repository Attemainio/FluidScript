using System.Collections.Immutable;

namespace FluidScript.Core.Model;

/// <summary>Marks a wire field that is left out entirely, rather than written as <see langword="null"/>, when it has no value.</summary>
/// <remarks>
/// The contract's own vocabulary for <c>26</c>'s rule that <see langword="null"/> means <em>not
/// computed</em> and absence means <em>not applicable</em>. The serializer outside Core reads this and
/// applies its own ignore condition (<c>D-47</c>: no serialization type is named in Core).
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsentWhenNullAttribute : Attribute;

/// <summary>The one serialized shape every consumer receives (<c>26</c>).</summary>
/// <remarks>
/// <para>
/// <strong>No Core type is on the wire.</strong> Every record here is a copy, so a rename in Core cannot
/// reshape the API. Every number is in the canonical script unit for its dimension (<c>D-14</c>), never
/// SI, and sits beside the unit it is in. <see langword="null"/> means <em>not computed</em>; a field
/// that is not applicable is absent, which is what <see cref="AbsentWhenNullAttribute"/> marks.
/// </para>
/// <para>
/// Property order is the wire order: the Api's serializer writes members in declaration order and
/// reads them back the same way, which is what makes a round trip byte-identical (invariant 10). No
/// serialization type is named here (<c>D-47</c>); the serializer lives in <c>FluidScript.Api</c>.
/// </para>
/// </remarks>
public sealed record ModelContract
{
    /// <summary>The contract version the producing Core implements, <c>major.minor</c>.</summary>
    public required string ContractVersion { get; init; }

    /// <summary>What produced this: the source, the language, the catalogue, the property backend.</summary>
    public required Provenance Provenance { get; init; }

    /// <summary>The <c>project</c> line, absent when the script has none (<c>D-37</c>).</summary>
    [AbsentWhenNull]
    public ProjectWire? Project { get; init; }

    /// <summary>Presentation Core carries and never interprets.</summary>
    public required StyleWire Style { get; init; }

    /// <summary>Every circuit, in declaration order; never empty (<c>D-33</c>).</summary>
    public required ImmutableArray<CircuitWire> Circuits { get; init; }

    /// <summary>One pressure datum per hydraulically connected part, not per circuit.</summary>
    public required ImmutableArray<string> PressureDatums { get; init; }

    /// <summary>Every graph component, in graph order.</summary>
    public required ImmutableArray<ComponentWire> Components { get; init; }

    /// <summary>The symbol definitions the components reference (<c>D-20</c>, <c>D-24</c>).</summary>
    public required ImmutableArray<SymbolWire> Symbols { get; init; }

    /// <summary>Every adjacency, in the model's connection order, keyed <c>c{n}</c>.</summary>
    public required ImmutableArray<ConnectionWire> Connections { get; init; }

    /// <summary>The layout hints, serialized from <c>25</c>'s contract field for field.</summary>
    public required LayoutWire Layout { get; init; }

    /// <summary>The <c>show</c> directive's resolution (<c>57</c>).</summary>
    public required VisualizationWire Visualization { get; init; }

    /// <summary>Evaluated <c>let</c> values.</summary>
    public required ImmutableArray<BindingWire> Bindings { get; init; }

    /// <summary>Every diagnostic the pipeline produced, ordered by severity then offset (<c>44</c>).</summary>
    public required ImmutableArray<DiagnosticWire> Diagnostics { get; init; }

    /// <summary>What the solve did, or <see langword="null"/> when nothing was solved.</summary>
    public required SolveWire? Solve { get; init; }
}

/// <summary>What produced the payload.</summary>
public sealed record Provenance
{
    /// <summary>SHA-256 of the source text, as <c>sha256:</c> and 64 hex digits.</summary>
    public required string SourceHash { get; init; }

    /// <summary>The language major version the script declared.</summary>
    public required int LanguageMajor { get; init; }

    /// <summary>The pipe catalogue sizes were drawn from.</summary>
    public required VersionedId Catalog { get; init; }

    /// <summary>The fluid property package.</summary>
    public required VersionedId PropertyBackend { get; init; }

    /// <summary>The atmosphere gauge pressures are relative to, kPa absolute (<c>D-26</c>).</summary>
    public required double AtmosphereKPaAbsolute { get; init; }
}

/// <summary>A named thing and its version.</summary>
/// <param name="Id">The stable identifier.</param>
/// <param name="Version">The exact version string.</param>
public sealed record VersionedId(string Id, string Version);

/// <summary>The <c>project</c> line.</summary>
/// <param name="Name">The project name.</param>
/// <param name="DefaultMode">The default solve mode, <c>steady</c>, <c>transient</c> or <see langword="null"/>.</param>
public sealed record ProjectWire(string? Name, string? DefaultMode);

/// <summary>The script's presentation directives, resolved (<c>D-104</c>).</summary>
/// <param name="Tokens">The applied <c>style</c> tokens as written.</param>
/// <param name="Spacing">The <c>spacing</c> value in world units, or <see langword="null"/> (<c>D-37</c>).</param>
/// <param name="Default">The project-level style, applied where a circuit states none.</param>
/// <param name="Named">The named styles, <c>style name = …</c>, resolved, for an editor to list.</param>
public sealed record StyleWire(ImmutableArray<string> Tokens, double? Spacing, ResolvedStyleWire Default, IReadOnlyDictionary<string, ResolvedStyleWire> Named);

/// <summary>One circuit.</summary>
public sealed record CircuitWire
{
    /// <summary>The name as written.</summary>
    public required string Name { get; init; }

    /// <summary>The number, stated or resolved.</summary>
    public required int Number { get; init; }

    /// <summary>Whether the script wrote the number; the printer needs this.</summary>
    public required bool NumberIsExplicit { get; init; }

    /// <summary>The fluid keyword.</summary>
    public required string Substance { get; init; }

    /// <summary>The solve mode: <c>steady</c> or <c>transient</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>The resolved role's canonical name, or <see langword="null"/> for a name the registry does not know (<c>D-35</c>).</summary>
    public required string? Role { get; init; }

    /// <summary>The parent circuit, or <see langword="null"/> when this one stands alone (<c>D-33</c>).</summary>
    public required string? ParentCircuit { get; init; }

    /// <summary>The parent component this circuit takes flow from.</summary>
    public required string? InletAnchorId { get; init; }

    /// <summary>The parent component this circuit returns flow to.</summary>
    public required string? OutletAnchorId { get; init; }

    /// <summary>Whether every component in this circuit has a state (invariant 7).</summary>
    public required bool Solved { get; init; }

    /// <summary>True only alongside <c>FS2502</c>.</summary>
    public required bool StatesOmitted { get; init; }
}

/// <summary>One graph component.</summary>
public sealed record ComponentWire
{
    /// <summary>The stable id (<c>25</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The script keyword for the kind.</summary>
    public required string Kind { get; init; }

    /// <summary>The kind's canonical mode -- an exchanger's <c>duty</c>, <c>rated</c> or <c>coupled</c> -- absent for a kind without one.</summary>
    [AbsentWhenNull]
    public string? Mode { get; init; }

    /// <summary>Which entry in <c>symbols</c> draws it.</summary>
    public required string SymbolId { get; init; }

    /// <summary><c>declared</c>, or <c>inferred:I1</c>, <c>inferred:I2</c>, <c>inferred:I3</c>, <c>inferred:I7</c> (a pipe a connection line's properties made, <c>D-110</c>).</summary>
    public required string Origin { get; init; }

    /// <summary>Where the declaration sits in the source: the component's line, or for an implicit pipe (I7) the connection line that made it; <see langword="null"/> for an inferred node, which has no text.</summary>
    public required SpanWire? SourceSpan { get; init; }

    /// <summary>The owning circuit (<c>D-33</c>; the losing side's under <c>D-36</c>).</summary>
    public required string Circuit { get; init; }

    /// <summary>The equipment tag, display metadata only; <see langword="null"/> when the kind has no code or the component is inferred (<c>D-34</c>).</summary>
    public required string? Tag { get; init; }

    /// <summary>The design specification, by canonical parameter name, in declaration order of the kind's parameters.</summary>
    public required IReadOnlyDictionary<string, ParameterWire> Parameters { get; init; }

    /// <summary>The solved operating point, or <see langword="null"/> when the circuit is unsolved or states are omitted.</summary>
    public required ComponentStateWire? State { get; init; }

    /// <summary>Every port, in declaration order; a tank lists only its materialized ports.</summary>
    public required ImmutableArray<PortWire> Ports { get; init; }
}

/// <summary>A character span in the source.</summary>
/// <param name="Start">The zero-based offset.</param>
/// <param name="Length">The length in UTF-16 code units.</param>
public sealed record SpanWire(int Start, int Length);

/// <summary>One design parameter, with where it came from (<c>D-02</c>).</summary>
public sealed record ParameterWire
{
    /// <summary>The value in <see cref="Unit"/>, or <see langword="null"/> under <c>FS2501</c>.</summary>
    public required double? Value { get; init; }

    /// <summary>The canonical unit, or <see langword="null"/> for a dimensionless value.</summary>
    public required string? Unit { get; init; }

    /// <summary><c>stated</c>, <c>sized</c> or <c>default</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Why a sized or default value is what it is; absent for a stated one.</summary>
    [AbsentWhenNull]
    public string? Basis { get; init; }
}

/// <summary>A solved value in its canonical unit.</summary>
/// <param name="Value">The value in <paramref name="Unit"/>, or <see langword="null"/> under <c>FS2501</c>.</param>
/// <param name="Unit">The canonical unit.</param>
public sealed record QuantityWire(double? Value, string Unit);

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

/// <summary>One tank layer.</summary>
/// <param name="Index">One-based, from the bottom.</param>
/// <param name="Elevation">The layer's top as a fraction of the tank height, <c>0…1</c>.</param>
/// <param name="T">The layer temperature.</param>
public sealed record LayerWire(int Index, double Elevation, QuantityWire T);

/// <summary>One port.</summary>
public sealed record PortWire
{
    /// <summary>The port id: <c>in</c>, <c>out</c>, <c>in2</c>, <c>a</c>. The id is the model's key, not the script's spelling -- a script writes the second side <c>in[2]</c> (<c>D-120</c>) and the wire carries <c>in2</c>, so the ids never changed.</summary>
    public required string Name { get; init; }

    /// <summary><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</summary>
    public required string Role { get; init; }

    /// <summary>The component the port is wired to, or <see langword="null"/> when open.</summary>
    public required string? ConnectedTo { get; init; }

    /// <summary>A tank port's normalized elevation; absent otherwise.</summary>
    [AbsentWhenNull]
    public double? Elevation { get; init; }

    /// <summary>The tank layer the port meets; absent otherwise.</summary>
    [AbsentWhenNull]
    public int? Layer { get; init; }
}

/// <summary>A symbol definition in a normalized box (<c>D-20</c>, <c>D-102</c>).</summary>
public sealed record SymbolWire
{
    /// <summary>The id components reference, <c>kind.variant</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The bounding box as <c>[x, y, width, height]</c> in symbol units; what layout reasons on.</summary>
    public required ImmutableArray<double> ViewBox { get; init; }

    /// <summary>What the canvas draws inside the box. Never executable.</summary>
    public required ImmutableArray<PrimitiveWire> Primitives { get; init; }

    /// <summary>The default arrangement: each named port's anchor on the box edge and its outward direction.</summary>
    public required IReadOnlyDictionary<string, AnchorWire> PortAnchors { get; init; }

    /// <summary>
    /// Other complete arrangements of the same ports on the same box, by name; the renderer may pick one
    /// per instance, with a rotation, to shorten the connections it has to draw. Absent when there is one.
    /// </summary>
    [AbsentWhenNull]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, AnchorWire>>? Alternatives { get; init; }

    /// <summary>Rules for indexed ports such as a tank's <c>in{n}</c>; absent for a fixed-port symbol.</summary>
    [AbsentWhenNull]
    public ImmutableArray<IndexedAnchorWire>? IndexedPortAnchors { get; init; }

    /// <summary>Where the label sits, <c>[x, y]</c>.</summary>
    public required ImmutableArray<double> LabelAnchor { get; init; }

    /// <summary>
    /// Which transforms the kind admits (<c>28</c> A4, <c>D-108</c>): <c>free</c> turns by any quarter, mirrored or not;
    /// <c>standing</c> is never turned, only mirrored left-right, up-down or both (every exchanger); <c>upright</c>
    /// admits only the left-right mirror (a tank, whose layers are a vertical order); <c>level</c> admits every transform
    /// but stands vertical only where nothing level fits (a pump, <c>D-113</c>). A fact about the kind, never a preference.
    /// </summary>
    public string TransformClass { get; init; } = "free";
}

/// <summary>One drawing primitive; the fields a kind does not use are absent.</summary>
public sealed record PrimitiveWire
{
    /// <summary><c>rect</c>, <c>line</c>, <c>circle</c>, <c>polyline</c> or <c>polygon</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>A rectangle's or circle's origin.</summary>
    [AbsentWhenNull]
    public double? X { get; init; }

    /// <summary>A rectangle's or circle's origin.</summary>
    [AbsentWhenNull]
    public double? Y { get; init; }

    /// <summary>A rectangle's width.</summary>
    [AbsentWhenNull]
    public double? Width { get; init; }

    /// <summary>A rectangle's height.</summary>
    [AbsentWhenNull]
    public double? Height { get; init; }

    /// <summary>A circle's radius.</summary>
    [AbsentWhenNull]
    public double? R { get; init; }

    /// <summary>A line's start.</summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? From { get; init; }

    /// <summary>A line's end.</summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? To { get; init; }

    /// <summary>A polyline's or polygon's points, flattened <c>[x0, y0, x1, y1, …]</c>.</summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? Points { get; init; }

    /// <summary>
    /// What fills a closed shape: <c>state</c> for the active colour scale's slot (<c>57</c>), <c>stroke</c>
    /// for a solid mark in the line colour; absent for an outline.
    /// </summary>
    [AbsentWhenNull]
    public string? Fill { get; init; }

    /// <summary><see langword="true"/> for a dashed stroke; absent for a solid one.</summary>
    [AbsentWhenNull]
    public bool? Dashed { get; init; }
}

/// <summary>Where a port meets its symbol, and which way a connection leaves it.</summary>
public sealed record AnchorWire
{
    /// <summary>The point on the box edge, <c>[x, y]</c> in symbol units.</summary>
    public required ImmutableArray<double> At { get; init; }

    /// <summary>
    /// The outward unit vector a connection leaves along, <c>[dx, dy]</c> with <c>y</c> up (<c>28</c> A1); rotates with
    /// the box. Absent for the wildcard anchor, whose direction the layout chooses.
    /// </summary>
    [AbsentWhenNull]
    public ImmutableArray<double>? Direction { get; init; }
}

/// <summary>An anchor rule for an indexed port family.</summary>
public sealed record IndexedAnchorWire
{
    /// <summary>The port name prefix, <c>in</c> or <c>out</c>.</summary>
    public required string Prefix { get; init; }

    /// <summary>The box side the family sits on.</summary>
    public required string Side { get; init; }

    /// <summary>The outward unit vector every anchor of the family leaves along, <c>[dx, dy]</c>.</summary>
    public required ImmutableArray<double> Direction { get; init; }

    /// <summary>Which port field gives the position along that side.</summary>
    public required string VerticalCoordinate { get; init; }

    /// <summary>The smallest index the rule covers.</summary>
    public required int MinIndex { get; init; }

    /// <summary>The largest index the rule covers.</summary>
    public required int MaxIndex { get; init; }
}

/// <summary>One adjacency.</summary>
public sealed record ConnectionWire
{
    /// <summary><c>c{n}</c>, by position in the model's connection list.</summary>
    public required string Id { get; init; }

    /// <summary>Where it starts.</summary>
    public required EndpointWire From { get; init; }

    /// <summary>Where it ends.</summary>
    public required EndpointWire To { get; init; }

    /// <summary><c>forward</c>, <c>reverse</c> or <c>none</c>: the solved direction relative to how it was written.</summary>
    public required string Flow { get; init; }

    /// <summary>The solved flow along it, or <see langword="null"/> when unsolved.</summary>
    public required ConnectionStateWire? State { get; init; }
}

/// <summary>One end of a connection.</summary>
/// <param name="Component">The component id.</param>
/// <param name="Port">The port name, or <see langword="null"/> for a node.</param>
public sealed record EndpointWire(string Component, string? Port);

/// <summary>A connection's solved state.</summary>
/// <param name="Flow">The mass flow in the written direction.</param>
public sealed record ConnectionStateWire(QuantityWire Flow);

/// <summary><c>25</c>'s hints, field for field.</summary>
public sealed record LayoutWire
{
    /// <summary>Depth-first order from each pressure datum.</summary>
    public required ImmutableArray<string> Order { get; init; }


    /// <summary>The heat-progression bands, left to right.</summary>
    public required ImmutableArray<ThermalStageWire> ThermalStages { get; init; }

    /// <summary>Solved direction per connection id.</summary>
    public required IReadOnlyDictionary<string, string> Flow { get; init; }


    /// <summary>Pipe expansions.</summary>
    public required ImmutableArray<ComponentGroupWire> Groups { get; init; }

    /// <summary>Instruments and controllers.</summary>
    public required ImmutableArray<NonFlowElementWire> NonFlowElements { get; init; }

    /// <summary>Owning circuit per component.</summary>
    public required IReadOnlyDictionary<string, string> CircuitOf { get; init; }

    /// <summary>Subcircuits sharing one parent, in declaration order.</summary>
    public required ImmutableArray<DistributionGroupWire> DistributionGroups { get; init; }

    /// <summary>Components the language added.</summary>
    public required ImmutableArray<string> Inferred { get; init; }


    /// <summary>The clearance every component keeps from every other, world units (<c>D-103</c>); the <c>spacing</c> directive or 0.5.</summary>
    public required double Margin { get; init; }

    /// <summary>The metric every label box was reserved from (<c>D-73</c>): the renderer's font must fit inside it, and its own table must agree with it.</summary>
    public required LabelMetricWire LabelMetric { get; init; }

    /// <summary>The bounds of the whole drawing as <c>[x, y, width, height]</c>, world units, outer boxes and routes included.</summary>
    public required ImmutableArray<double> Extent { get; init; }

    /// <summary>Where every component sits, in <see cref="Order"/> then the non-flow elements.</summary>
    public required ImmutableArray<PlacementWire> Placements { get; init; }

    /// <summary>Every connection's path, in connection order, then the instruments' signal lines.</summary>
    public required ImmutableArray<RouteWire> Routes { get; init; }
}

/// <summary>The declared label metric (<c>D-73</c>).</summary>
/// <param name="Size">The label's height, world units: the canvas label's size at one world unit's pixels.</param>
/// <param name="Advance">The advance per character, in em; a label's width is <c>Advance × characters × Size</c>.</param>
public sealed record LabelMetricWire(double Size, double Advance);

/// <summary>One component's place in the drawing (<c>D-103</c>). World units: a pump is 1×1, <c>y</c> grows upward and a box's <c>y</c> is its bottom edge (<c>28</c> A1).</summary>
public sealed record PlacementWire
{
    /// <summary>The component.</summary>
    public required string ComponentId { get; init; }

    /// <summary>The symbol drawn inside <see cref="Inner"/>.</summary>
    public required string SymbolId { get; init; }

    /// <summary>The symbol's box as placed, <c>[x, y, width, height]</c>; the renderer draws the strokes inside it.</summary>
    public required ImmutableArray<double> Inner { get; init; }

    /// <summary>The inner box grown by the margin; no other component's inner box enters it.</summary>
    public required ImmutableArray<double> Outer { get; init; }

    /// <summary>The quarter turn applied, clockwise degrees: 0, 90, 180 or 270.</summary>
    public required int Rotation { get; init; }

    /// <summary>Whether the symbol is mirrored left-to-right before the turn.</summary>
    public required bool Mirrored { get; init; }

    /// <summary><c>default</c> or one of the symbol's alternative arrangements (<c>D-102</c>).</summary>
    public required string Arrangement { get; init; }

    /// <summary>Every port's anchor in world coordinates with its outward direction; a node's ports are <c>#0</c>, <c>#1</c>, …</summary>
    public required IReadOnlyDictionary<string, AnchorWire> Anchors { get; init; }

    /// <summary>Where the label sits, <c>[x, y]</c>: the centre of <see cref="LabelBox"/>.</summary>
    public required ImmutableArray<double> LabelAt { get; init; }

    /// <summary>The box the label reserves, <c>[x, y, width, height]</c>, from the declared metric (<c>D-73</c>): height is the label size, width the advance times the characters. The renderer draws the text centred in it.</summary>
    public required ImmutableArray<double> LabelBox { get; init; }

    /// <summary>Whether the label sits clear of every symbol, label and line; when <see langword="false"/> the renderer draws a leader from the label to its owner (<c>53</c>).</summary>
    public required bool LabelClear { get; init; }

    /// <summary><c>computed</c>; <c>pinned</c> is reserved for a placement the script states.</summary>
    public required string Source { get; init; }

    /// <summary>The resolved style: the script's named or anonymous style (<c>D-104</c>); absent when the theme's defaults apply throughout.</summary>
    [AbsentWhenNull]
    public ResolvedStyleWire? Style { get; init; }

    /// <summary>Where the component's representative value sits on the active colour scale, 0 to 1; <see langword="null"/> when not computed. The same as <c>Scales[visualization.active].At</c>.</summary>
    public required double? Scale { get; init; }

    /// <summary>The component's position on every available scale, keyed by property (<c>D-117</c>): the switcher needs no request.</summary>
    public required IReadOnlyDictionary<string, ScalePositionWire> Scales { get; init; }
}

/// <summary>One connection's path.</summary>
public sealed record RouteWire
{
    /// <summary><c>c{n}</c> for a connection; <c>{instrument}:measures</c> or <c>{controller}:actuates</c> for a signal line.</summary>
    public required string Id { get; init; }

    /// <summary><c>pipe</c> or <c>signal</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The draw order (<c>28</c> C16): <c>supply</c> in front, <c>return</c> behind it, <c>signal</c> behind everything. A pipe is supply until the flow from a heat source has passed a losing side.</summary>
    public required string Layer { get; init; }

    /// <summary>The orthogonal polyline, flattened <c>[x0, y0, x1, y1, …]</c>; the first and last points are the anchors.</summary>
    public required ImmutableArray<double> Points { get; init; }

    /// <summary>Where this route passes behind another it crosses, flattened <c>[x0, y0, …]</c> in world units; the renderer breaks this route around each so the one in front runs through (<c>28</c> C16).</summary>
    public required ImmutableArray<double> Hops { get; init; }

    /// <summary>The resolved style, from the component the route leaves; absent when the theme's defaults apply throughout.</summary>
    [AbsentWhenNull]
    public ResolvedStyleWire? Style { get; init; }

    /// <summary>The scale position at the start, for a gradient; <see langword="null"/> when not computed. The same as <c>Scales[visualization.active].From</c>.</summary>
    public required double? ScaleFrom { get; init; }

    /// <summary>The scale position at the end.</summary>
    public required double? ScaleTo { get; init; }

    /// <summary>The route's ends on every available scale, keyed by property (<c>D-117</c>); <c>At</c> is unused for a route.</summary>
    public required IReadOnlyDictionary<string, ScalePositionWire> Scales { get; init; }
}

/// <summary>A style with every name resolved (<c>D-104</c>). A <see langword="null"/> colour or width is the theme's default.</summary>
/// <param name="Stroke">The stroke colour, <c>#rrggbb</c>, or <see langword="null"/> for the theme's.</param>
/// <param name="StrokeWidth">The stroke width in CSS pixels at scale 1, or <see langword="null"/> for the theme's.</param>
/// <param name="Pattern"><c>solid</c>, <c>dashed</c>, <c>dotted</c> or <c>dash-dot</c>.</param>
/// <param name="Fill">The static fill colour, or <see langword="null"/> for none; the colour scale paints over it while <c>show</c> is active.</param>
/// <param name="Corner"><c>fillet</c>, <c>round</c>, <c>sharp</c> or <see langword="null"/> for the theme's.</param>
public sealed record ResolvedStyleWire(string? Stroke, double? StrokeWidth, string Pattern, string? Fill, string? Corner);

/// <summary>One thermal stage.</summary>
/// <param name="Rank">The band index.</param>
/// <param name="Role"><c>source</c>, <c>conversion</c>, <c>storage</c>, <c>consumer</c> or <c>neutral</c>.</param>
/// <param name="Components">Members in graph order.</param>
public sealed record ThermalStageWire(int Rank, string Role, ImmutableArray<string> Components);

/// <summary>A pipe expansion.</summary>
/// <param name="ParentComponentId">The declared pipe.</param>
/// <param name="Children">What lowering made of it.</param>
public sealed record ComponentGroupWire(string ParentComponentId, ImmutableArray<string> Children);

/// <summary>An instrument or controller.</summary>
/// <param name="ComponentId">Its id.</param>
/// <param name="PlacementAnchorId">The component it is drawn beside.</param>
/// <param name="MeasurementTargetId">What it reads.</param>
/// <param name="ActuationTargetId">What it drives, or <see langword="null"/> for an instrument.</param>
/// <param name="NavigationOrder">Its position in the tab order.</param>
public sealed record NonFlowElementWire(
    string ComponentId, string PlacementAnchorId, string MeasurementTargetId, string? ActuationTargetId, int NavigationOrder);

/// <summary>A distribution group.</summary>
/// <param name="ParentCircuit">The circuit owning the rails.</param>
/// <param name="Members">The branches, at least two.</param>
public sealed record DistributionGroupWire(string ParentCircuit, ImmutableArray<string> Members);

/// <summary>The <c>show</c> directive resolved (<c>57</c>).</summary>
public sealed record VisualizationWire
{
    /// <summary>The property the colour scale follows.</summary>
    public required string Active { get; init; }

    /// <summary>The properties the switcher offers.</summary>
    public required ImmutableArray<string> Available { get; init; }

    /// <summary>The scale for <see cref="Active"/>. The same as <c>Scales[Active]</c>.</summary>
    public required ScaleWire Scale { get; init; }

    /// <summary>A scale per available property (<c>D-117</c>), each with its own domain, so switching needs no recompile (<c>57</c> invariant 6).</summary>
    public required IReadOnlyDictionary<string, ScaleWire> Scales { get; init; }
}

/// <summary>An element's place on one colour scale, each 0 to 1 or <see langword="null"/> where the element has no such value (<c>57</c> invariant 5: neutral, never the low end).</summary>
/// <param name="At">The representative value: a node's own, a component's outlet (<c>D-30</c>).</param>
/// <param name="From">Where a gradient starts: a component's inlet, a route's first end.</param>
/// <param name="To">Where it ends: a component's outlet, a route's last end.</param>
public sealed record ScalePositionWire(double? At, double? From, double? To);

/// <summary>A colour scale.</summary>
public sealed record ScaleWire
{
    /// <summary>The property.</summary>
    public required string Property { get; init; }

    /// <summary>The legend title.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The unit the domain is in.</summary>
    public required string Unit { get; init; }

    /// <summary><c>sequential</c> or <c>diverging</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The range mapped to the scale's ends, or <see langword="null"/> before anything is solved.</summary>
    public required DomainWire? Domain { get; init; }

    /// <summary>Whether every element has the same value at the legend's precision; a plant on its pressure datum is degenerate at 0 (<c>C-112</c>).</summary>
    public required bool Degenerate { get; init; }
}

/// <summary>A scale domain.</summary>
/// <param name="Min">The low end, in the scale's unit.</param>
/// <param name="Max">The high end, in the scale's unit.</param>
/// <param name="Nice">Whether the ends were settled to six significant digits and rounded outward to legend ticks (<c>C-112</c>).</param>
public sealed record DomainWire(double Min, double Max, bool Nice);

/// <summary>An evaluated <c>let</c>.</summary>
/// <param name="Name">The name.</param>
/// <param name="Value">The value in <paramref name="Unit"/>, or <see langword="null"/> when the binding is deferred to the solve.</param>
/// <param name="Unit">The canonical unit, or <see langword="null"/> when dimensionless or deferred.</param>
/// <param name="Dimension">The dimension's name, so the editor can filter completion by it (<c>52</c>); <see langword="null"/> for a dimensionless, an unnamed or a deferred binding.</param>
/// <param name="SiUnit">For an unnamed dimension, the SI spelling the value carries, shown dimmed by completion; otherwise <see langword="null"/>.</param>
public sealed record BindingWire(string Name, double? Value, string? Unit, string? Dimension, string? SiUnit);

/// <summary>One diagnostic (<c>44</c>).</summary>
public sealed record DiagnosticWire
{
    /// <summary>The registry code.</summary>
    public required string Code { get; init; }

    /// <summary><c>error</c>, <c>warning</c> or <c>info</c>.</summary>
    public required string Severity { get; init; }

    /// <summary>The rendered message.</summary>
    public required string Message { get; init; }

    /// <summary>Where, or <see langword="null"/> when it is about no source text.</summary>
    public required RangeWire? Range { get; init; }

    /// <summary>The component it is about, or <see langword="null"/>.</summary>
    public required string? Component { get; init; }

    /// <summary>A fix, or <see langword="null"/>.</summary>
    public required SuggestionWire? Suggestion { get; init; }

    /// <summary>Other places it is about.</summary>
    public required ImmutableArray<RelatedWire> Related { get; init; }
}

/// <summary>A source range in both forms, from one line index.</summary>
public sealed record RangeWire
{
    /// <summary>Where it starts.</summary>
    public required PositionWire Start { get; init; }

    /// <summary>Where it ends, exclusive.</summary>
    public required PositionWire End { get; init; }

    /// <summary>The zero-based character offset.</summary>
    public required int Offset { get; init; }

    /// <summary>The length in UTF-16 code units.</summary>
    public required int Length { get; init; }
}

/// <summary>A line and column, both zero-based; the column counts UTF-16 code units.</summary>
/// <param name="Line">The line.</param>
/// <param name="Character">The column.</param>
public sealed record PositionWire(int Line, int Character);

/// <summary>A concrete fix.</summary>
/// <param name="Title">What it does.</param>
/// <param name="Range">What it replaces.</param>
/// <param name="NewText">What it puts there.</param>
public sealed record SuggestionWire(string Title, RangeWire Range, string NewText);

/// <summary>A related location.</summary>
/// <param name="Message">Why it is related.</param>
/// <param name="Range">Where it is.</param>
public sealed record RelatedWire(string Message, RangeWire Range);

/// <summary>What the solve did.</summary>
public sealed record SolveWire
{
    /// <summary>Whether the last pass converged.</summary>
    public required bool Converged { get; init; }

    /// <summary>Newton iterations over every sizing pass, retries included: the run's work, where a warm start's saving shows (<c>A-4</c>).</summary>
    public required int Iterations { get; init; }

    /// <summary>The scaled residual norm at the end.</summary>
    public required double ResidualNorm { get; init; }

    /// <summary>Wall time, or <see langword="null"/> when the caller did not time it.</summary>
    public required int? ElapsedMs { get; init; }

    /// <summary>Outer-loop passes.</summary>
    public required int SizingPasses { get; init; }
}
