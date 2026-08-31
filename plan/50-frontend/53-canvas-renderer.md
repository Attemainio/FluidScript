---
id: 53-canvas-renderer
title: Canvas renderer
tier: 50-frontend
status: reviewed
owns: [SVG canvas rendering, viewport, layout engine and its two modes, routing, declarative-symbol interpretation, axes, component spacing]
depends_on: [25-layout-hints, 26-model-contract, 51-frontend-architecture]
traces_to: [R-22, R-23, R-27, R-34, R-37, R-41, R-42, R-44, R-45, R-46, R-47, R-48]
open_questions: 0
last_review_pass: 6
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
               a circuit named in a hints.distributionGroups entry → HEADER;
               everything else → LOOP RECTANGLE. Core never names the mode (D-38).
3. Partition   Within each band/group, split into loop members (hints.loops) and tree parts.
4a. Rectangle  Lay each loop out as a rectangle in hints.loopOrientations, components distributed
               around its perimeter in traversal order, sized by member count. Shared-component
               loops sit side-by-side with that component between them.
4b. Header     Lay the parent's supply chain along a top rail and its return chain along a bottom
               rail. Place the group's members side by side in hints order, each drawn as a U —
               supply run rightward, consumer on the descending side, return run leftward — tee'd
               off the rails at its supplyAnchorId/returnAnchorId.
5. Tree parts  Layered locally by hints.rank: rank = column, siblings stack vertically.
6. Attach      Tree parts attach to their loop or rail at the shared node's position.
7. Compact     Remove empty local columns/rows without crossing a thermal-band boundary; centre.
8. Space       Enforce the minimum gap between adjacent bounding boxes (below).
9. Route       Orthogonal routing between port anchors, including right-to-left returns.
```

The membership test needs no member count: Core emits no group below two members
([`25`](../20-core-domain/25-layout-hints.md)'s invariant 11), so a parent with one subcircuit simply
has no group and takes the rectangle. Repeating the threshold here would put one rule in two places.

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

The mode that makes a plant look like a plant, and the reason `D-33` added the **distribution header**
([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) as a sixth reference circuit to test
it against.

**A branch is a U, not a box.** The shape below is the target, and it is what a designer draws: the
branch leaves the supply rail, runs left to right through its flow components, turns **down** the far
side through its consumer, and returns **right to left** along the bottom into the return rail.

```
SUPPLY ──────▶[3-way valve]──────▶[pump]──[P]──[T]─┐
                    ▲                              │
                    │                           [valve]
                    │                              │
             [check valve]                 [heat exchanger]
                    │                              │
                    │                           [valve]
                    │                              │
RETURN ◀────────────┴──────────────[P]──[T]────────┘
```

Four rules produce it:

1. **The supply run is the top edge and the return run is the bottom**, flow left to right on top and
   right to left underneath. Which chain is which comes from `hints.loopOrientations`, not a guess:
   supply above return is already `D-30`'s convention.
2. **The consumer turns the corner.** The component carrying the branch's duty — the heat exchanger,
   the load — is placed on the *descending* side with its isolating valves above and below it, because
   that is where a reader looks for it and because it keeps the two horizontal runs free for
   instrumentation.
3. **The bypass leg closes the U on the near side.** A three-way valve's recirculation port routes
   straight down to the return run, and whatever sits on that leg — a check valve here — sits on the
   vertical. This is the same `PortSides` fact the cooling loop already relies on (`3WV.b North`,
   `3WV.c South`); the header only chooses which vertical it descends.
4. **Inline components stay inline.** Sensors, isolating valves and fittings are placed along whichever
   run they belong to, evenly spaced by their bounding boxes, never bunched at a corner.

**Parallel subcircuits repeat the U side by side, off shared rails:**

```
SUPPLY ──┬──────────────────────────────────────┬───────────────────────────────▶
         │                                      │
         └──▶[3WV]──▶[pump]──[P]──[T]─┐         └──▶[3WV]──▶[pump]──[P]──[T]─┐
              ▲                       │              ▲                       │
              │                    [valve]           │                    [valve]
              │                       │              │                       │
       [check valve]          [heat exchanger]  [check valve]        [heat exchanger]
              │                       │              │                       │
              │                    [valve]           │                    [valve]
              │                       │              │                       │
         ┌────┴───────[P]──[T]────────┘         ┌────┴───────[P]──[T]────────┘
         │                                      │
RETURN ◀─┴──────────────────────────────────────┴─────────────────────────────────
```

- **Members are laid out side by side, in `DistributionGroup.Members` order** — Core fixes that to
  declaration order, which is what stops the branches swapping places between renders.
- **Each member is the single-circuit shape above, unchanged.** The header adds the two rails and the
  tee where each branch leaves and rejoins them; it does not change how a branch is drawn. Modes nest.
- **The rails extend past the last branch**, so a plant that continues beyond the drawn extent reads as
  continuing rather than as terminating at its last consumer.
- **A branch leaves the rail vertically before turning**, which is what keeps the rails legible as
  rails rather than as two more branches.

Two notes on what these shapes need that v1 does not yet have:

**The `[P]` and `[T]` positions are sensor positions, and `D-23` defers persistent sensors.** In v1
those slots are simply unoccupied — the runs are drawn, the spacing is the same, and pinned readouts
and the accessible state table carry the values instead
([`57-state-visualization`](57-state-visualization.md)). The layout is specified with them because the
positions are where a reader expects to find them, and a layout that has to be rearranged when sensors
land is a layout that was specified around a temporary gap.

**The check valve on the bypass leg is a `valve`, not a distinct kind.** `R-09` ships one valve family;
a non-return valve is that component with its own symbol variant, not a seventh family. If a future
kind is added for it, the layout above is unchanged — the leg is chosen by port side, not by kind.

### Symbol orientation and the corner rule

Core supplies each symbol in a normalised unit box with named port anchors (`D-20`, `D-24`). Choosing
how that box is **oriented and where it sits on a run** is the renderer's, and it is what separates a
diagram a designer recognises from one that is merely correct.

| Kind | Default orientation | Why |
|---|---|---|
| `heat_exchanger` | **Vertical** — flow enters one end and leaves the other along a vertical run | This is how exchangers are drawn on nearly every P&I diagram: the two sides read as two stacked passes, and a rated exchanger's second side attaches horizontally without crossing anything |
| `tank` | **Vertical**, always | Its layers are stratified by elevation (`D-32`); a horizontal tank would make layer 1 mean nothing |
| `pump` | **Horizontal**, on a horizontal run, triangle pointing along flow | A pump is read by its flow direction, which is only legible on a horizontal run |
| `valve`, `three_way_valve` | Along the run it sits on; the third stub perpendicular | The controlled leg has to be visually distinct from the through leg |
| `pipe`, `node` | None — a node is a point, a pipe is the run itself | |
| `controller` | Beside its actuator, offset perpendicular to that component's run | It is not in the flow path (`D-40`) and must not read as though it were |

**The corner rule: an oriented component never sits at a *bend*.** A bend is where exactly two runs
meet and the path simply turns. A component whose preferred orientation is vertical is placed on a
vertical run, one whose orientation is horizontal on a horizontal run, and the layout allocates run
length for them before it places bends.

**A junction is not a bend, and the distinction is the whole rule.** Where three or more runs meet, a
junction *element* — a three-way valve, a tee — belongs exactly there, because it **is** the junction.
Placing it beside the meeting point would mean drawing a bare tee and then a valve next to it, which
is both longer and wrong. The cooling loop's `3WV` sits at such a T and is correctly placed;
`23-topology-and-graph`'s flow-group test already names precisely these components, so the renderer
does not have to guess which they are.

So: **bends carry no oriented component; junctions carry their junction element; runs carry
everything else.** Nodes are exempt everywhere, having no direction to get wrong.

**This is not cosmetic.** A symbol at a corner has one port on each of two perpendicular runs, so its
own geometry has to absorb a 90° turn: the exchanger's two passes stop being parallel, the pump's
triangle points diagonally, and a reader can no longer tell at a glance which way anything flows. It is
also the failure that makes a generated diagram look generated.

**Consequence for loops.** A four-component loop does not become a square with one component per
corner. The rectangle is sized so each run is long enough to carry its components clear of the bends —
which is why `hints.loops` gives *traversal order* rather than positions, and why the perimeter is
sized by member count rather than fixed.

### Component spacing

Every symbol has a bounding box, and step 8 enforces a minimum gap between adjacent boxes along a rail
or a perimeter. **Sparse by default**: the reference drawings this convention comes from set valves,
sensors and fittings well apart, and a diagram whose symbols touch reads as a single smear at fit
zoom.

The gap comes from `spacing` in the serialized style payload (`D-37`), defaulting to the design
system's token when the script says nothing. It is in world units and it is **not** a layout hint.

**The isolation test has to be stated precisely, because the obvious phrasing is impossible.** Spacing
must reach the renderer, so it is serialized, so the model contract is *not* byte-identical across two
spacing values — `style.spacing` differs, and must. What is identical is everything Core computes:
solved state, every parameter and its `source`/`basis`, the graph, and the whole of `layout`. The test
asserts that, comparing the two contracts with `style` excluded, and separately asserts that the two
placements differ. A test written as "the whole contract is byte-identical" either fails immediately or
passes only because spacing never reached the frontend at all.

That pair is the enforcement of `D-03` from this side: spacing crosses Core as opaque presentation and
influences nothing Core decides.

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
4a. No oriented component is placed at a two-run bend. A junction element (`hints` flow groups of
   three or more ports) is placed *at* its junction; every other oriented component is placed on a run
   matching its default orientation unless a stated hint overrides it. Nodes are exempt.
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

**Step 2 — loop.** Six members on a rectangle **480 × 240** world units, distributed clockwise from the
loop's entry node. The rectangle is wider than the member count alone would need, because the corner
rule reserves the four bends for routing:

| Component | Position | On | Port sides |
|---|---|---|---|
| `N2` | (0, 0) | top run, above `3WV` | — (a node has no sides) |
| `PU1` | (240, 0) | top run | in West, out East — **horizontal** |
| `PU1__HE1` | (400, 0) | top run | — |
| `HE1` | (480, −120) | **right run** | in North, out South — **vertical** |
| `HE1__3WV` | (400, −240) | bottom run | — |
| `3WV` | (0, −240) | bottom run, at the T | a East, b North, c West |

**`HE1` sits at the midpoint of the right run, not on a corner.** An exchanger is drawn vertically
(above), and the two right-hand bends at (480, 0) and (480, −240) carry the turns. Placing it on the
corner — as an earlier version of this example did — would force its two passes through a 90° turn and
make the diagram unreadable at exactly the component a designer looks at first.

**`3WV` sits at a T, and that is placement rather than a corner-rule breach.** Three runs meet at
(0, −240): the return arriving from the east, the recirculation leaving north up the left vertical to
`N2`, and the primary return continuing west to `N3`. A three-way valve *is* the junction — putting it
anywhere else would mean drawing a bare tee and then a valve beside it. `3WV.b North` and `3WV.c West`
separate the two outlets without the renderer having to work out which is which.

**The primary return continues along the bottom run, it does not hang below.** `3WV__P1`, `P1` and
`N3` extend westward from the valve on the same horizontal as the rest of the return. An earlier
version of this example dropped them downward as a stub, which put a third vertical on the diagram and
destroyed the supply-above-return reading the shape exists to give.

**Step 3–4 — tree parts.** `N1` attaches west of `N2` at (−160, 0), on the supply run. The primary
return continues west from `3WV` on the return run: `3WV__P1` at (−160, −240), `P1` at (−320, −240),
`N3` at (−480, −240). Supply enters top-left and return leaves bottom-left, which is the reading a
designer expects.

**Step 7 — routes.** Nine connections, all single-segment or one-bend, because the loop is a rectangle
with components on and between its corners and both tree parts leave it perpendicular.

**Rendered:**

```
   N1 ──────▶ N2 ────────▶[PU1]────────▶ PU1__HE1 ─────┐
               ▲                                       │
               │                                    [HE1]     consumer: vertical,
               │                                       │      mid-run, never on a bend
               │                                       ▼
   N3 ◀── P1 ◀── 3WV__P1 ◀──[3WV]◀───────── HE1__3WV ──┘
                              ▲
                       (b: north, up to N2)
```

Supply along the top running right, consumer descending the right, return along the bottom running
left, and the recirculation closing the loop on the left vertical — the same shape as the header
branch above, with the three-way valve on the return end rather than the supply end because that is
where the cooling loop puts it.

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
- [ ] `HE1` renders vertically at the midpoint of the loop's right run; no oriented component in any
      sample or reference circuit lands on a two-run bend.
- [ ] `3WV` renders at the T where the return, the recirculation and the primary outlet meet, not
      beside it.
- [ ] The cooling loop's primary return runs west along the return line to `N3`; no sample places a
      third vertical by dropping a return chain below the loop.
- [ ] A tank renders vertically in every circuit that contains one.
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
- [ ] The distribution-header reference renders as two rails with both subcircuits side by side, each
      drawn as a U: supply run left to right on top, consumer on the descending side between its two
      isolating valves, return run right to left below. A circuit with no subcircuits renders the same
      U without rails.
- [ ] A three-way valve's recirculation port descends the near side and rejoins the branch's own
      return run, not the header's.
- [ ] Both branches' rails extend past the last consumer rather than terminating at it.
- [ ] Reordering the two subcircuit blocks in the source reorders the stack and changes nothing else.
- [ ] Symbols carry their tag as a label, and a test asserts no DOM key, selection key, or export id
      contains a tag.
- [ ] The header's two pumps render with distinct DOM keys from their identifiers (`PU_AHU`,
      `PU_RAD`) and distinct drawn labels from their tags (`101PU01`, `102PU01`).
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
