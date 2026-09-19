# The model contract

Everything FluidScript computes about a script arrives at the editor, the diagram, the hover panel
and the console as one JSON document: the *model contract*. If you read the API, write a tool that
consumes it, or ask an agent to improve a design, this is the shape you are reading.

You never write it. It is what a script becomes.

## What is in it

- **What you wrote**, resolved: every circuit, every component with its parameters, every connection.
- **Where each number came from.** A parameter is `stated` (yours), `sized` (a rule's, or the
  solver's, with a `basis` sentence saying why), or `default` (a placeholder with a `basis` saying
  what it stands in for). This is the field an agent needs most: it tells your constraints from the
  tool's guesses.
- **The operating point**, once solved: flows, temperatures, pressures and duties per component and
  per connection, and a pump's delivered head. Unsolved, every `state` is `null` and the diagram
  still draws.
- **How to draw it**: the finished layout -- every component's box, turned and arranged, every
  pipe's polyline, every label's position, in world units -- with the hints it was solved from
  ([How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md)), each kind's symbol
  to draw inside its box, and every [`style`](style.md) resolved to colours, widths and patterns.
- **What the tool has to say**: every diagnostic, with its position in two forms and any fix it offers.
- **What produced it**: the script's hash, the language version, the catalogue and property backend
  versions, and the atmosphere gauge pressures are relative to.

## Conventions

- Field names are `camelCase`.
- **Every number is in the script's own unit for its dimension**: kW, °C, kPa (gauge), kg/s, dm³ —
  never SI — and sits beside a `unit` field that says so. A head is in m and a Kv in m³/h.
- Values carry six significant digits. Anything smaller than a nano-unit is written as `0`.
- `null` means *not computed* — an unsolved circuit's `state`. A field that does not apply — a pump's
  `power`, a stated value's `basis` — is simply absent.
- `contractVersion` is `major.minor`. A minor bump only adds fields; a major bump is anything a
  consumer could misread, including a changed unit. The frontend refuses to render on a major
  mismatch rather than draw a diagram from numbers it is misinterpreting.
- Payloads over 1 MiB arrive without per-component states and with `FS2502`; `statesOmitted` on each
  circuit says so, and the layout is still whole.

## Symbols

Every component names a symbol, and `symbols` carries each definition once: a bounding box in symbol
units, named port anchors on the box edge -- each with the outward direction a connection leaves it
in -- the strokes inside, and where the label sits. The canvas scales the box to the diagram and draws
the strokes; the layout engine and the exporter work from the box and the anchors alone, so a symbol
whose strokes change without its box changing moves nothing
([How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md)).

A symbol is placed by turning its box in quarter turns, and the anchor directions turn with it: a
pump's `out` faces east in the definition and faces south once the pump sits on a downward run. Some
symbols also offer `alternatives` -- other complete arrangements of the same ports on the same box.
The exchanger's default is the *through-pass*, each side entering at one end and leaving at the other,
primary on the left flank and secondary on the right; its alternative `u` brings each side in and out
on its own flank, which is what a substation drawn with the primary to the left and the secondary to
the right wants. Which turns a kind admits is fixed by the kind -- an exchanger stands, a tank
stands upright, a pump and a valve turn freely -- and within that the diagram picks the arrangement
and the rotation for each instance so that its ports face the pipes that reach them; the
definition offers, it does not choose.

The glyphs follow the notation an engineering office draws by hand -- ISO 10628 for the process
diagram, ISO 14617 for the equipment symbols, ANSI/ISA-5.1 for the instrument bubbles -- as this
project reads them: a filled dot for a junction, the line itself for a pipe, a crossed rectangle for an
exchanger with its second side entering at the top, opposed triangles for a valve with a general
actuator drawn as a stem and bar, three triangles meeting at the centre for a three-way valve, a
circle with a triangle pointing the flow's way for a pump, a tall vessel for a tank, and a circle for
an instrument -- dashed when it is a controller. The standards' own figures are paywalled and were not
reproduced; where a glyph here differs from your office standard, the box and anchors are what the
diagram depends on and the strokes can be changed without moving anything.

A stroke with `fill: "state"` is the slot the active colour scale paints
([`show`](show.md)); one with `fill: "stroke"` is a solid mark in the line colour; the rest are
outlines. What the strokes do not carry is drawn by the canvas from the state: a valve's position bar,
a tank's layer bands, an exchanger's heat arrow, badges, and the sized-versus-stated marker.

<!-- BEGIN GENERATED: symbol-catalog -->
| Symbol | Box `[x, y, w, h]` | Port anchors, facing | Strokes | Label at |
|---|---|---|---|---|
| `node.junction` | `-0.1, -0.1, 0.2, 0.2` | `*` (0, 0) | circle (solid) | (0, 0.3) |
| `pipe.standard` | `-0.5, -0.1, 1, 0.2` | `in` (-0.5, 0) ←, `out` (0.5, 0) → | line | (0, 0.3) |
| `heat_exchanger.standard` | `-0.25, -0.5, 0.5, 1` | `in` (-0.15, 0.5) ↑, `in2` (0.15, -0.5) ↓, `out` (-0.15, -0.5) ↓, `out2` (0.15, 0.5) ↑<br>*or `u`:* `in` (-0.25, 0.3) ←, `in2` (0.25, -0.3) →, `out` (-0.25, -0.3) ←, `out2` (0.25, 0.3) → | rect (state fill), 2 lines | (0, 0.65) |
| `valve.standard` | `-0.5, -0.3, 1, 0.6` | `in` (-0.5, 0) ←, `out` (0.5, 0) → | 2 polygons (state fill), 4 lines | (0, 0.45) |
| `three_way_valve.standard` | `-0.5, -0.5, 1, 1` | `a` (0, 0.5) ↑, `ab` (0, -0.5) ↓, `b` (-0.5, 0) ←<br>*or `swapped`:* `a` (-0.5, 0) ←, `ab` (0, -0.5) ↓, `b` (0, 0.5) ↑ | 3 polygons (state fill), 5 lines | (0, 0.65) |
| `pump.standard` | `-0.5, -0.5, 1, 1` | `in` (-0.5, 0) ←, `out` (0.5, 0) → | circle (state fill), polygon (solid), 2 lines | (0, 0.65) |
| `tank.stratified` | `-0.5, -0.8, 1, 1.6` | `in{1..16}` on the west at `port.elevation` ←, `out{1..16}` on the east at `port.elevation` → | rect (state fill) | (0, 0.95) |
| `t_sensor.standard` | `-0.3, -0.3, 0.6, 0.6` | `*` (0, 0) | circle | (0, 0) |
| `p_sensor.standard` | `-0.3, -0.3, 0.6, 0.6` | `*` (0, 0) | circle | (0, 0) |
| `flow_sensor.standard` | `-0.3, -0.3, 0.6, 0.6` | `*` (0, 0) | circle | (0, 0) |
| `controller.standard` | `-0.3, -0.3, 0.6, 0.6` | `*` (0, 0) | circle (dashed) | (0, 0) |
<!-- END GENERATED: symbol-catalog -->

## Fields

Generated from the contract's own definitions; the first table is the document, the rest are what
it reaches.

<!-- BEGIN GENERATED: contract-fields -->
### `ModelContract`

The one serialized shape every consumer receives (`26`).

| Field | Type | Meaning |
|---|---|---|
| `contractVersion` | string | The contract version the producing Core implements, `major.minor`. |
| `provenance` | [`Provenance`](#provenance) | What produced this: the source, the language, the catalogue, the property backend. |
| `project` | [`Project`](#project) or `null` | The `project` line, absent when the script has none (`D-37`). Absent when not applicable. |
| `style` | [`Style`](#style) | Presentation Core carries and never interprets. |
| `circuits` | array of [`Circuit`](#circuit) | Every circuit, in declaration order; never empty (`D-33`). |
| `pressureDatums` | array of string | One pressure datum per hydraulically connected part, not per circuit. |
| `components` | array of [`Component`](#component) | Every graph component, in graph order. |
| `symbols` | array of [`Symbol`](#symbol) | The symbol definitions the components reference (`D-20`, `D-24`). |
| `connections` | array of [`Connection`](#connection) | Every adjacency, in the model's connection order, keyed `c{n}`. |
| `layout` | [`Layout`](#layout) | The layout hints, serialized from `25`'s contract field for field. |
| `visualization` | [`Visualization`](#visualization) | The `show` directive's resolution (`57`). |
| `bindings` | array of [`Binding`](#binding) | Evaluated `let` values. |
| `diagnostics` | array of [`Diagnostic`](#diagnostic) | Every diagnostic the pipeline produced, ordered by severity then offset (`44`). |
| `solve` | [`Solve`](#solve) or `null` | What the solve did, or `null` when nothing was solved. |

### `Provenance`

What produced the payload.

| Field | Type | Meaning |
|---|---|---|
| `sourceHash` | string | SHA-256 of the source text, as `sha256:` and 64 hex digits. |
| `languageMajor` | integer | The language major version the script declared. |
| `catalog` | [`VersionedId`](#versionedid) | The pipe catalogue sizes were drawn from. |
| `propertyBackend` | [`VersionedId`](#versionedid) | The fluid property package. |
| `atmosphereKPaAbsolute` | number | The atmosphere gauge pressures are relative to, kPa absolute (`D-26`). |

### `Project`

The `project` line.

| Field | Type | Meaning |
|---|---|---|
| `name` | string or `null` | The project name. |
| `defaultMode` | string or `null` | The default solve mode, `steady`, `transient` or `null`. |

### `Style`

The script's presentation directives, resolved (`D-104`).

| Field | Type | Meaning |
|---|---|---|
| `tokens` | array of string | The applied `style` tokens as written. |
| `spacing` | number or `null` | The `spacing` value in world units, or `null` (`D-37`). |
| `default` | [`ResolvedStyle`](#resolvedstyle) | The project-level style, applied where a circuit states none. |
| `named` | object of [`ResolvedStyle`](#resolvedstyle) | The named styles, `style name = …`, resolved, for an editor to list. |

### `Circuit`

One circuit.

| Field | Type | Meaning |
|---|---|---|
| `name` | string | The name as written. |
| `number` | integer | The number, stated or resolved. |
| `numberIsExplicit` | boolean | Whether the script wrote the number; the printer needs this. |
| `substance` | string | The fluid keyword. |
| `mode` | string | The solve mode: `steady` or `transient`. |
| `role` | string or `null` | The resolved role's canonical name, or `null` for a name the registry does not know (`D-35`). |
| `parentCircuit` | string or `null` | The parent circuit, or `null` when this one stands alone (`D-33`). |
| `inletAnchorId` | string or `null` | The parent component this circuit takes flow from. |
| `outletAnchorId` | string or `null` | The parent component this circuit returns flow to. |
| `solved` | boolean | Whether every component in this circuit has a state (invariant 7). |
| `statesOmitted` | boolean | True only alongside `FS2502`. |

### `Component`

One graph component.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The stable id (`25`). |
| `kind` | string | The script keyword for the kind. |
| `mode` | string or `null` | The kind's canonical mode -- an exchanger's `duty`, `rated` or `coupled` -- absent for a kind without one. Absent when not applicable. |
| `symbolId` | string | Which entry in `symbols` draws it. |
| `origin` | string | `declared`, or `inferred:I1`, `inferred:I2`, `inferred:I3`, `inferred:I7` (a pipe a connection line's properties made, `D-110`). |
| `sourceSpan` | [`Span`](#span) or `null` | Where the declaration sits in the source: the component's line, or for an implicit pipe (I7) the connection line that made it; `null` for an inferred node, which has no text. |
| `circuit` | string | The owning circuit (`D-33`; the losing side's under `D-36`). |
| `tag` | string or `null` | The equipment tag, display metadata only; `null` when the kind has no code or the component is inferred (`D-34`). |
| `parameters` | object of [`Parameter`](#parameter) | The design specification, by canonical parameter name, in declaration order of the kind's parameters. |
| `state` | [`ComponentState`](#componentstate) or `null` | The solved operating point, or `null` when the circuit is unsolved or states are omitted. |
| `ports` | array of [`Port`](#port) | Every port, in declaration order; a tank lists only its materialized ports. |

### `Symbol`

A symbol definition in a normalized box (`D-20`, `D-102`).

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The id components reference, `kind.variant`. |
| `viewBox` | array of number | The bounding box as `[x, y, width, height]` in symbol units; what layout reasons on. |
| `primitives` | array of [`Primitive`](#primitive) | What the canvas draws inside the box. Never executable. |
| `portAnchors` | object of [`Anchor`](#anchor) | The default arrangement: each named port's anchor on the box edge and its outward direction. |
| `alternatives` | object of object of [`Anchor`](#anchor) or `null` | Other complete arrangements of the same ports on the same box, by name; the renderer may pick one per instance, with a rotation, to shorten the connections it has to draw. Absent when there is one. Absent when not applicable. |
| `indexedPortAnchors` | array of [`IndexedAnchor`](#indexedanchor) or `null` | Rules for indexed ports such as a tank's `in{n}`; absent for a fixed-port symbol. Absent when not applicable. |
| `labelAnchor` | array of number | Where the label sits, `[x, y]`. |
| `transformClass` | string | Which transforms the kind admits (`28` A4, `D-108`): `free` turns by any quarter, mirrored or not; `standing` is never turned, only mirrored left-right, up-down or both (every exchanger); `upright` admits only the left-right mirror (a tank, whose layers are a vertical order); `level` admits every transform but stands vertical only where nothing level fits (a pump, `D-113`). A fact about the kind, never a preference. |

### `Connection`

One adjacency.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | `c{n}`, by position in the model's connection list. |
| `from` | [`Endpoint`](#endpoint) | Where it starts. |
| `to` | [`Endpoint`](#endpoint) | Where it ends. |
| `flow` | string | `forward`, `reverse` or `none`: the solved direction relative to how it was written. |
| `state` | [`ConnectionState`](#connectionstate) or `null` | The solved flow along it, or `null` when unsolved. |

### `Layout`

`25`'s hints, field for field.

| Field | Type | Meaning |
|---|---|---|
| `order` | array of string | Depth-first order from each pressure datum. |
| `thermalStages` | array of [`ThermalStage`](#thermalstage) | The heat-progression bands, left to right. |
| `flow` | object of string | Solved direction per connection id. |
| `groups` | array of [`ComponentGroup`](#componentgroup) | Pipe expansions. |
| `nonFlowElements` | array of [`NonFlowElement`](#nonflowelement) | Instruments and controllers. |
| `circuitOf` | object of string | Owning circuit per component. |
| `distributionGroups` | array of [`DistributionGroup`](#distributiongroup) | Subcircuits sharing one parent, in declaration order. |
| `inferred` | array of string | Components the language added. |
| `margin` | number | The clearance every component keeps from every other, world units (`D-103`); the `spacing` directive or 0.5. |
| `extent` | array of number | The bounds of the whole drawing as `[x, y, width, height]`, world units, outer boxes and routes included. |
| `placements` | array of [`Placement`](#placement) | Where every component sits, in `Order` then the non-flow elements. |
| `routes` | array of [`Route`](#route) | Every connection's path, in connection order, then the instruments' signal lines. |

### `Visualization`

The `show` directive resolved (`57`).

| Field | Type | Meaning |
|---|---|---|
| `active` | string | The property the colour scale follows. |
| `available` | array of string | The properties the switcher offers. |
| `scale` | [`Scale`](#scale) | The scale for `Active`. The same as `Scales[Active]`. |
| `scales` | object of [`Scale`](#scale) | A scale per available property (`D-117`), each with its own domain, so switching needs no recompile (`57` invariant 6). |

### `Binding`

An evaluated `let`.

| Field | Type | Meaning |
|---|---|---|
| `name` | string | The name. |
| `value` | number or `null` | The value in `unit`, or `null` when the binding is deferred to the solve. |
| `unit` | string or `null` | The canonical unit, or `null` when dimensionless or deferred. |
| `dimension` | string or `null` | The dimension's name, so the editor can filter completion by it (`52`); `null` for a dimensionless, an unnamed or a deferred binding. |
| `siUnit` | string or `null` | For an unnamed dimension, the SI spelling the value carries, shown dimmed by completion; otherwise `null`. |

### `Diagnostic`

One diagnostic (`44`).

| Field | Type | Meaning |
|---|---|---|
| `code` | string | The registry code. |
| `severity` | string | `error`, `warning` or `info`. |
| `message` | string | The rendered message. |
| `range` | [`Range`](#range) or `null` | Where, or `null` when it is about no source text. |
| `component` | string or `null` | The component it is about, or `null`. |
| `suggestion` | [`Suggestion`](#suggestion) or `null` | A fix, or `null`. |
| `related` | array of [`Related`](#related) | Other places it is about. |

### `Solve`

What the solve did.

| Field | Type | Meaning |
|---|---|---|
| `converged` | boolean | Whether the last pass converged. |
| `iterations` | integer | Newton iterations over every sizing pass, retries included: the run's work, where a warm start's saving shows (`A-4`). |
| `residualNorm` | number | The scaled residual norm at the end. |
| `elapsedMs` | integer or `null` | Wall time, or `null` when the caller did not time it. |
| `sizingPasses` | integer | Outer-loop passes. |

### `VersionedId`

A named thing and its version.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The stable identifier. |
| `version` | string | The exact version string. |

### `ResolvedStyle`

A style with every name resolved (`D-104`). A `null` colour or width is the theme's default.

| Field | Type | Meaning |
|---|---|---|
| `stroke` | string or `null` | The stroke colour, `#rrggbb`, or `null` for the theme's. |
| `strokeWidth` | number or `null` | The stroke width in CSS pixels at scale 1, or `null` for the theme's. |
| `pattern` | string | `solid`, `dashed`, `dotted` or `dash-dot`. |
| `fill` | string or `null` | The static fill colour, or `null` for none; the colour scale paints over it while `show` is active. |
| `corner` | string or `null` | `fillet`, `round`, `sharp` or `null` for the theme's. |

### `Span`

A character span in the source.

| Field | Type | Meaning |
|---|---|---|
| `start` | integer | The zero-based offset. |
| `length` | integer | The length in UTF-16 code units. |

### `Parameter`

One design parameter, with where it came from (`D-02`).

| Field | Type | Meaning |
|---|---|---|
| `value` | number or `null` | The value in `Unit`, or `null` under `FS2501`. |
| `unit` | string or `null` | The canonical unit, or `null` for a dimensionless value. |
| `source` | string | `stated`, `sized` or `default`. |
| `basis` | string or `null` | Why a sized or default value is what it is; absent for a stated one. Absent when not applicable. |

### `ComponentState`

A component's solved operating point. Fields a kind does not have are absent.

| Field | Type | Meaning |
|---|---|---|
| `flow` | [`Quantity`](#quantity) or `null` | Mass flow through the component's first flow group, positive from its first port toward its second. Absent when not applicable. |
| `tIn` | [`Quantity`](#quantity) or `null` | Temperature at the inlet port. Absent when not applicable. |
| `tOut` | [`Quantity`](#quantity) or `null` | Temperature of the stream leaving through the outlet port — the component's own outlet, not the node it discharges into. A valve passing 50 °C into a node where a colder return also arrives reports 50 °C; the node reports the mix. A port that is itself a mix (a mixing valve's common port, a vessel outlet) reports the node. Absent when not applicable. |
| `pIn` | [`Quantity`](#quantity) or `null` | Pressure at the inlet port, gauge in the canonical unit (`D-26`). Absent when not applicable. |
| `pOut` | [`Quantity`](#quantity) or `null` | Pressure at the outlet port, gauge in the canonical unit (`D-26`). Absent when not applicable. |
| `dp` | [`Quantity`](#quantity) or `null` | Pressure drop inlet to outlet; negative across a pump. Absent when not applicable. |
| `power` | [`Quantity`](#quantity) or `null` | Heat into the fluid on the first side, positive when the fluid gains. Absent when not applicable. |
| `flow2` | [`Quantity`](#quantity) or `null` | Mass flow on an exchanger's second side. Absent when not applicable. |
| `tIn2` | [`Quantity`](#quantity) or `null` | Temperature at the second side's inlet. Absent when not applicable. |
| `tOut2` | [`Quantity`](#quantity) or `null` | Temperature at the second side's outlet. Absent when not applicable. |
| `head` | [`Quantity`](#quantity) or `null` | A pump's delivered head. Absent when not applicable. |
| `t` | [`Quantity`](#quantity) or `null` | A node's temperature. Absent when not applicable. |
| `p` | [`Quantity`](#quantity) or `null` | A node's pressure, gauge in the canonical unit (`D-26`). Absent when not applicable. |
| `solved` | object of [`Quantity`](#quantity) or `null` | Solved parameters the solver was asked to find -- a promoted `kv`, a sized `head`. Absent when not applicable. |
| `layers` | array of [`Layer`](#layer) or `null` | A tank's layers, bottom to top, `1…N`. Absent when not applicable. |

### `Port`

One port.

| Field | Type | Meaning |
|---|---|---|
| `name` | string | The port name. |
| `role` | string | `inlet`, `outlet` or `bidirectional`. |
| `connectedTo` | string or `null` | The component the port is wired to, or `null` when open. |
| `elevation` | number or `null` | A tank port's normalized elevation; absent otherwise. Absent when not applicable. |
| `layer` | integer or `null` | The tank layer the port meets; absent otherwise. Absent when not applicable. |

### `Primitive`

One drawing primitive; the fields a kind does not use are absent.

| Field | Type | Meaning |
|---|---|---|
| `kind` | string | `rect`, `line`, `circle`, `polyline` or `polygon`. |
| `x` | number or `null` | A rectangle's or circle's origin. Absent when not applicable. |
| `y` | number or `null` | A rectangle's or circle's origin. Absent when not applicable. |
| `width` | number or `null` | A rectangle's width. Absent when not applicable. |
| `height` | number or `null` | A rectangle's height. Absent when not applicable. |
| `r` | number or `null` | A circle's radius. Absent when not applicable. |
| `from` | array of number or `null` | A line's start. Absent when not applicable. |
| `to` | array of number or `null` | A line's end. Absent when not applicable. |
| `points` | array of number or `null` | A polyline's or polygon's points, flattened `[x0, y0, x1, y1, …]`. Absent when not applicable. |
| `fill` | string or `null` | What fills a closed shape: `state` for the active colour scale's slot (`57`), `stroke` for a solid mark in the line colour; absent for an outline. Absent when not applicable. |
| `dashed` | boolean or `null` | `true` for a dashed stroke; absent for a solid one. Absent when not applicable. |

### `Anchor`

Where a port meets its symbol, and which way a connection leaves it.

| Field | Type | Meaning |
|---|---|---|
| `at` | array of number | The point on the box edge, `[x, y]` in symbol units. |
| `direction` | array of number or `null` | The outward unit vector a connection leaves along, `[dx, dy]` with `y` up (`28` A1); rotates with the box. Absent for the wildcard anchor, whose direction the layout chooses. Absent when not applicable. |

### `IndexedAnchor`

An anchor rule for an indexed port family.

| Field | Type | Meaning |
|---|---|---|
| `prefix` | string | The port name prefix, `in` or `out`. |
| `side` | string | The box side the family sits on. |
| `direction` | array of number | The outward unit vector every anchor of the family leaves along, `[dx, dy]`. |
| `verticalCoordinate` | string | Which port field gives the position along that side. |
| `minIndex` | integer | The smallest index the rule covers. |
| `maxIndex` | integer | The largest index the rule covers. |

### `Endpoint`

One end of a connection.

| Field | Type | Meaning |
|---|---|---|
| `component` | string | The component id. |
| `port` | string or `null` | The port name, or `null` for a node. |

### `ConnectionState`

A connection's solved state.

| Field | Type | Meaning |
|---|---|---|
| `flow` | [`Quantity`](#quantity) | The mass flow in the written direction. |

### `ThermalStage`

One thermal stage.

| Field | Type | Meaning |
|---|---|---|
| `rank` | integer | The band index. |
| `role` | string | `source`, `conversion`, `storage`, `consumer` or `neutral`. |
| `components` | array of string | Members in graph order. |

### `ComponentGroup`

A pipe expansion.

| Field | Type | Meaning |
|---|---|---|
| `parentComponentId` | string | The declared pipe. |
| `children` | array of string | What lowering made of it. |

### `NonFlowElement`

An instrument or controller.

| Field | Type | Meaning |
|---|---|---|
| `componentId` | string | Its id. |
| `placementAnchorId` | string | The component it is drawn beside. |
| `measurementTargetId` | string | What it reads. |
| `actuationTargetId` | string or `null` | What it drives, or `null` for an instrument. |
| `navigationOrder` | integer | Its position in the tab order. |

### `DistributionGroup`

A distribution group.

| Field | Type | Meaning |
|---|---|---|
| `parentCircuit` | string | The circuit owning the rails. |
| `members` | array of string | The branches, at least two. |

### `Placement`

One component's place in the drawing (`D-103`). World units: a pump is 1×1, `y` grows upward and a box's `y` is its bottom edge (`28` A1).

| Field | Type | Meaning |
|---|---|---|
| `componentId` | string | The component. |
| `symbolId` | string | The symbol drawn inside `Inner`. |
| `inner` | array of number | The symbol's box as placed, `[x, y, width, height]`; the renderer draws the strokes inside it. |
| `outer` | array of number | The inner box grown by the margin; no other component's inner box enters it. |
| `rotation` | integer | The quarter turn applied, clockwise degrees: 0, 90, 180 or 270. |
| `mirrored` | boolean | Whether the symbol is mirrored left-to-right before the turn. |
| `arrangement` | string | `default` or one of the symbol's alternative arrangements (`D-102`). |
| `anchors` | object of [`Anchor`](#anchor) | Every port's anchor in world coordinates with its outward direction; a node's ports are `#0`, `#1`, … |
| `labelAt` | array of number | Where the label sits, `[x, y]`. |
| `source` | string | `computed`; `pinned` is reserved for a placement the script states. |
| `style` | [`ResolvedStyle`](#resolvedstyle) or `null` | The resolved style: the script's named or anonymous style (`D-104`); absent when the theme's defaults apply throughout. Absent when not applicable. |
| `scale` | number or `null` | Where the component's representative value sits on the active colour scale, 0 to 1; `null` when not computed. The same as `Scales[visualization.active].At`. |
| `scales` | object of [`ScalePosition`](#scaleposition) | The component's position on every available scale, keyed by property (`D-117`): the switcher needs no request. |

### `Route`

One connection's path.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | `c{n}` for a connection; `{instrument}:measures` or `{controller}:actuates` for a signal line. |
| `kind` | string | `pipe` or `signal`. |
| `layer` | string | The draw order (`28` C16): `supply` in front, `return` behind it, `signal` behind everything. A pipe is supply until the flow from a heat source has passed a losing side. |
| `points` | array of number | The orthogonal polyline, flattened `[x0, y0, x1, y1, …]`; the first and last points are the anchors. |
| `hops` | array of number | Where this route passes behind another it crosses, flattened `[x0, y0, …]` in world units; the renderer breaks this route around each so the one in front runs through (`28` C16). |
| `style` | [`ResolvedStyle`](#resolvedstyle) or `null` | The resolved style, from the component the route leaves; absent when the theme's defaults apply throughout. Absent when not applicable. |
| `scaleFrom` | number or `null` | The scale position at the start, for a gradient; `null` when not computed. The same as `Scales[visualization.active].From`. |
| `scaleTo` | number or `null` | The scale position at the end. |
| `scales` | object of [`ScalePosition`](#scaleposition) | The route's ends on every available scale, keyed by property (`D-117`); `At` is unused for a route. |

### `Scale`

A colour scale.

| Field | Type | Meaning |
|---|---|---|
| `property` | string | The property. |
| `displayName` | string | The legend title. |
| `unit` | string | The unit the domain is in. |
| `kind` | string | `sequential` or `diverging`. |
| `domain` | [`Domain`](#domain) or `null` | The range mapped to the scale's ends, or `null` before anything is solved. |
| `degenerate` | boolean | Whether every element has the same value. |

### `Range`

A source range in both forms, from one line index.

| Field | Type | Meaning |
|---|---|---|
| `start` | [`Position`](#position) | Where it starts. |
| `end` | [`Position`](#position) | Where it ends, exclusive. |
| `offset` | integer | The zero-based character offset. |
| `length` | integer | The length in UTF-16 code units. |

### `Suggestion`

A concrete fix.

| Field | Type | Meaning |
|---|---|---|
| `title` | string | What it does. |
| `range` | [`Range`](#range) | What it replaces. |
| `newText` | string | What it puts there. |

### `Related`

A related location.

| Field | Type | Meaning |
|---|---|---|
| `message` | string | Why it is related. |
| `range` | [`Range`](#range) | Where it is. |

### `Quantity`

A solved value in its canonical unit.

| Field | Type | Meaning |
|---|---|---|
| `value` | number or `null` | The value in `unit`, or `null` under `FS2501`. |
| `unit` | string | The canonical unit. |

### `Layer`

One tank layer.

| Field | Type | Meaning |
|---|---|---|
| `index` | integer | One-based, from the bottom. |
| `elevation` | number | The layer's top as a fraction of the tank height, `0…1`. |
| `t` | [`Quantity`](#quantity) | The layer temperature. |

### `ScalePosition`

An element's place on one colour scale, each 0 to 1 or `null` where the element has no such value (`57` invariant 5: neutral, never the low end).

| Field | Type | Meaning |
|---|---|---|
| `at` | number or `null` | The representative value: a node's own, a component's outlet (`D-30`). |
| `from` | number or `null` | Where a gradient starts: a component's inlet, a route's first end. |
| `to` | number or `null` | Where it ends: a component's outlet, a route's last end. |

### `Domain`

A scale domain.

| Field | Type | Meaning |
|---|---|---|
| `min` | number | The low end, in the scale's unit. |
| `max` | number | The high end, in the scale's unit. |
| `nice` | boolean | Whether the ends were rounded outward to legend ticks. |

### `Position`

A line and column, both zero-based; the column counts UTF-16 code units.

| Field | Type | Meaning |
|---|---|---|
| `line` | integer | The line. |
| `character` | integer | The column. |
<!-- END GENERATED: contract-fields -->

## See also

[`show`](show.md) · [Diagnostics](diagnostics.md) · [How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md)
