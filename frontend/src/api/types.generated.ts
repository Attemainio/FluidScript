/* Generated from src/FluidScript.Api/Contracts/Schemas/*.schema.json by npm run types. Do not edit. */
/* eslint-disable */

/**
 * The one serialized shape every consumer receives (26).
 */

export interface ModelContract {
  /**
   * The contract version the producing Core implements, major.minor.
   */
  contractVersion: string;
  /**
   * What produced this: the source, the language, the catalogue, the property backend.
   */
  provenance: Provenance;
  /**
   * The project line, absent when the script has none (D-37).
   */
  project?: Project | null;
  /**
   * Presentation Core carries and never interprets.
   */
  style: Style;
  /**
   * Every circuit, in declaration order; never empty (D-33).
   */
  circuits: Circuit[];
  /**
   * One pressure datum per hydraulically connected part, not per circuit.
   */
  pressureDatums: string[];
  /**
   * Every graph component, in graph order.
   */
  components: Component[];
  /**
   * The symbol definitions the components reference (D-20, D-24).
   */
  symbols: Symbol[];
  /**
   * Every adjacency, in the model's connection order, keyed c{n}.
   */
  connections: Connection[];
  /**
   * The layout hints, serialized from 25's contract field for field.
   */
  layout: Layout;
  /**
   * The show directive's resolution (57).
   */
  visualization: Visualization;
  /**
   * Evaluated let values.
   */
  bindings: Binding[];
  /**
   * Every diagnostic the pipeline produced, ordered by severity then offset (44).
   */
  diagnostics: Diagnostic[];
  /**
   * What the solve did, or null when nothing was solved.
   */
  solve: Solve | null;
}

export interface Provenance {
  /**
   * SHA-256 of the source text, as sha256: and 64 hex digits.
   */
  sourceHash: string;
  /**
   * The language major version the script declared.
   */
  languageMajor: number;
  /**
   * The pipe catalogue sizes were drawn from.
   */
  catalog: VersionedId;
  /**
   * The fluid property package.
   */
  propertyBackend: VersionedId;
  /**
   * The atmosphere gauge pressures are relative to, kPa absolute (D-26).
   */
  atmosphereKPaAbsolute: number;
}

export interface VersionedId {
  /**
   * The stable identifier.
   */
  id: string;
  /**
   * The exact version string.
   */
  version: string;
}

export interface Project {
  /**
   * The project name.
   */
  name: string | null;
  /**
   * The default solve mode, steady, transient or null.
   */
  defaultMode: string | null;
}

export interface Style {
  /**
   * The applied style tokens as written.
   */
  tokens: string[];
  /**
   * The spacing value in world units, or null (D-37).
   */
  spacing: number | null;
  /**
   * The project-level style, applied where a circuit states none.
   */
  default: ResolvedStyle;
  /**
   * The named styles, style name = …, resolved, for an editor to list.
   */
  named: {
    /**
     * A style with every name resolved (D-104). A null colour or width is the theme's default.
     */
    [k: string]: ResolvedStyle | undefined;
  };
}

export interface ResolvedStyle {
  /**
   * The stroke colour, #rrggbb, or null for the theme's.
   */
  stroke: string | null;
  /**
   * The stroke width in CSS pixels at scale 1, or null for the theme's.
   */
  strokeWidth: number | null;
  /**
   * solid, dashed, dotted or dash-dot.
   */
  pattern: string;
  /**
   * The static fill colour, or null for none; the colour scale paints over it while show is active.
   */
  fill: string | null;
  /**
   * fillet, round, sharp or null for the theme's.
   */
  corner: string | null;
}

export interface Circuit {
  /**
   * The name as written.
   */
  name: string;
  /**
   * The number, stated or resolved.
   */
  number: number;
  /**
   * Whether the script wrote the number; the printer needs this.
   */
  numberIsExplicit: boolean;
  /**
   * The fluid keyword.
   */
  substance: string;
  /**
   * The solve mode: steady or transient.
   */
  mode: string;
  /**
   * The resolved role's canonical name, or null for a name the registry does not know (D-35).
   */
  role: string | null;
  /**
   * The parent circuit, or null when this one stands alone (D-33).
   */
  parentCircuit: string | null;
  /**
   * The parent component this circuit takes flow from.
   */
  inletAnchorId: string | null;
  /**
   * The parent component this circuit returns flow to.
   */
  outletAnchorId: string | null;
  /**
   * Whether every component in this circuit has a state (invariant 7).
   */
  solved: boolean;
  /**
   * True only alongside FS2502.
   */
  statesOmitted: boolean;
}

export interface Component {
  /**
   * The stable id (25).
   */
  id: string;
  /**
   * The script keyword for the kind.
   */
  kind: string;
  /**
   * The kind's canonical mode -- an exchanger's duty, rated or coupled -- absent for a kind without one.
   */
  mode?: string | null;
  /**
   * Which entry in symbols draws it.
   */
  symbolId: string;
  /**
   * declared, or inferred:I1, inferred:I2, inferred:I3, inferred:I7 (a pipe a connection line's properties made, D-110).
   */
  origin: string;
  /**
   * Where the declaration sits in the source: the component's line, or for an implicit pipe (I7) the connection line that made it; null for an inferred node, which has no text.
   */
  sourceSpan: Span | null;
  /**
   * The owning circuit (D-33; the losing side's under D-36).
   */
  circuit: string;
  /**
   * The equipment tag, display metadata only; null when the kind has no code or the component is inferred (D-34).
   */
  tag: string | null;
  /**
   * The design specification, by canonical parameter name, in declaration order of the kind's parameters.
   */
  parameters: {
    /**
     * One design parameter, with where it came from (D-02).
     */
    [k: string]: Parameter | undefined;
  };
  /**
   * The solved operating point, or null when the circuit is unsolved or states are omitted.
   */
  state: ComponentState | null;
  /**
   * Every port, in declaration order; a tank lists only its materialized ports.
   */
  ports: Port[];
}

export interface Span {
  /**
   * The zero-based offset.
   */
  start: number;
  /**
   * The length in UTF-16 code units.
   */
  length: number;
}

export interface Parameter {
  /**
   * The value in Unit, or null under FS2501.
   */
  value: number | null;
  /**
   * The canonical unit, or null for a dimensionless value.
   */
  unit: string | null;
  /**
   * stated, sized or default.
   */
  source: string;
  /**
   * Why a sized or default value is what it is; absent for a stated one.
   */
  basis?: string | null;
}

export interface ComponentState {
  /**
   * Mass flow through the component's first flow group, positive from its first port toward its second.
   */
  flow?: Quantity | null;
  /**
   * Temperature at the inlet port.
   */
  tIn?: Quantity | null;
  /**
   * Temperature of the stream leaving through the outlet port — the component's own outlet, not the node it discharges into. A valve passing 50 °C into a node where a colder return also arrives reports 50 °C; the node reports the mix. A port that is itself a mix (a mixing valve's common port, a vessel outlet) reports the node.
   */
  tOut?: Quantity | null;
  /**
   * Pressure at the inlet port, gauge in the canonical unit (D-26).
   */
  pIn?: Quantity | null;
  /**
   * Pressure at the outlet port, gauge in the canonical unit (D-26).
   */
  pOut?: Quantity | null;
  /**
   * Pressure drop inlet to outlet; negative across a pump.
   */
  dp?: Quantity | null;
  /**
   * Heat into the fluid on the first side, positive when the fluid gains.
   */
  power?: Quantity | null;
  /**
   * Mass flow on an exchanger's second side.
   */
  flow2?: Quantity | null;
  /**
   * Temperature at the second side's inlet.
   */
  tIn2?: Quantity | null;
  /**
   * Temperature at the second side's outlet.
   */
  tOut2?: Quantity | null;
  /**
   * A pump's delivered head.
   */
  head?: Quantity | null;
  /**
   * A node's temperature.
   */
  t?: Quantity | null;
  /**
   * A node's pressure, gauge in the canonical unit (D-26).
   */
  p?: Quantity | null;
  /**
   * Solved parameters the solver was asked to find -- a promoted kv, a sized head.
   */
  solved?: {
    /**
     * A solved value in its canonical unit.
     */
    [k: string]: Quantity | undefined;
  } | null;
  /**
   * A tank's layers, bottom to top, 1…N.
   */
  layers?: Layer[] | null;
}

export interface Quantity {
  /**
   * The value in , or null under FS2501.
   */
  value: number | null;
  /**
   * The canonical unit.
   */
  unit: string;
}

export interface Layer {
  /**
   * One-based, from the bottom.
   */
  index: number;
  /**
   * The layer's top as a fraction of the tank height, 0…1.
   */
  elevation: number;
  /**
   * The layer temperature.
   */
  t: Quantity;
}

export interface Port {
  /**
   * The port name.
   */
  name: string;
  /**
   * inlet, outlet or bidirectional.
   */
  role: string;
  /**
   * The component the port is wired to, or null when open.
   */
  connectedTo: string | null;
  /**
   * A tank port's normalized elevation; absent otherwise.
   */
  elevation?: number | null;
  /**
   * The tank layer the port meets; absent otherwise.
   */
  layer?: number | null;
}

export interface Symbol {
  /**
   * The id components reference, kind.variant.
   */
  id: string;
  /**
   * The bounding box as [x, y, width, height] in symbol units; what layout reasons on.
   */
  viewBox: number[];
  /**
   * What the canvas draws inside the box. Never executable.
   */
  primitives: Primitive[];
  /**
   * The default arrangement: each named port's anchor on the box edge and its outward direction.
   */
  portAnchors: {
    /**
     * Where a port meets its symbol, and which way a connection leaves it.
     */
    [k: string]: Anchor | undefined;
  };
  /**
   * Other complete arrangements of the same ports on the same box, by name; the renderer may pick one per instance, with a rotation, to shorten the connections it has to draw. Absent when there is one.
   */
  alternatives?: {
    [k: string]:
      | {
          /**
           * Where a port meets its symbol, and which way a connection leaves it.
           */
          [k: string]: Anchor | undefined;
        }
      | undefined;
  } | null;
  /**
   * Rules for indexed ports such as a tank's in{n}; absent for a fixed-port symbol.
   */
  indexedPortAnchors?: IndexedAnchor[] | null;
  /**
   * Where the label sits, [x, y].
   */
  labelAnchor: number[];
  /**
   * Which transforms the kind admits (28 A4, D-108): free turns by any quarter, mirrored or not; standing is never turned, only mirrored left-right, up-down or both (every exchanger); upright admits only the left-right mirror (a tank, whose layers are a vertical order); level admits every transform but stands vertical only where nothing level fits (a pump, D-113). A fact about the kind, never a preference.
   */
  transformClass?: string;
}

export interface Primitive {
  /**
   * rect, line, circle, polyline or polygon.
   */
  kind: string;
  /**
   * A rectangle's or circle's origin.
   */
  x?: number | null;
  /**
   * A rectangle's or circle's origin.
   */
  y?: number | null;
  /**
   * A rectangle's width.
   */
  width?: number | null;
  /**
   * A rectangle's height.
   */
  height?: number | null;
  /**
   * A circle's radius.
   */
  r?: number | null;
  /**
   * A line's start.
   */
  from?: number[] | null;
  /**
   * A line's end.
   */
  to?: number[] | null;
  /**
   * A polyline's or polygon's points, flattened [x0, y0, x1, y1, …].
   */
  points?: number[] | null;
  /**
   * What fills a closed shape: state for the active colour scale's slot (57), stroke for a solid mark in the line colour; absent for an outline.
   */
  fill?: string | null;
  /**
   * true for a dashed stroke; absent for a solid one.
   */
  dashed?: boolean | null;
}

export interface Anchor {
  /**
   * The point on the box edge, [x, y] in symbol units.
   */
  at: number[];
  /**
   * The outward unit vector a connection leaves along, [dx, dy] with y up (28 A1); rotates with the box. Absent for the wildcard anchor, whose direction the layout chooses.
   */
  direction?: number[] | null;
}

export interface IndexedAnchor {
  /**
   * The port name prefix, in or out.
   */
  prefix: string;
  /**
   * The box side the family sits on.
   */
  side: string;
  /**
   * The outward unit vector every anchor of the family leaves along, [dx, dy].
   */
  direction: number[];
  /**
   * Which port field gives the position along that side.
   */
  verticalCoordinate: string;
  /**
   * The smallest index the rule covers.
   */
  minIndex: number;
  /**
   * The largest index the rule covers.
   */
  maxIndex: number;
}

export interface Connection {
  /**
   * c{n}, by position in the model's connection list.
   */
  id: string;
  /**
   * Where it starts.
   */
  from: Endpoint;
  /**
   * Where it ends.
   */
  to: Endpoint;
  /**
   * forward, reverse or none: the solved direction relative to how it was written.
   */
  flow: string;
  /**
   * The solved flow along it, or null when unsolved.
   */
  state: ConnectionState | null;
}

export interface Endpoint {
  /**
   * The component id.
   */
  component: string;
  /**
   * The port name, or null for a node.
   */
  port: string | null;
}

export interface ConnectionState {
  /**
   * The mass flow in the written direction.
   */
  flow: Quantity;
}

export interface Layout {
  /**
   * Depth-first order from each pressure datum.
   */
  order: string[];
  /**
   * The heat-progression bands, left to right.
   */
  thermalStages: ThermalStage[];
  /**
   * Solved direction per connection id.
   */
  flow: {
    [k: string]: string | undefined;
  };
  /**
   * Pipe expansions.
   */
  groups: ComponentGroup[];
  /**
   * Instruments and controllers.
   */
  nonFlowElements: NonFlowElement[];
  /**
   * Owning circuit per component.
   */
  circuitOf: {
    [k: string]: string | undefined;
  };
  /**
   * Subcircuits sharing one parent, in declaration order.
   */
  distributionGroups: DistributionGroup[];
  /**
   * Components the language added.
   */
  inferred: string[];
  /**
   * The clearance every component keeps from every other, world units (D-103); the spacing directive or 0.5.
   */
  margin: number;
  /**
   * The bounds of the whole drawing as [x, y, width, height], world units, outer boxes and routes included.
   */
  extent: number[];
  /**
   * Where every component sits, in Order then the non-flow elements.
   */
  placements: Placement[];
  /**
   * Every connection's path, in connection order, then the instruments' signal lines.
   */
  routes: Route[];
}

export interface ThermalStage {
  /**
   * The band index.
   */
  rank: number;
  /**
   * source, conversion, storage, consumer or neutral.
   */
  role: string;
  /**
   * Members in graph order.
   */
  components: string[];
}

export interface ComponentGroup {
  /**
   * The declared pipe.
   */
  parentComponentId: string;
  /**
   * What lowering made of it.
   */
  children: string[];
}

export interface NonFlowElement {
  /**
   * Its id.
   */
  componentId: string;
  /**
   * The component it is drawn beside.
   */
  placementAnchorId: string;
  /**
   * What it reads.
   */
  measurementTargetId: string;
  /**
   * What it drives, or null for an instrument.
   */
  actuationTargetId: string | null;
  /**
   * Its position in the tab order.
   */
  navigationOrder: number;
}

export interface DistributionGroup {
  /**
   * The circuit owning the rails.
   */
  parentCircuit: string;
  /**
   * The branches, at least two.
   */
  members: string[];
}

export interface Placement {
  /**
   * The component.
   */
  componentId: string;
  /**
   * The symbol drawn inside Inner.
   */
  symbolId: string;
  /**
   * The symbol's box as placed, [x, y, width, height]; the renderer draws the strokes inside it.
   */
  inner: number[];
  /**
   * The inner box grown by the margin; no other component's inner box enters it.
   */
  outer: number[];
  /**
   * The quarter turn applied, clockwise degrees: 0, 90, 180 or 270.
   */
  rotation: number;
  /**
   * Whether the symbol is mirrored left-to-right before the turn.
   */
  mirrored: boolean;
  /**
   * default or one of the symbol's alternative arrangements (D-102).
   */
  arrangement: string;
  /**
   * Every port's anchor in world coordinates with its outward direction; a node's ports are #0, #1, …
   */
  anchors: {
    /**
     * Where a port meets its symbol, and which way a connection leaves it.
     */
    [k: string]: Anchor | undefined;
  };
  /**
   * Where the label sits, [x, y].
   */
  labelAt: number[];
  /**
   * computed; pinned is reserved for a placement the script states.
   */
  source: string;
  /**
   * The resolved style: the script's named or anonymous style (D-104); absent when the theme's defaults apply throughout.
   */
  style?: ResolvedStyle | null;
  /**
   * Where the component's representative value sits on the active colour scale, 0 to 1; null when not computed. The same as Scales[visualization.active].At.
   */
  scale: number | null;
  /**
   * The component's position on every available scale, keyed by property (D-117): the switcher needs no request.
   */
  scales: {
    /**
     * An element's place on one colour scale, each 0 to 1 or null where the element has no such value (57 invariant 5: neutral, never the low end).
     */
    [k: string]: ScalePosition | undefined;
  };
}

export interface ScalePosition {
  /**
   * The representative value: a node's own, a component's outlet (D-30).
   */
  at: number | null;
  /**
   * Where a gradient starts: a component's inlet, a route's first end.
   */
  from: number | null;
  /**
   * Where it ends: a component's outlet, a route's last end.
   */
  to: number | null;
}

export interface Route {
  /**
   * c{n} for a connection; {instrument}:measures or {controller}:actuates for a signal line.
   */
  id: string;
  /**
   * pipe or signal.
   */
  kind: string;
  /**
   * The draw order (28 C16): supply in front, return behind it, signal behind everything. A pipe is supply until the flow from a heat source has passed a losing side.
   */
  layer: string;
  /**
   * The orthogonal polyline, flattened [x0, y0, x1, y1, …]; the first and last points are the anchors.
   */
  points: number[];
  /**
   * Where this route passes behind another it crosses, flattened [x0, y0, …] in world units; the renderer breaks this route around each so the one in front runs through (28 C16).
   */
  hops: number[];
  /**
   * The resolved style, from the component the route leaves; absent when the theme's defaults apply throughout.
   */
  style?: ResolvedStyle | null;
  /**
   * The scale position at the start, for a gradient; null when not computed. The same as Scales[visualization.active].From.
   */
  scaleFrom: number | null;
  /**
   * The scale position at the end.
   */
  scaleTo: number | null;
  /**
   * The route's ends on every available scale, keyed by property (D-117); At is unused for a route.
   */
  scales: {
    /**
     * An element's place on one colour scale, each 0 to 1 or null where the element has no such value (57 invariant 5: neutral, never the low end).
     */
    [k: string]: ScalePosition | undefined;
  };
}

export interface Visualization {
  /**
   * The property the colour scale follows.
   */
  active: string;
  /**
   * The properties the switcher offers.
   */
  available: string[];
  /**
   * The scale for Active. The same as Scales[Active].
   */
  scale: Scale;
  /**
   * A scale per available property (D-117), each with its own domain, so switching needs no recompile (57 invariant 6).
   */
  scales: {
    /**
     * A colour scale.
     */
    [k: string]: Scale | undefined;
  };
}

export interface Scale {
  /**
   * The property.
   */
  property: string;
  /**
   * The legend title.
   */
  displayName: string;
  /**
   * The unit the domain is in.
   */
  unit: string;
  /**
   * sequential or diverging.
   */
  kind: string;
  /**
   * The range mapped to the scale's ends, or null before anything is solved.
   */
  domain: Domain | null;
  /**
   * Whether every element has the same value.
   */
  degenerate: boolean;
}

export interface Domain {
  /**
   * The low end, in the scale's unit.
   */
  min: number;
  /**
   * The high end, in the scale's unit.
   */
  max: number;
  /**
   * Whether the ends were rounded outward to legend ticks.
   */
  nice: boolean;
}

export interface Binding {
  /**
   * The name.
   */
  name: string;
  /**
   * The value in , or null when the binding is deferred to the solve.
   */
  value: number | null;
  /**
   * The canonical unit, or null when dimensionless or deferred.
   */
  unit: string | null;
  /**
   * The dimension's name, so the editor can filter completion by it (52); null for a dimensionless, an unnamed or a deferred binding.
   */
  dimension: string | null;
  /**
   * For an unnamed dimension, the SI spelling the value carries, shown dimmed by completion; otherwise null.
   */
  siUnit: string | null;
}

export interface Diagnostic {
  /**
   * The registry code.
   */
  code: string;
  /**
   * error, warning or info.
   */
  severity: string;
  /**
   * The rendered message.
   */
  message: string;
  /**
   * Where, or null when it is about no source text.
   */
  range: Range | null;
  /**
   * The component it is about, or null.
   */
  component: string | null;
  /**
   * A fix, or null.
   */
  suggestion: Suggestion | null;
  /**
   * Other places it is about.
   */
  related: Related[];
}

export interface Range {
  /**
   * Where it starts.
   */
  start: Position;
  /**
   * Where it ends, exclusive.
   */
  end: Position;
  /**
   * The zero-based character offset.
   */
  offset: number;
  /**
   * The length in UTF-16 code units.
   */
  length: number;
}

export interface Position {
  /**
   * The line.
   */
  line: number;
  /**
   * The column.
   */
  character: number;
}

export interface Suggestion {
  /**
   * What it does.
   */
  title: string;
  /**
   * What it replaces.
   */
  range: Range;
  /**
   * What it puts there.
   */
  newText: string;
}

export interface Related {
  /**
   * Why it is related.
   */
  message: string;
  /**
   * Where it is.
   */
  range: Range;
}

export interface Solve {
  /**
   * Whether the last pass converged.
   */
  converged: boolean;
  /**
   * Newton iterations over every sizing pass, retries included: the run's work, where a warm start's saving shows (A-4).
   */
  iterations: number;
  /**
   * The scaled residual norm at the end.
   */
  residualNorm: number;
  /**
   * Wall time, or null when the caller did not time it.
   */
  elapsedMs: number | null;
  /**
   * Outer-loop passes.
   */
  sizingPasses: number;
}

/**
 * The body of a 200 from compile and solve (42).
 */

export interface CompileResponse {
  /**
   * The model contract, or null when the script's language version is not supported.
   */
  model: ModelContract | null;
  /**
   * Diagnostics, only when Model is null; absent otherwise.
   */
  diagnostics?: Diagnostic[] | null;
  /**
   * Stage timings.
   */
  timings: Timings;
}

export interface Timings {
  /**
   * Lexing and parsing.
   */
  parseMs: number;
  /**
   * Binding: symbols, kinds, parameters, inference.
   */
  bindMs: number;
  /**
   * Lowering with sizing applied, before the solve; zero when nothing was lowered.
   */
  sizeMs: number;
  /**
   * The outer loop; zero when nothing was solved.
   */
  solveMs: number;
  /**
   * Request receipt to the response being built, layout and serialization included.
   */
  totalMs: number;
}

/**
 * The static description of the language, the body of GET /api/v1/metadata (42).
 */

export interface Metadata {
  /**
   * The REST major this document describes; the v1 in the path.
   */
  restMajor: number;
  /**
   * The model contract version compile returns.
   */
  contractVersion: string;
  /**
   * The language majors this build reads.
   */
  language: LanguageVersions;
  /**
   * Every component kind, in registry order.
   */
  kinds: Kind[];
  /**
   * Every dimension a parameter or property can have, with its units.
   */
  dimensions: Dimension[];
  /**
   * The symbol definitions the canvas draws with (D-24), the same records the model contract carries.
   */
  symbols: Symbol[];
  /**
   * Every live diagnostic code.
   */
  diagnostics: DiagnosticCode[];
  /**
   * Codes that were allocated and are no longer emitted, so a stale reference can be told from a typo.
   */
  retiredDiagnostics: RetiredCode[];
  /**
   * The catalogues a script may pin, with their exact versions.
   */
  catalogs: Catalog[];
  /**
   * The fluid property package and its version.
   */
  propertyBackend: VersionedId;
  /**
   * The ceilings a request may reach (07).
   */
  limits: Limits;
  /**
   * A URI to the generated function index (61).
   */
  docsIndex: string;
}

export interface LanguageVersions {
  /**
   * The major a new script is written in.
   */
  current: number;
  /**
   * Every major this build parses, including the current one.
   */
  supported: number[];
}

export interface Kind {
  /**
   * The canonical keyword.
   */
  keyword: string;
  /**
   * Other spellings the binder accepts and reads as the keyword.
   */
  aliases: string[];
  /**
   * The equipment-tag code (D-34), or null when the kind is not tagged.
   */
  tagCode: string | null;
  /**
   * Whether the kind drives flow, as a pump does.
   */
  drivesFlow: boolean;
  /**
   * Whether the kind observes rather than carries flow: a sensor or a controller.
   */
  isObserver: boolean;
  /**
   * The symbol that draws it, an id into Symbols.
   */
  symbolId: string;
  /**
   * The fixed ports, in declaration order.
   */
  ports: Port[];
  /**
   * Indexed port families such as a tank's in1..in16 (D-32).
   */
  portFamilies: PortFamily[];
  /**
   * The parameters, in the registry's order.
   */
  parameters: ParameterMeta[];
  /**
   * Indexed parameter families such as a tank's t{index}.
   */
  indexedParameters: IndexedParameter[];
  /**
   * The properties an expression can read, in the registry's order.
   */
  properties: PropertyMeta[];
  /**
   * Indexed property families.
   */
  indexedProperties: IndexedProperty[];
  /**
   * The parameter a control line may actuate, or null.
   */
  actuatedParameter: string | null;
  /**
   * The property a sensor of this kind measures, or null.
   */
  measuredProperty: string | null;
}

export interface PortFamily {
  /**
   * The name before the index, in for in1.
   */
  prefix: string;
  /**
   * The lowest index.
   */
  minIndex: number;
  /**
   * The highest index.
   */
  maxIndex: number;
  /**
   * inlet, outlet or bidirectional.
   */
  role: string;
  /**
   * The suffix of the parameter that places the port, or null.
   */
  levelParameterSuffix: string | null;
}

export interface ParameterMeta {
  /**
   * The canonical name.
   */
  name: string;
  /**
   * Other spellings the binder accepts.
   */
  aliases: string[];
  /**
   * quantity, symbol or reference.
   */
  valueKind: string;
  /**
   * The dimension's name, an entry in Dimensions; null for a synthesised dimension such as W/(m²·K), which Unit alone names.
   */
  dimension: string | null;
  /**
   * The unit a bare number means and values are reported in, or null for a dimensionless parameter.
   */
  unit: string | null;
  /**
   * The symbols a symbol-valued parameter accepts, such as a valve characteristic.
   */
  acceptedSymbols: string[];
  /**
   * What omitting it means: size, default or require (D-02).
   */
  omission: string;
  /**
   * The default as the script would write it, when the omission policy is default.
   */
  default: string | null;
  /**
   * Why that default, in the registry's words.
   */
  defaultBasis: string | null;
  /**
   * The range a stated value usually falls in, or null.
   */
  usualRange: RangeMeta | null;
  /**
   * The range outside which a stated value is an error, or null.
   */
  validRange: RangeMeta | null;
  /**
   * Whether a stated value must be a whole number.
   */
  wholeNumber: boolean;
  /**
   * Significant digits a display shows.
   */
  displayPrecision: number;
}

export interface RangeMeta {
  /**
   * The lowest value.
   */
  min: number;
  /**
   * The highest value.
   */
  max: number;
}

export interface IndexedParameter {
  /**
   * The name with {index} where the index goes.
   */
  pattern: string;
  /**
   * The lowest index.
   */
  minIndex: number;
  /**
   * The highest index, or null when another parameter sets it.
   */
  maxIndex: number | null;
  /**
   * The parameter that sets the highest index, or null.
   */
  maxIndexParameter: string | null;
  /**
   * What each member of the family is.
   */
  element: ParameterMeta;
}

export interface PropertyMeta {
  /**
   * The name after the dot in an expression.
   */
  name: string;
  /**
   * The dimension's name.
   */
  dimension: string | null;
  /**
   * The unit it is reported in.
   */
  unit: string;
  /**
   * When it has a value: declared, sized or solved.
   */
  availability: string;
}

export interface IndexedProperty {
  /**
   * The name with {index} where the index goes.
   */
  pattern: string;
  /**
   * The lowest index.
   */
  minIndex: number;
  /**
   * The highest index, or null when another parameter sets it.
   */
  maxIndex: number | null;
  /**
   * The parameter that sets the highest index, or null.
   */
  maxIndexParameter: string | null;
  /**
   * What each member of the family is.
   */
  element: PropertyMeta;
}

export interface Dimension {
  /**
   * The dimension's name, as parameters refer to it.
   */
  name: string;
  /**
   * The unit Core computes in.
   */
  siUnit: string;
  /**
   * The unit a bare number means and the wire reports in, or null.
   */
  canonicalUnit: string | null;
  /**
   * Every unit symbol accepted for this dimension.
   */
  units: string[];
}

export interface DiagnosticCode {
  /**
   * The code, FS1302.
   */
  code: string;
  /**
   * error, warning or info.
   */
  severity: string;
  /**
   * The subject the code belongs to.
   */
  area: string;
  /**
   * The message with its {placeholders} unfilled.
   */
  message: string;
  /**
   * The placeholder names, in order.
   */
  arguments: string[];
}

export interface RetiredCode {
  /**
   * The code.
   */
  code: string;
  /**
   * Why it was withdrawn and what replaced it.
   */
  reason: string;
}

export interface Catalog {
  /**
   * The id as written after catalog.
   */
  id: string;
  /**
   * The exact version.
   */
  version: string;
  /**
   * The standard the rows are drawn from, or null.
   */
  standard: string | null;
}

export interface Limits {
  /**
   * The largest script accepted, bytes of UTF-8; over it is 413.
   */
  sourceBytes: number;
  /**
   * The most component declarations; over it is FS4601.
   */
  declarations: number;
  /**
   * The most tokens; over it is FS4601.
   */
  tokens: number;
  /**
   * The most solver unknowns; over it is FS4601.
   */
  unknowns: number;
}
