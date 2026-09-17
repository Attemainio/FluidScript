---
id: 26-model-contract
title: Model contract
tier: 20-core-domain
status: reviewed
owns: [the serialized model shape, model contract versioning, JSON field conventions, what every consumer receives]
depends_on: [22-component-model, 23-topology-and-graph, 24-auto-sizing, 25-layout-hints]
traces_to: [R-18, R-20, R-23, R-31, R-37, R-39, R-41, R-44, R-45, R-46, R-47]
open_questions: 0
last_review_pass: 6
---

# Model contract

## Purpose

One serialized shape, produced by Core, consumed by the REST API, the canvas, the hover readout, the
console log, and eventually the exporters. Defining it once is what stops the canvas and the exporters
diverging into two half-compatible views of the same model — the standard outcome when each consumer
gets its own endpoint.

## Responsibilities

**Owns.** The serialized model's shape, its versioning rule, JSON conventions, and the mapping from
Core types to wire types.

**Explicitly does not own.** Transport ([`42-rest-contract`](../40-api/42-rest-contract.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md)), what the canvas draws
([`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md)), diagnostics' own shape
([`16-diagnostics`](../10-language/16-diagnostics.md)), the meaning of the `visualization` block
([`57-state-visualization`](../50-frontend/57-state-visualization.md) — this document carries it, that
one defines it).

**The domain in `visualization.scale` is computed by Core**, because only Core holds every element's
value; the colour ramp is the frontend's. That split is `D-03` applied to one more field.

## Conventions

| Rule | Reason |
|---|---|
| `camelCase` field names | JavaScript consumer; matches `02-glossary`'s casing table |
| **Values are numbers in the canonical script unit, not SI (`D-14`)** | The consumer displays them and the user thinks in kW and °C. Converting once, in Core, beats every consumer converting — and beats every consumer *forgetting* to. |
| Every dimensioned field has a sibling `*Unit` field, or the unit is in the shape's schema | A number with no unit on a wire is a bug waiting for a second consumer |
| `null` means "not computed"; absent means "not applicable" | A pump has no `kv`; an unsolved circuit has a `null` flow. Different things. |
| No Core type is serialized directly | A rename in Core must not silently reshape the API (`architecture.md`) |
| Enums serialize as their script keyword | `"three_way_valve"`, not `3` — so the wire is readable and matches `/docs` |

**The canonical-unit rule is the one worth defending.** SI on the wire is the conventional choice and
is wrong here: three consumers would each convert, the exporters would need the same table, and a
tooltip showing 30 000 W for a heat exchanger the user wrote as `power=30` is a bad tooltip. Core owns
the canonical-unit table already ([`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md));
converting there costs one pass and removes a whole class of consumer bug.

## The shape

```jsonc
{
  "contractVersion": "2.0",            // D-33 made `circuit` → `circuits` a breaking change
  "provenance": {
    "sourceHash": "sha256:…", "languageMajor": 1,
    "catalog": { "id": "steel-en10255", "version": "2026.1" },
    "propertyBackend": { "id": "sharp-prop", "version": "…" },
    "atmosphereKPaAbsolute": 101.325       // gauge/absolute boundary fixed by D-26
  },
  "project": {                         // D-37; absent when the script has no `project` line
    "name": "plant_01",
    "defaultMode": "dynamic"           // "steady" | "transient" | null
  },

  "style": {                           // D-104: Core resolves, the renderer draws
    "tokens": ["blue", "2px", "fillet", "--"],
    "spacing": 20,                     // world units, or null — D-37; the layout margin since D-103
    "default": { "stroke": "#0000ff", "strokeWidth": 2, "pattern": "dashed", "fill": null, "corner": "fillet" },
    "named": { "trace": { "stroke": "#ff0000", "strokeWidth": null, "pattern": "solid", "fill": null, "corner": null } }
  },

  "circuits": [                        // D-33; always at least one, in declaration order
    {
      "name": "coolingLoop",
      "number": 100,
      "numberIsExplicit": false,       // false when the binder resolved it — the printer needs this
      "substance": "water",
      "mode": "steady",                // "steady" | "transient"
      "role": null,                    // resolved circuit role, or null for Neutral (D-35)
      "parentCircuit": null,           // D-33; set on a subcircuit
      "inletAnchorId": null,          // the parent component this circuit takes flow from
      "outletAnchorId": null,
      "solved": true,
      "statesOmitted": false           // true only alongside FS2502
    }
  ],

  "pressureDatums": ["N1"],            // one per hydraulic connected component, NOT per circuit

  "components": [
    {
      "id": "HE1",                     // stable id (25-layout-hints)
      "kind": "heat_exchanger",        // script keyword
      "mode": "duty",                  // component-specific canonical mode; null/absent otherwise
      "symbolId": "heat_exchanger.standard",
      "origin": "declared",            // "declared" | "inferred:I1" | "inferred:I2" | "inferred:I3"
      "sourceSpan": { "start": 142, "length": 39 },   // null for inferred
      "circuit": "coolingLoop",        // D-33; owning circuit under D-36 for a two-sided component
      "tag": "100HE01",                // D-34; display metadata, null when the kind has no tag code

      "parameters": {
        "power": { "value": 30,   "unit": "kW", "source": "stated" },
        "in":    { "value": 20,   "unit": "C",  "source": "stated" },
        "out":   { "value": 50,   "unit": "C",  "source": "stated" },
        "dp":    { "value": 20,   "unit": "kPa","source": "default",
                   "basis": "20 kPa at 0.239 l/s — default" }
      },

      "state": {                        // solved values; null when unsolved
        "flow":   { "value": 0.2392, "unit": "kg/s" },
        "tIn":    { "value": 20.0,   "unit": "C" },
        "tOut":   { "value": 50.0,   "unit": "C" },
        "dp":     { "value": 20.0,   "unit": "kPa" },
        "power":  { "value": 30.0,   "unit": "kW" }
      },

      "ports": [
        { "name": "in",  "role": "inlet",  "connectedTo": "PU1__HE1" },
        { "name": "out", "role": "outlet", "connectedTo": "HE1__3WV" }
      ]
    }
  ],

  "symbols": [                            // Core-owned by D-20; delivered with M3 by D-24
    { "id": "heat_exchanger.standard", "viewBox": [-0.5, -0.5, 1, 1],
      "primitives": [
        { "kind": "rect", "x": -0.45, "y": -0.35, "width": 0.9, "height": 0.7 },
        { "kind": "line", "from": [-0.35, -0.25], "to": [0.35, 0.25] },
        { "kind": "line", "from": [-0.35, 0.25], "to": [0.35, -0.25] }
      ],
      "portAnchors": { "in": [-0.5, 0], "out": [0.5, 0],
                         "in2": [0, -0.5], "out2": [0, 0.5] },
      "labelAnchor": [0, -0.65] }
  ],

  "connections": [
    { "id": "c0", "from": { "component": "PU1__HE1", "port": null },
                  "to":   { "component": "HE1", "port": "in" },
      "flow": "forward",
      "state": { "flow": { "value": 0.2392, "unit": "kg/s" } } }
  ],

  "layout": {
    "order": ["N1", "N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV", "3WV__P1", "P1", "N3"],
    "thermalStages": [
      { "rank": 0, "role": "neutral", "components": ["N1", "N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV", "3WV__P1", "P1", "N3"] }
    ],
    "flow": { "c0": "forward", "c1": "forward" },   // per connection, as the binder wrote it or as the solved loop turned it
    "groups": [],
    "nonFlowElements": [],
    "circuitOf": { "N1": "main", "PU1": "main" },
    "distributionGroups": [],
    "inferred": ["N2", "PU1__HE1", "HE1__3WV", "3WV__P1"],

    "margin": 0.5,                     // D-103: the outer box is the inner box grown by this
    "extent": [0, 0, 7.5, 4.0],        // [x, y, w, h] of the union of every outer box, world units
    "placements": [                    // one per component, instruments and controllers included
      { "componentId": "HE1", "symbolId": "heat_exchanger",
        "inner": [3.0, 1.5, 0.5, 1.0], "outer": [2.5, 1.0, 1.5, 2.0],
        "rotation": 0, "mirrored": false, "arrangement": "u",
        "anchors": { "in": { "at": [3.0, 1.7], "direction": [-1, 0] }, "out": { "at": [3.0, 2.3], "direction": [-1, 0] },
                     "in2": { "at": [3.5, 2.3], "direction": [1, 0] }, "out2": { "at": [3.5, 1.7], "direction": [1, 0] } },
        "labelAt": [3.25, 2.65], "source": "computed",
        "style": { "stroke": "#ff0000", "strokeWidth": null, "pattern": "solid", "fill": null, "corner": null },
        "scale": 0.62 }                // position on the active `show` scale, 0..1; absent when unsolved
    ],
    "routes": [                        // one per connection: the stubs of margin/2 and the orthogonal join
      { "id": "c0", "kind": "pipe", "points": [2.25, 1.7, 3.0, 1.7], "hops": [],
        "scaleFrom": 0.62, "scaleTo": 0.62 }
    ]
  },

  "visualization": {                     // the `show` directive's resolution — owned by 57
    "active": "temperature",
    "available": ["temperature", "pressure", "flow"],
    "scale": { "property": "temperature", "displayName": "Temperature", "unit": "C",
               "kind": "sequential",
               "domain": { "min": 5.0, "max": 50.0, "nice": true },
               "degenerate": false }
  },

  "bindings": [                          // evaluated `let` values, contract 1.0
    { "name": "dT", "value": 30, "unit": "K" }
  ],

  "diagnostics": [
    { "code": "FS1510", "severity": "info",
      "message": "Added node 'HE1__3WV' (I2).",
      "span": null, "component": "HE1__3WV", "suggestion": null, "related": [] }
  ],

  "solve": {
    "converged": true,
    "iterations": 4,
    "residualNorm": 3.2e-9,
    "elapsedMs": 41,
    "sizingPasses": 2
  }
}
```

`layout.groups` serializes [`25-layout-hints`](25-layout-hints.md)'s contract directly as
`{ "parentComponentId": string, "children": string[] }`. For `PB pipe ... nodes=4`, one entry uses
`PB` as the parent and contains all four thermal-node ids followed by all five hydraulic sub-pipe ids;
consumers neither infer membership nor discard either child kind. `thermalStages` likewise serializes
Core's completed deterministic ranking. Wire consumers may place those stages, but must not derive a
different thermal order.

For a tank, `parameters` uses canonical names even when the source wrote `container v=...`, dynamic
ports list only the materialized indices, and transient state exposes every layer bottom to top:

```jsonc
{
  "id": "T1", "kind": "tank", "symbolId": "tank.stratified",
  "parameters": {
    "volume": { "value": 300, "unit": "dm3", "source": "stated" },
    "layers": { "value": 5, "unit": null, "source": "stated" }
  },
  "state": {
    "storedEnergy": { "value": 51200000, "unit": "J" },
    "layers": [
      { "index": 1, "elevation": 0.10, "t": { "value": 25, "unit": "C" }, "mass": { "value": 59.8, "unit": "kg" } }
      // indexes 2…5 follow; every frame keeps the same count/order
    ]
  },
  "ports": [
    { "name": "in1", "role": "bidirectional", "elevation": 0.90, "layer": 5, "connectedTo": "S1" },
    { "name": "out1", "role": "bidirectional", "elevation": 0.90, "layer": 5, "connectedTo": "RAD_NETWORK" }
  ]
}
```

Its matching entry in `symbols` carries the generic anchor rule:

```jsonc
{
  "id": "tank.stratified", "viewBox": [-0.5, -0.8, 1, 1.6],
  "indexedPortAnchors": [
    { "prefix": "in", "side": "west", "verticalCoordinate": "port.elevation", "minIndex": 1, "maxIndex": 16 },
    { "prefix": "out", "side": "east", "verticalCoordinate": "port.elevation", "minIndex": 1, "maxIndex": 16 }
  ]
}
```

An unsolved tank still carries resolved defaults, materialized ports, elevations, and layer count;
only `state` is null. The symbol definition provides an indexed-anchor rule for `in{n}` on the west
wall and `out{n}` on the east wall at their normalized elevation, rather than enumerating 32 anchors
in every payload (`D-32`).

### What P5.1b shipped against this shape (2026-09-15)

The shape above is the contract; these are the places the implementation had to be more precise than
it, each recorded here rather than left for a reader of the golden files to discover:

- **The records are Core's, the serializer is the Api's** (`D-46`, `D-47`). `FluidScript.Core.Model`
  holds the hand-written wire records, `ModelContractBuilder` (the projection) and `SymbolCatalog`,
  and names no serialization type; `FluidScript.Api/Contracts/ModelContractJson` is System.Text.Json
  with one options object -- camelCase, declaration order, nulls written, non-ASCII unescaped -- and
  a contract modifier that turns Core's `[AbsentWhenNull]` into the serializer's ignore condition.
  The architecture tests caught the first draft with the serializer in Core; the split is what `03`
  always drew. `D-46`'s emitted schema and the generated TypeScript mirror are not built: P5.2's,
  with the endpoints that carry the payload. The golden files and the round trip are Api tests.

- **The unit strings are the language's canonical spellings**: `°C` not `C`, `dK` for a temperature
  difference, `kPa` gauge. A dimension with no canonical spelling -- head, Kv -- goes out in its SI
  unit (`m`, `m3/h`), which is what a bare number meant for it. A dimensionless value has `unit: null`.
  Confirmed 2026-09-15: the wire carries `°C`, `kPa`, `kW` and `kg/s` **always**, and the frontend
  shows the wire's unit as it is -- it formats the number and never converts it. SI stays inside
  Core; the wire is the one place the language's units reappear, and there is one unit boundary, not
  two.
- **Six significant digits, and a clean zero.** A Newton solution's trailing digits are noise below
  `36`'s tolerances and made two runs of one script differ in the golden file; the wire carries six
  digits, rounded through the shortest decimal so the writer prints exactly those. A magnitude under a
  nano-unit -- the gauge pressure at the datum, solved as −1.7 × 10⁻²¹ kPa -- is written as `0`.
  `solve.residualNorm` carries three. Project reasoning: six is beyond any instrument on the plant.
- **A promoted parameter is `sized`.** A pump head or valve Kv the solver was asked to find is not in
  the sizing overlay with a rule's basis; it is a solved unknown. On the wire it is `sized` with the
  basis *"found by the solver, to hold the stated constraints"* unless the outer loop wrote one, and it
  outranks the overlay's entry for the same name because that entry is the seed. The same number
  appears under `state.solved`, which is the operating-point half of the design/operating pair.
- **`circuits[].role` is the resolved canonical name, `null` only when the name matched nothing.**
  A registered Neutral role (`heating`) is its name; `25`'s precision on what Neutral means is why.
- **A subcircuit written as connections gets `parentCircuit` and its anchors** from `25`'s node-contact
  rule, not only from `inlet`/`outlet` lines.
- **`components[]` also lists instruments and controllers**, with no ports and no state, so a canvas
  can key everything it draws by one id. Invariant 3 is read as: a component with ports appears in
  `layout.order`, one without appears in `layout.nonFlowElements`. Pipe-expansion children carry
  `origin: "expanded"`; they are neither declared nor inferred by I1–I3.
- **A node's symbol has one anchor, `"*"`, at its centre**, because its ports are not named in
  advance; instruments and controllers likewise. Invariant 9's "every port resolves to an anchor" is
  satisfied by a named anchor, the wildcard, or an indexed rule.
- **A tank in a steady solve carries `state.t` and `state.p`**, not `layers`: the steady tank has one
  mixed enthalpy unknown, and writing it into five layers would draw a stratification the solver did
  not compute. `layers` is the transient's, and arrives with it.
- **The size cap is measured, not estimated**, and therefore lives in the Api: `ModelContractJson.Build`
  serializes compact, and if the bytes exceed `MaxPayloadBytes` (1 MiB, twice `07`'s budget for the
  reference model) asks `ModelContractBuilder` for the payload again with states omitted and `FS2502`
  in the diagnostics. `solved` stays `true`; `statesOmitted` is what changed. Compile-only payloads
  carry no states and are never capped. One 100-node pipe is 210 components and well under; the cap
  is for the several-thousand-component case, which the test reaches by lowering it.
- **An anchor is a point and a direction, and a symbol may offer alternatives** (`D-102`, P5.1c):
  `portAnchors` is `{ port: { at, direction } }` and `alternatives` names other complete arrangements
  of the same ports. The example above predates this and shows the `[x, y]` form.
- **The layout hints are the nine fields the example shows and nothing more** (`D-107`, 2026-09-16):
  `order`, `thermalStages`, `flow`, `groups`, `nonFlowElements`, `circuitOf`, `circuits`,
  `distributionGroups`, `inferred`. The `rank`, `portSides`, `loops`, `loopOrientations` and
  `branchShapes` fields P5.1a added were derived for a solver that no longer exists and left the
  wire with it; [`25`](25-layout-hints.md) names each field's reader.
- **`show` is read off the syntax**, not the model, because the binder does not bind it (`L-50`).
- The duplicate `style` object the shape carried -- one of tokens, one of resolved stroke and
  pattern -- was a drafting slip; the tokens form is what Core carried and did not interpret
  (`D-37`). Superseded by `D-104` below: Core now resolves, and the object carries both.
- `solve.elapsedMs` is `null` unless the caller timed the run; Core does not.

### What P5.1d-1 shipped against this shape (2026-09-15)

`D-103` moved the layout into Core and `D-104` the style resolution with it; `layout` and `style`
grew, and nothing else moved.

- **`layout.margin`, `layout.extent`, `layout.placements[]`, `layout.routes[]`** are the solved
  scene. A placement is one component -- instruments and controllers included, keyed by the same id
  as `components[]` -- with its `inner` box (the symbol's bounds after the transform) and its
  `outer` box (the inner grown by `margin` on every side), the transform that produced them
  (`rotation` in quarter turns clockwise, `mirrored`, `arrangement` naming the `D-102` alternative
  in use or `null` for the default), every anchor in world coordinates with its outward direction,
  `labelAt`, and `source: "computed"`; `"pinned"` is reserved for the write-back loop and never
  emitted yet. Boxes are `[x, y, w, h]` with `y` growing **upward** and `y` the bottom edge
  (`D-106`, [`28`](28-layout-solver.md) A1); the renderer flips once where it maps units to pixels.
  The invariant is `D-103`'s as `28` H1–H2 state it: no inner box intersects another placement's
  inner box or comes closer than the clearance, asserted through `SceneAudit`; outer boxes may
  overlap (soft).
- **A route is a flat polyline** `[x0, y0, x1, y1, …]` from one anchor point to the other; its
  first and last segments are the stubs, a whole `margin` long along the anchor directions, so the
  polyline runs straight from the inner boundary to the outer one and turns only from there
  (`28` A3, A7, H5); the segments between are axis-aligned and the polyline is normalised (no
  duplicate, collinear or backtracking points). `hops` holds the points where this route crosses
  an earlier one, in `[x, y, …]` pairs, for the renderer's crossing mark. `kind` is `pipe` or
  `signal` (dashed, instrument to its anchor). `scaleFrom` / `scaleTo` are the two ends' positions
  on the active `show` scale. An anchor's `direction` is the outward normal; the flow direction is
  not on the wire, because `connections[].flow` already says which way each connection runs and the
  renderer's arrow follows the route.
- **`style.default` and `style.named` are resolved** (`ResolvedStyleWire`): `stroke` and `fill`
  as `#rrggbb` (named CSS colours resolved by Core, `NamedColours`), `strokeWidth` in px, `pattern`,
  `corner`; a null field means the theme's default, and a placement or route carries `style` only
  when the script said something about it, so an unstyled model is byte-identical to before on
  every component. `show` overrides a placement's fill only; the stroke it stated stays (`D-104`).
- **`scale` is a position, not a colour.** `57` owns the ramp; the wire carries where on it the
  component's active property sits, 0..1, six digits. The frontend maps that to a colour and does
  not recompute the domain.
- The example above shows the `u` arrangement on the exchanger because that is what the cooling
  loop's ring chooses; the through-pass default is drawn in a header branch.
- **An inline element's placement is a point** (`D-105`, 2026-09-16): a pipe, a pipe-expansion
  child, or a node with two connections has `inner` and `outer` of zero size at its point on the
  run, both port anchors at that point facing along the run, and no clearance; a renderer draws
  nothing for such a node and only the label for such a pipe, because the routes through it are the
  pipe. Zero size *is* the signal; there is no separate flag on the wire. A junction -- three or
  more connections -- keeps a 0.2 × 0.2 box and one route per side.
- **The three-way valve's anchors** are `a` north, `ab` south, `b` west (`D-105`).

### `parameters[].source` is the field that carries `D-02`

`"stated"` · `"sized"` · `"default"`. The canvas renders the three differently (`R-23`): a stated value
is the user's, a sized value is a decision with a basis, a default is a placeholder. Collapsing them
into one "value" loses exactly the information a designer needs, and it is one string field.

`basis` is present for `sized` and `default`, absent for `stated`.

### `state` versus `parameters`

`parameters` is the design specification; `state` is the solved operating point. They overlap by name
— a heat exchanger has both a `dp` parameter and a `dp` state — and that is intentional: the parameter
is what it was sized for, the state is what it is doing. A hover panel showing `dp: 20 kPa (design) /
19.8 kPa (now)` is possible only because both are present.

## Versioning

`contractVersion` is `major.minor`.

- **Minor** — additive only: a new optional field, a new enum member in a field whose consumers already
  handle unknown values. Consumers ignore what they do not know.
- **Major** — anything a consumer could misread: a removed field, a changed unit, a changed meaning.

The frontend checks the major version on connect and refuses to render on a mismatch rather than
drawing a diagram from fields it is misinterpreting. A wrong number rendered confidently is worse than
no diagram.

**The unit of a field is part of the contract.** Changing `dp` from kPa to bar is a major version bump
even though the JSON shape is identical — this is the change that would otherwise ship silently and
produce a diagram that is wrong by a factor of 100.

### `1.0` → `2.0`: `circuit` becomes `circuits`

`D-33` replaces the single `circuit` object with a `circuits` array, and that is **a major bump by
this document's own rule**: a consumer reading `model.circuit.name` against the new shape gets
`undefined`, not an error. It cannot be done as an additive minor.

The rejected softer options are worth recording, because both look cheaper and are worse:

- *Keep `circuit` as the first circuit and add `circuits` alongside.* Additive, so a minor bump, and
  every existing consumer keeps working — on a lie. A two-circuit model would report one circuit to
  anything that had not been updated, and the diagram would silently lose half the plant. A field that
  is correct only for single-circuit models is a trap with a timer on it.
- *Keep `circuit` for single-circuit models and emit `circuits` only when there are several.* No
  duplication. Cost: the shape now depends on the data, so every consumer needs both code paths and
  the single-circuit path is the one that gets tested.

A major bump is honest and the frontend already refuses to render on a major mismatch, which is
exactly the behaviour wanted here.

### `pressureDatum` moved out of the circuit, and that is a correction

It was `circuit.pressureDatum`, which quietly asserted one datum per circuit. That was never true —
[`23-topology-and-graph`](23-topology-and-graph.md) puts one datum per **hydraulic connected
component**, and a rated exchanger already produces two of those inside one circuit. Under `D-33` the
mismatch becomes visible in both directions: a subcircuit attached to its parent shares the parent's
datum, so two circuits have one between them, while the substation's single circuit has two.
`pressureDatums` is therefore top-level and plural, which is what the graph has always meant.

## Contracts

```csharp
/// <summary>Serializes a solved (or unsolved) circuit into the shared model contract.</summary>
public interface IModelSerializer
{
    /// <summary>Projects a circuit into the wire model.</summary>
    /// <param name="graph">The circuit, solved or not.</param>
    /// <param name="solution">The solve result, or <see langword="null"/> for a compile-only request.</param>
    /// <param name="diagnostics">Every diagnostic produced by the whole pipeline.</param>
    /// <returns>
    /// The model with values in canonical script units. Never throws: an unsolved circuit
    /// serializes with null states, which is what the debounce path needs while the user types.
    /// </returns>
    ModelContract Serialize(CircuitGraph graph, ISolution? solution,
                            ImmutableArray<Diagnostic> diagnostics);
}
```

**Serializing an unsolved circuit is a first-class case, not a degraded one.** The debounce path
(`R-21`) frequently has a parseable script that does not yet solve — half-written, or under-specified.
The canvas must draw the topology anyway. A contract that requires a solution would make the editor
blank the diagram constantly.

## Invariants

1. Every numeric value is in the canonical script unit for its dimension, and its `unit` field says so.
2. `contractVersion` is present and is the version the producing Core actually implements.
3. Every `component.id` is unique across the whole model and matches an entry in `layout.order`
   (`D-41`). A consumer may key components by bare id; `component.circuit` records membership, not
   identity.
4. Every `connections[].from`/`to` names an existing component and one of its ports.
5. `parameters[].source == "stated"` if and only if the user wrote it.
6. `basis` is present exactly when `source` is `sized` or `default`.
7. `state` is `null` on every component of a circuit whose `solved` is false, and non-null on every
   component of one whose `solved` is true. **Solve state is per circuit, not per model** (`D-33`):
   a model may hold a solved circuit and an unsolved one, and a consumer that reads a single
   whole-model flag would blank the states of a circuit that has them. Where a whole-model answer is
   wanted — enabling an export, say — it is the conjunction over `circuits[]`, computed by the
   consumer rather than carried as a second field that could disagree — **unless `FS2502` is present**, in which case `statesOmitted` is `true`
   and the consumer fetches states on demand. The two must never disagree: a solved model with null
   states and no `FS2502` is a bug.
8a. `component.tag` is display metadata and is never used as a key, a reference, or a lookup by any
   consumer. It is null exactly when the component's kind has no tag code or the component is
   inferred (`D-34`).
8b. Every `component.circuit` names an entry in `circuits`, and every `circuits[].parentCircuit`, when
   non-null, names a different entry.
8. `diagnostics` at the top level is the only diagnostic collection. Consumers group it by its
   `component` field; components do not duplicate diagnostic codes.
9. Every `component.symbolId` resolves to exactly one entry in `symbols`, and every port resolves to
   an anchor in that definition. Symbol primitives contain no executable code or renderer-specific API.
10. The model round-trips: deserializing and re-serializing is byte-identical after canonical
    formatting.
11. A heat exchanger's `mode` is exactly `duty`, `rated`, or `coupled` and agrees with `D-19`'s
    secondary-property/connection evidence; consumers never infer it again.
12. `layout.thermalStages` partitions every component exactly once and is immutable for the lifetime
    of a transient run. Connection flow direction remains a separate field (`D-31`).
13. A tank serializes only materialized ports; its layers are bottom-to-top, contiguous 1…N, and the
    count and port-to-layer map do not change between frames of one snapshot.

## Error cases

The contract carries diagnostics rather than producing them. Two serialization-level failures exist:

| Code | Trigger | Severity |
|---|---|---|
| `FS2501` | A value could not be converted to its canonical unit (non-finite) | Error — the field serializes as `null` and the diagnostic explains |
| `FS2502` | The model exceeds the size cap | Warning — `layout` is emitted, per-component `state` is omitted, `circuit.statesOmitted` is set to `true`, and the consumer fetches states on demand |

`FS2502` matters for a pipe discretized into 100 nodes across a 50-component circuit: several thousand
components, each with a state block. A size cap with a documented degradation is better than a 20 MB
payload arriving once per debounce interval. `07` budgets that payload at 512 KiB uncompressed for
the 200-component reference model, with serialization and client parse inside the end-to-end gate
(`D-48`).

**Measured 2026-09-15 (P5.1c)** on `ReferenceModels.DistributionHeader(18)` -- `01`'s header with
eighteen pumped consumers, exactly 200 components as the contract counts them, 19 circuits: the
compile response is **194 388 bytes (189.8 KiB)**, the solved response **243 319 bytes (237.6 KiB)**
with every state present; serialization is 1.1--1.3 ms warm and about 65 ms for the first call in a
process, which is `System.Text.Json` building its type metadata once. Both are well inside the 512 KiB
budget and the 1 MiB cap, so `FS2502` is for the discretized-pipe case above, not for a large plant.
The solve itself takes 1.1--1.5 s on the reference environment, which is the number the editor
debounce (`D-48`) will have to live with, not the payload's.

**Re-measured 2026-09-16 (P5.1d-1)** with the solved layout on the wire (`D-103`, `D-104`,
`D-105`): the compile response is **278.5 KiB**, the solved response **325.7 KiB**; serialization
~2 ms warm. The layout adds 89 KiB -- 200 placements with their anchors and 250 routes -- and the
budget still holds with 187 KiB to spare. `ModelContractBuilder.Build` including the layout solve is
34--46 ms warm, of which the solver is 24 ms (`07`'s layout row).

## Worked example

What each consumer takes from one payload:

| Consumer | Reads | Ignores |
|---|---|---|
| **Canvas** (`53`) | `layout`, `symbols`, `components[].symbolId/origin/id`, `connections`, `style` | `parameters[].basis`, `solve` |
| **Hover** (`54`) | `components[].state`, `parameters` with `source` and `basis` | `layout`, `connections` |
| **Editor squiggles** (`52`) | `diagnostics[].span/severity/message/suggestion` | everything else |
| **Console log** (`56`) | `diagnostics` where severity ≠ info, `solve.converged` | `layout`, `state` |
| **SVG export** (`59`) | prepared scene from `layout`, `symbols`, `components`, `connections`, `style` | internal Core objects |
| **An LLM agent** (`R-29`) | all of it — `parameters[].source` and `diagnostics[].code` above all | — |

The agent row is the strongest argument for `source` and `basis`: an agent asked to improve a design
needs to know which numbers are the user's constraints and which are the tool's guesses. Without those
fields it must treat all values as equally authoritative, and it will "helpfully" override the user's
stated head.

## Acceptance criteria

- [x] The M2 demo circuit serializes to a payload matching the shape above, validated against a schema.
      (P5.1b: the golden files are the schema; the wire records are what a JSON Schema would be
      generated from, and none is generated yet.)
- [x] Every dimensioned field carries a `unit` and its value is in the canonical unit.
- [x] An unsolved circuit serializes with `solved: false` and null states, and the canvas renders it.
      (The canvas half is P5.3's.)
- [x] A golden-file test pins the full payload for the demo circuit; a contract change fails it visibly.
- [x] Deserialize/re-serialize is byte-identical.
- [x] Changing a field's unit fails a test that asserts the version was bumped.
- [x] A 100-node discretized pipe triggers `FS2502` rather than emitting the full payload. (Read as
      the size cap, measured: one such pipe is 210 components and fits; the test that reaches
      `FS2502` lowers the cap, and a scene of fifty such pipes is what the real one is for.)
- [ ] Every component symbol resolves, every port has an anchor, and the same definitions drive canvas
      and SVG export golden files. (P5.1b: resolves and anchored, on every sample; the strokes and the
      export are P5.1c's and P5.3's.)
- [x] Duty, Rated, and Coupled exchanger fixtures serialize their canonical `mode`; frontend and agents
      do not derive mode from port count.
- [x] Provenance contains source hash, language major, exact catalogue/property versions, and the
      pressure atmosphere used.
- [x] The storage header serializes source/storage/consumer thermal stages in ranks 0/1/2, and its
      tank ports/layers match the shape above in compile-only and transient payloads.
- [x] A `nodes=4` pipe serializes one group with its declared pipe id as `parentComponentId` and all
      nine lowered children in deterministic order; cooling-loop, substation, storage-header, and
      multi-conversion fixtures serialize Core's exact stage roles, ranks, and component order.
- [x] `container v=300` round-trips as source text while the contract contains `kind: "tank"` and
      parameter `volume`; a bare/default tank volume is carried as dm³, never m³.

## Open questions

Realtime uses validated delta frames (`43`), and evaluated `bindings` ship in contract 1.0 so
agents and hover can explain derived values. One from P5.1b: `parameters[].unit` is `null` for a
value whose registry dimension is dimensionless, and that includes `u` (`W/(m²·K)`), because the
registry declares it so. The wire is honest about what the registry says; the registry is what
should say more (`C-78`'s neighbour).
