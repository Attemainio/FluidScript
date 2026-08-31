---
id: 53-canvas-renderer
title: Canvas renderer
tier: 50-frontend
status: draft
owns: [SVG canvas rendering, viewport, layout engine and its two modes, routing, declarative-symbol interpretation, axes, component spacing]
depends_on: [25-layout-hints, 26-model-contract, 51-frontend-architecture]
traces_to: [R-22, R-23, R-27, R-34, R-37, R-41, R-42, R-44, R-45, R-46, R-47, R-48]
open_questions: 0
last_review_pass: 0
---

# Canvas renderer

## Purpose

`D-03`'s frontend half, and the brief's "most interesting part": turning a graph plus hints into a P&I
diagram that a designer recognises. The hard problem is layout — a diagram that a human would draw
differently from how a generic graph layout draws it, because hydronic diagrams have conventions
(flow left to right, loops as closed rectangles, valves on the branch they control) that no
off-the-shelf algorithm knows.

## Responsibilities

**Owns.** SVG rendering, the viewport, layout and routing algorithms, interpretation of Core-owned
symbol definitions, and the axes. It does not define component shapes (`D-20`, `D-24`).

**Explicitly does not own.** Layout hints ([`25-layout-hints`](../20-core-domain/25-layout-hints.md)),
interaction and write-back ([`54-interaction-and-writeback`](54-interaction-and-writeback.md)), colours
and theming ([`55-design-system`](55-design-system.md)), and how a solved property becomes a colour
gradient or a legend ([`57-state-visualization`](57-state-visualization.md) — this document draws the
shapes, that one decides what colour they are filled with).

## Rendering technology

**SVG, not Canvas 2D or WebGL.**

| | SVG | Canvas 2D | WebGL |
|---|---|---|---|
| Hit testing | Free — DOM events per element | Manual | Manual |
| Accessibility | Real DOM nodes | None | None |
| Export (`R-31`) | Serialize the DOM | Re-render | Re-render |
| Text | Native, themeable, selectable | Manual layout | Painful |
| 1000 elements | Fine | Fine | Fine |
| 50 000 elements | Slow | Fine | Fine |

v1 models are hundreds of elements, and three of SVG's advantages — free hit testing, free
export, native text — are exactly what this project needs. M3 export reuses this prepared scene
([`59-static-export`](59-static-export.md)) rather than defining another drawing path. Revisit only if
a model reaches thousands of elements; `D-30`'s initial-collapse thresholds are the measured v1 gate.

## Viewport

CAD-style, per `R-22`.

| Property | Behaviour |
|---|---|
| Pan | Drag with middle mouse or space+drag; two-finger scroll on trackpad |
| Zoom | Wheel, centred on the cursor — never on the viewport centre, which feels wrong |
| Zoom range | 0.1× to 10× |
| Fit | `F` fits the model with a 5 % margin |
| Reset | `Home` returns to 1× at the origin |
| Coordinate system | World units; **Y is up**, as in CAD, not down as in screen space |

**Y up costs one transform and buys correctness of intuition.** A designer reading coordinates expects
Y to increase upward; a diagram where a component "above" another has a smaller Y is quietly
disorienting. The flip lives in one root transform.

### Axes

Origin marked with a red X axis and a green Y axis (`R-22`) — the CAD convention, and the reason it is
in the requirements rather than the design system. Rendered as two short rays from the origin with tick
marks, fading below 0.5× zoom where they become noise.

A grid is drawn at a zoom-dependent spacing, subordinate to everything else — visible enough to give a
sense of scale, faint enough that it never competes with the diagram
([`55-design-system`](55-design-system.md) owns the values).

## Layout engine

The core of this document. Input: `LayoutHints` plus the graph. Output: a placement per component and a
route per connection.

### Approach: thermal bands, then one of two modes per group

```
1. Heat bands  Allocate monotonically increasing X bands from hints.thermalStages: sources,
               conversion/storage, then consumers. Parallel groups share a band and stack.
2. Mode        For each circuit group, choose a layout mode from structure alone:
               a circuit named in a hints.distributionGroups entry with 2+ members → HEADER;
               everything else → LOOP RECTANGLE. Core never names the mode (D-38).
3. Partition   Within each band/group, split into loop members (hints.loops) and tree parts.
4a. Rectangle  Lay each loop out as a rectangle in hints.loopOrientations, components distributed
               around its perimeter in traversal order, sized by member count. Shared-component
               loops sit side-by-side with that component between them.
4b. Header     Lay the parent circuit's supply chain along a top rail and its return chain along a
               bottom rail; stack the group's members between them in hints order; connect each
               member up to supply and down to return at its supplyAnchorId/returnAnchorId.
5. Tree parts  Layered locally by hints.rank: rank = column, siblings stack vertically.
6. Attach      Tree parts attach to their loop or rail at the shared node's position.
7. Compact     Remove empty local columns/rows without crossing a thermal-band boundary; centre.
8. Space       Enforce the minimum gap between adjacent bounding boxes (below).
9. Route       Orthogonal routing between port anchors, including right-to-left returns.
```

**Step 2 reads structure and produces a shape, and the split matters.** Core says *these circuits
share a supply/return pair*; the renderer decides that such a set is drawn as two rails with members
between them. Putting the mode name in `LayoutHints` would make Core hold an opinion about shapes,
which `D-03` forbids and which would also mean a renderer could not choose differently at a small
canvas size without Core knowing about canvas sizes.

**Why two modes instead of one general algorithm.** A loop distributes components around a perimeter;
a header stacks them between two rails. Parameterising a single algorithm to do both is the same trap
force-directed layout represents one level up — it would be tuned until it did one of them acceptably
and the other badly. `D-38` records the alternatives.

### Header layout

The mode that makes a plant look like a plant, and the reason `D-33` added a sixth reference circuit
to have something to test it against. Picture a heating circuit `100` with subcircuits `101` and
`102`:

```
     supply rail ──┬─────────────┬──────────────►
                   │             │
              ┌────┴────┐   ┌────┴────┐
              │   101   │   │   102   │      members stack, in hints order
              └────┬────┘   └────┬────┘
                   │             │
     return rail ◄─┴─────────────┴───────────
```

Rules:

- **The parent's supply chain is the top rail, its return chain the bottom.** Which is which comes
  from `hints.loopOrientations` for the parent, not from a guess: supply on top and return below is
  already `D-30`'s convention.
- **Members stack in `DistributionGroup.Members` order**, which Core fixes to declaration order. This
  is what stops the branches reordering between renders.
- **A branch leaves its rail vertically, then turns in the heat direction** — right for heating, left
  for cooling, per `D-31`. The vertical-then-horizontal shape is what makes the rails readable as
  rails rather than as two more branches.
- **A member is laid out internally by its own mode.** A subcircuit that is itself a loop draws as a
  rectangle inside its slot. Modes nest; they do not compete.

### Component spacing

Every symbol has a bounding box, and step 8 enforces a minimum gap between adjacent boxes along a rail
or a perimeter. **Sparse by default**: the reference drawings this convention comes from set valves,
sensors and fittings well apart, and a diagram whose symbols touch reads as a single smear at fit
zoom.

The gap comes from `spacing` in the style settings (`D-37`), defaulting to the design system's token
when the script says nothing. It is in world units and it is **not** a layout hint: Core carries the
number without interpreting it, and a test asserts the model contract is byte-identical across two
different spacing values. That test is the enforcement of `D-03` from this side — if spacing ever
changed a Core output, the boundary would have moved without anyone deciding to move it.

`D-31` makes step 1 normative. Cooling/source circuits occupy the left, conversion and storage the
middle, and radiator/AHU heating networks the right. Multiple sources and multiple consumers stack
within their shared band. **Heat moves left to right; fluid need not.** A return branch may route right
to left and its arrow must say so. Reversing a solved duty during playback never moves a band because
layout is fixed by the run snapshot's design point.

**Why not force-directed.** Force-directed layout is the default reach for a graph, and it produces a
diagram that is different on every run, drifts as values change, and never quite settles into the
rectangles a designer expects. It fails `R-21`'s implicit requirement that the diagram be *stable*
while typing more than it fails on aesthetics.

**Determinism is a hard requirement.** The same hints must produce the same placement, byte for byte.
Any iteration must be bounded and seeded. A layout that shifts by two pixels per keystroke is worse
than an ugly one.

### Routing

Orthogonal (Manhattan) segments, matching P&I convention:

1. Exit each port perpendicular to its side for a fixed stub length.
2. Route in axis-aligned segments, preferring at most two bends.
3. Avoid crossing symbols; crossing another route is acceptable and drawn with a hop.
4. Apply the `style` corner treatment — `fillet` rounds the corners
   ([`12-grammar`](../10-language/12-grammar.md)'s style directive).

**Route caching.** Routes recompute only when placements change — not when values change. A transient
run changes values 600 times and placements zero times
([`51-frontend-architecture`](51-frontend-architecture.md)).

### Placement policy

v1 uses computed, deterministic placement only. Stable ids preserve selection, DOM reconciliation,
worker commits, and export identity across recompiles; they do not key user-authored coordinates.
Manual position overrides and free-placement drawing remain post-v1 research (`D-29`).

## Declarative symbols

One `SymbolDefinition` per delivered component kind arrives in the model/metadata contract, drawn in a
normalised unit box and scaled at render time. The table is a release inventory, not a TypeScript shape
library: Core owns each primitive, port anchor, and label anchor (`D-20`, `D-24`). M3 requires only
kinds delivered through M2b; M4 adds its two rows before M4 exits.

| Kind | First required | Symbol |
|---|---|---|
| `node` | M3 | Small filled circle. **Inferred nodes render smaller and lighter** — `hints.inferred` |
| `pipe` | M3 | The connection line itself, thickened; a discretized pipe shows tick marks per internal node |
| `heat_exchanger` | M3 | The standard crossed-rectangle exchanger glyph; an arrow indicates heat in or out |
| `valve` | M3 | Two opposed triangles (bowtie), with a fill proportion showing position |
| `three_way_valve` | M3 | Bowtie with a third stub, the controlled port emphasised |
| `pump` | M3 | Circle with an internal triangle pointing in the flow direction |
| `tank` | M4 | `D-32` vessel divided into `layers` bands; materialized inlet/outlet anchors sit at their normalized elevations, and layer fills use their own temperatures |
| `controller` | M4 | Dashed circle with the loop tag, connected to its actuator by a dashed line, and to its measurement point by a second, lighter one. Both ends come from the `control` binding (`D-40`) via `hints.nonFlowElements`; the renderer infers neither from the graph, where a controller has no ports |

There is no v1 persistent sensor symbol: `D-23` defers the component, while pinned readouts and the
accessible state table provide its current UI use case.

Conventions applied to every symbol:

- **Fill comes from the active colour scale** ([`57-state-visualization`](57-state-visualization.md)),
  not from this document. Symbols and routes are drawn as shapes with a fill slot; what goes in it is
  the visualization layer's decision. A symbol whose fill is hard-coded here cannot participate in a
  gradient.
- **Flow arrows** on connections, from `hints.flow`. `None` draws no arrow — a dead leg is visibly dead.
- **Warning badges**: a small marker at the symbol's corner, coloured by severity, from top-level
  diagnostics grouped by `component` (`R-24`).
- **Sized versus stated**: a subtle marker distinguishing components carrying auto-sized values
  ([`26-model-contract`](../20-core-domain/26-model-contract.md)'s `source` field). This is `D-02`
  made visible, and it is the single most useful thing the canvas can tell a designer at a glance.
- **Labels**: the component's **tag** where it has one, falling back to its identifier where it does
  not (`D-34`); key values (duty, DN, Kv) at zoom above 1.5×, hidden below to avoid clutter. The tag
  is what a reader recognises from a drawing — `400PU01`, not `PU1` — and it is display only. Nothing
  in the renderer may key an element, a selection, a worker commit or an export identity by it; those
  all use `component.id`, which the tag is deliberately not
  ([`25-layout-hints`](../20-core-domain/25-layout-hints.md)'s stable-id section).
- **Position indicator**: a valve or pump whose position or relative speed is solved draws a small
  0–1 fill bar beneath its symbol. It is an indication, not a readout — the numeric value belongs in
  hover and in the accessible table, and the bar exists so a reader scanning a running diagram can see
  at a glance which valves are working and which are pinned open. Its accessible name states the value
  as text, so the information is not carried by length alone (`R-42`).

## Level of detail

| Zoom | Shows |
|---|---|
| < 0.5× | Symbols and routes only; no labels, no axes ticks, no grid |
| 0.5×–1.5× | Names |
| > 1.5× | Names, key values, port markers |
| > 3× | Everything, including internal pipe nodes and inferred node names |

Without level-of-detail, a 200-component circuit at fit zoom is unreadable text soup, and that is the
first impression a user forms.

## Invariants

1. The same graph and hints produce a byte-identical placement.
1a. Layout mode is a function of `hints.distributionGroups` alone. No mode is chosen from a component
   count, a canvas size, or anything Core did not state.
1b. Changing `spacing` changes placements and changes no Core output — asserted by comparing the model
   contract byte for byte across two spacing values (`D-37`).
1c. No element is keyed, selected, committed, or exported by an equipment tag (`D-34`).
2. Layout runs only on a topology change, never on a value change.
3. No symbol overlaps another for a model inside `07`'s supported scale after mandatory initial
   collapse has been applied.
4. Every connection route starts and ends at a port anchor.
5. Rendering a model with `solved: false` succeeds, showing topology with no state.
6. The prepared scene contains every symbol, route, label, state, and provenance input required by
   [`59-static-export`](59-static-export.md), with no second drawing implementation.
7. No layout or rendering code performs a unit conversion beyond display formatting.

## Error cases

| Situation | Behaviour |
|---|---|
| A supported post-collapse model would overlap | Deterministically increase stage/row spacing and reflow until clear; failure is a renderer invariant breach, not a degraded success |
| An unsupported/degraded model still overlaps after collapse/reflow | Place deterministically with overlap, log `FS5001` (warning), mark the scene degraded, and never present it as satisfying the supported layout gate |
| A route cannot avoid crossing a symbol | Route through it, drawn beneath — a visible imperfection beats a missing connection |
| Model exceeds 500 rendered elements | Collapse every collapsible group; groups over 10 members start collapsed even below the scene limit (`FS2402`) |
| A component kind has no symbol | Render a labelled rectangle; log a warning. A new component kind must never break the canvas |
| `layout` missing from the model | Fall back to a simple grid; the diagram is poor but present |

User-input and unsupported-scale rows degrade rather than blank the canvas. A supported-scale overlap
is different: it violates the layout contract and is surfaced as an internal invariant failure while
the last good render remains visible.

During a transient, however, a symbol/port mismatch against the immutable run snapshot is a contract
breach: stop playback and retain the last verified frame (`D-22`). Static draft rendering may use the
labelled fallback because it cannot corrupt a running result.

## Threading and accessibility

Layout, routing, frame-delta application, colour-scale calculation, and SVG attribute preparation run
in a Web Worker. The worker returns a compact commit list keyed by stable element id. The UI thread
performs only one coalesced `requestAnimationFrame` DOM commit; it may skip obsolete display commits
when frames arrive faster than paint, but never reorders simulation state (`R-41`).

The SVG has a keyboard-navigable component order matching `layout.order` for hydraulic elements and
inserting `layout.nonFlowElements` at each record's `navigationOrder`. Controllers are placed beside
their `placementAnchorId` (the actuator component) and route their observer line to
`measurementTargetId`; the frontend does not infer either relationship from the graph. Every element
has visible focus, a title and description, and non-colour warning/state cues. A synchronized structured table exposes
component, connection, state, unit, source/basis, and diagnostic data to assistive technology. Pan,
zoom, fit, select, hover-equivalent details, and export all have keyboard controls; reduced-motion
removes animated transitions (`R-42`).

## Worked example

The **cooling loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)), from
[`25-layout-hints`](../20-core-domain/25-layout-hints.md)'s payload.

**Step 1 — partition.** `hints.loops` gives one loop:
`[N2, PU1, PU1__HE1, HE1, HE1__3WV, 3WV]`. Tree parts: the primary supply `N1 → N2`, and the primary
return `3WV → 3WV__P1 → P1 → N3`.

**Step 2 — loop.** Six members on a rectangle, 320 × 160 world units, distributed clockwise from the
loop's entry node:

| Component | Position | Port sides |
|---|---|---|
| `N2` | (0, 0) | — (a node has no sides) |
| `PU1` | (160, 0) | in West, out East |
| `PU1__HE1` | (320, 0) | — |
| `HE1` | (320, −160) | in North, out South |
| `HE1__3WV` | (160, −160) | — |
| `3WV` | (0, −160) | a East, b North, c South |

`3WV.b North` sends the recirculation branch straight up the rectangle's left edge back to `N2`, and
`3WV.c South` puts the primary return below it — the two outlets separate without the renderer having
to work out which is which.

**Step 3–4 — tree parts.** `N1` attaches left of `N2` at (−160, 0). The return chain hangs below
`3WV`: `3WV__P1` at (0, −280), `P1` at (0, −380), `N3` at (0, −480).

**Step 7 — routes.** Nine connections, all single-segment or one-bend, because the loop is a rectangle
with components on and between its corners and both tree parts leave it perpendicular.

**Rendered:**

```
   N1 ──────► N2 ────► PU1 ────► PU1__HE1
               ▲                     │
               │                     ▼
              3WV ◄── HE1__3WV ◄─── HE1
               │
               ▼
            3WV__P1 ──► P1 ──► N3
```

Every connection carries an arrow: this circuit has no dead legs. `N2`, `PU1__HE1`, `HE1__3WV` and
`3WV__P1` render as small light circles because they are inferred; `HE1`, `3WV`, `PU1`, `P1` and the
two boundary nodes `N1` and `N3` render at full weight because the user wrote them. Someone reading
this diagram sees immediately which six things they declared and which four the language added — and the recirculation
leg returning to `N2` is visible as a loop rather than inferable from a connection list.

### Storage-header layout

The **storage header** reference is the multi-source/multi-consumer acceptance case. Its thermal bands
are fixed at X = −160, 0, and +160 world units:

```
S1  ───────► in1 ┌────────┐ out1 ───────► RAD_NETWORK
S2  ───────► in2 │   T1   │ out2 ───────► AHU_NETWORK
                  │ 5 layers│
                  └────────┘
 source band        storage        consumer band
```

`S1`/`S2` and the two network boundaries stack in source order. The tank draws five horizontal layer
bands bottom-to-top and locates the 30% anchors on layer 2 and 90% anchors on layer 5. The diagram does
not move as those layer colours change. In a full closed heating network, its return route travels
back toward the tank and may point left; that is correct fluid flow inside a left-to-right heat chain.

## Acceptance criteria

- [ ] The worked example produces the placement above, deterministically across 100 runs.
- [ ] The recirculation branch `3WV.b → N2` renders as a closed loop edge, not as a stub.
- [ ] The cooling loop follows Core's `Clockwise` orientation; two loops sharing an exchanger render
      side-by-side with the exchanger between them.
- [ ] No symbols overlap on any sample or supported 200-component post-collapse fixture; the layout
      reflows deterministically until clear.
- [ ] An explicitly over-limit fixture may render overlap only with `FS5001` and `degraded: true`; the
      same condition in a supported fixture fails the invariant test.
- [ ] Layout is not recomputed during a transient run — counting spy.
- [ ] Zoom centres on the cursor; Y increases upward.
- [ ] The red X and green Y axes are visible at the origin above 0.5× zoom.
- [ ] An unsolved model renders topology with no state values.
- [ ] The prepared scene passes the shared renderer/export golden test in [`59-static-export`](59-static-export.md).
- [ ] An unknown component kind renders a labelled rectangle rather than breaking the canvas.
- [ ] A 200-component model meets `07-quality-attributes`' frame and UI-thread budgets while panning.
- [ ] Inferred components are visually distinguishable from declared ones without hovering.
- [ ] The distribution-header reference renders as two rails with both subcircuits stacked between
      them, each branch leaving its rail vertically before turning; a circuit with no subcircuits
      still renders as a loop rectangle.
- [ ] Reordering the two subcircuit blocks in the source reorders the stack and changes nothing else.
- [ ] Symbols carry their tag as a label, and a test asserts no DOM key, selection key, or export id
      contains a tag.
- [ ] Two components named `PU1` in different circuits render as two symbols with distinct tags and
      distinct DOM keys.
- [ ] Adjacent components on a rail never touch at the default spacing; setting `spacing` to twice the
      default widens every gap and leaves the model contract byte-identical.
- [ ] A modulating valve shows a 0–1 indicator whose accessible name states the numeric value.
- [ ] Layout/routing/render preparation is verified to run in the Web Worker; the UI thread performs
      only the bounded SVG commit.
- [ ] Keyboard and screen-reader users can reach the same component state and diagnostic information.
- [ ] Groups over 10 members and scenes over 500 elements apply the specified initial-collapse rule
      and still meet `07`'s budgets.
- [ ] The storage header renders source/storage/consumer bands at monotonically increasing X, with
      both source and both consumer branches parallel and non-overlapping.
- [ ] Cooling-loop, substation, and storage-header fixtures consume Core's exact thermal-stage ranks:
      Neutral remains one band, Conversion stays between source and consumer, and equal-rank parallel
      groups stack without a renderer-side reclassification.
- [ ] A tank renders exactly its resolved layer count and only its materialized port anchors, placing
      30% on layer 2 and 90% on layer 5 for the five-layer reference.
- [ ] A solved/transient flow reversal changes connection arrows but not thermal-band placements; a
      right-to-left return is never relabelled or drawn as rightward merely to satisfy the heat order.

## Open questions

None. Core supplies loop orientation; shared loops render side-by-side around their shared component.
Groups over 10 members and all collapsible groups in a scene over 500 elements start collapsed. The M3
large-model benchmark validates those fixed v1 thresholds against `07`; changing them requires recorded
evidence rather than an implementation guess (`D-30`).
