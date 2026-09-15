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
- **How to draw it**: the layout hints ([How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md))
  and each kind's symbol box and port anchors.
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

Presentation tokens, verbatim.

| Field | Type | Meaning |
|---|---|---|
| `tokens` | array of string | The `style` tokens as written. |
| `spacing` | number or `null` | The `spacing` value in world units, or `null` (`D-37`). |

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
| `supplyAnchorId` | string or `null` | The parent component this circuit takes flow from. |
| `returnAnchorId` | string or `null` | The parent component this circuit returns flow to. |
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
| `origin` | string | `declared`, or `inferred:I1`, `inferred:I2`, `inferred:I3`. |
| `sourceSpan` | [`Span`](#span) or `null` | Where the declaration sits in the source; `null` for an inferred component. |
| `circuit` | string | The owning circuit (`D-33`; the losing side's under `D-36`). |
| `tag` | string or `null` | The equipment tag, display metadata only; `null` when the kind has no code or the component is inferred (`D-34`). |
| `parameters` | object of [`Parameter`](#parameter) | The design specification, by canonical parameter name, in declaration order of the kind's parameters. |
| `state` | [`ComponentState`](#componentstate) or `null` | The solved operating point, or `null` when the circuit is unsolved or states are omitted. |
| `ports` | array of [`Port`](#port) | Every port, in declaration order; a tank lists only its materialized ports. |

### `Symbol`

A symbol definition in a normalized box (`D-20`).

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The id components reference, `kind.variant`. |
| `viewBox` | array of number | The bounding box as `[x, y, width, height]` in symbol units; what layout reasons on. |
| `primitives` | array of [`Primitive`](#primitive) | What the canvas draws inside the box. Never executable. |
| `portAnchors` | object of array of number | Named port anchors as `[x, y]` on the box edge. |
| `indexedPortAnchors` | array of [`IndexedAnchor`](#indexedanchor) or `null` | Rules for indexed ports such as a tank's `in{n}`; absent for a fixed-port symbol. Absent when not applicable. |
| `labelAnchor` | array of number | Where the label sits, `[x, y]`. |

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
| `rank` | object of integer | Hops from the nearest loop, for non-loop components only. |
| `thermalStages` | array of [`ThermalStage`](#thermalstage) | The heat-progression bands, left to right. |
| `flow` | object of string | Solved direction per connection id. |
| `portSides` | object of string | Which box side each port leaves from, keyed `component.port`. |
| `loops` | array of array of string | Each loop as a closed walk. |
| `loopOrientations` | array of string | `clockwise` or `counterclockwise`, one per loop. |
| `groups` | array of [`ComponentGroup`](#componentgroup) | Pipe expansions. |
| `nonFlowElements` | array of [`NonFlowElement`](#nonflowelement) | Instruments and controllers. |
| `circuitOf` | object of string | Owning circuit per component. |
| `distributionGroups` | array of [`DistributionGroup`](#distributiongroup) | Subcircuits sharing one parent, in declaration order. |
| `inferred` | array of string | Components the language added. |
| `branchShapes` | object of array of string | Kind sequence per attached circuit (`D-100`). |

### `Visualization`

The `show` directive resolved (`57`).

| Field | Type | Meaning |
|---|---|---|
| `active` | string | The property the colour scale follows. |
| `available` | array of string | The properties the switcher offers. |
| `scale` | [`Scale`](#scale) | The scale for `Active`. |

### `Binding`

An evaluated `let`.

| Field | Type | Meaning |
|---|---|---|
| `name` | string | The name. |
| `value` | number or `null` | The value in `unit`. |
| `unit` | string or `null` | The canonical unit, or `null` when dimensionless. |

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
| `iterations` | integer | Newton iterations on the last pass. |
| `residualNorm` | number | The scaled residual norm at the end. |
| `elapsedMs` | integer or `null` | Wall time, or `null` when the caller did not time it. |
| `sizingPasses` | integer | Outer-loop passes. |

### `VersionedId`

A named thing and its version.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The stable identifier. |
| `version` | string | The exact version string. |

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
| `tOut` | [`Quantity`](#quantity) or `null` | Temperature at the outlet port. Absent when not applicable. |
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

### `IndexedAnchor`

An anchor rule for an indexed port family.

| Field | Type | Meaning |
|---|---|---|
| `prefix` | string | The port name prefix, `in` or `out`. |
| `side` | string | The box side the family sits on. |
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
