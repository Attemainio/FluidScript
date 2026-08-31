---
id: 26-model-contract
title: Model contract
tier: 20-core-domain
status: draft
owns: [the serialized model shape, model contract versioning, JSON field conventions, what every consumer receives]
depends_on: [22-component-model, 23-topology-and-graph, 24-auto-sizing, 25-layout-hints]
traces_to: [R-18, R-20, R-23, R-31, R-37, R-39, R-41, R-44, R-45, R-46, R-47]
open_questions: 0
last_review_pass: 0
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

  "circuits": [                        // D-33; always at least one, in declaration order
    {
      "name": "coolingLoop",
      "number": 100,
      "numberIsExplicit": false,       // false when the binder resolved it — the printer needs this
      "substance": "water",
      "mode": "steady",                // "steady" | "transient"
      "role": null,                    // resolved circuit role, or null for Neutral (D-35)
      "parentCircuit": null,           // D-33; set on a subcircuit
      "supplyAnchorId": null,          // the parent component this circuit takes flow from
      "returnAnchorId": null,
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
        "flow":   { "value": 0.2394, "unit": "kg/s" },
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
      "state": { "flow": { "value": 0.2394, "unit": "kg/s" } } }
  ],

  "layout": {
    "order": ["N1", "N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV", "3WV__P1", "P1", "N3"],
    "rank":  { "N1": 1, "3WV__P1": 1, "P1": 2, "N3": 3 },
    "thermalStages": [
      { "rank": 0, "role": "neutral", "components": ["N1", "N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV", "3WV__P1", "P1", "N3"] }
    ],
    "portSides": { "HE1.in": "west", "HE1.out": "east",
                   "3WV.a": "west", "3WV.b": "north", "3WV.c": "south" },
    "loops": [["N2", "PU1", "PU1__HE1", "HE1", "HE1__3WV", "3WV"]],
    "loopOrientations": ["clockwise"],
    "groups": [],
    "inferred": ["N2", "PU1__HE1", "HE1__3WV", "3WV__P1"]
  },

  "style": { "stroke": "#2f6f9f", "width": 2, "corners": "fillet", "pattern": "dashed" },

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

**Serializing an unsolved circuit is a first-class case, not a degraded one.** The 300 ms debounce path
(`R-21`) frequently has a parseable script that does not yet solve — half-written, or under-specified.
The canvas must draw the topology anyway. A contract that requires a solution would make the editor
blank the diagram constantly.

## Invariants

1. Every numeric value is in the canonical script unit for its dimension, and its `unit` field says so.
2. `contractVersion` is present and is the version the producing Core actually implements.
3. Every `component.id` is unique **within its circuit** and matches an entry in `layout.order`. Two
   circuits may each hold a `PU1`; a consumer keying components by bare id across the whole model is
   wrong, and `component.circuit` is the qualifier (`D-33`).
4. Every `connections[].from`/`to` names an existing component and one of its ports.
5. `parameters[].source == "stated"` if and only if the user wrote it.
6. `basis` is present exactly when `source` is `sized` or `default`.
7. `state` is `null` on every component when `circuit.solved` is false, and non-null on every
   component when it is true — **unless `FS2502` is present**, in which case `statesOmitted` is `true`
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
payload arriving every 300 ms.

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

- [ ] The M2 demo circuit serializes to a payload matching the shape above, validated against a schema.
- [ ] Every dimensioned field carries a `unit` and its value is in the canonical unit.
- [ ] An unsolved circuit serializes with `solved: false` and null states, and the canvas renders it.
- [ ] A golden-file test pins the full payload for the demo circuit; a contract change fails it visibly.
- [ ] Deserialize/re-serialize is byte-identical.
- [ ] Changing a field's unit fails a test that asserts the version was bumped.
- [ ] A 100-node discretized pipe triggers `FS2502` rather than emitting the full payload.
- [ ] Every component symbol resolves, every port has an anchor, and the same definitions drive canvas
      and SVG export golden files.
- [ ] Duty, Rated, and Coupled exchanger fixtures serialize their canonical `mode`; frontend and agents
      do not derive mode from port count.
- [ ] Provenance contains source hash, language major, exact catalogue/property versions, and the
      pressure atmosphere used.
- [ ] The storage header serializes source/storage/consumer thermal stages in ranks 0/1/2, and its
      tank ports/layers match the shape above in compile-only and transient payloads.
- [ ] A `nodes=4` pipe serializes one group with its declared pipe id as `parentComponentId` and all
      nine lowered children in deterministic order; cooling-loop, substation, storage-header, and
      multi-conversion fixtures serialize Core's exact stage roles, ranks, and component order.
- [ ] `container v=300` round-trips as source text while the contract contains `kind: "tank"` and
      parameter `volume`; a bare/default tank volume is carried as dm³, never m³.

## Open questions

None. Realtime uses validated delta frames (`43`), and evaluated `bindings` ship in contract 1.0 so
agents and hover can explain derived values.
