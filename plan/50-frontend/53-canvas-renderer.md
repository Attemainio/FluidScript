---
id: 53-canvas-renderer
title: Canvas renderer
tier: 50-frontend
status: implemented
owns: [SVG canvas rendering, viewport, the prepared scene, label geometry, declarative-symbol interpretation, axes, what the renderer does with a solved layout]
depends_on: [25-layout-hints, 26-model-contract, 51-frontend-architecture]
traces_to: [R-22, R-23, R-27, R-34, R-37, R-41, R-42, R-44, R-45, R-46, R-47, R-48]
open_questions: 0
last_review_pass: 6
---

# Canvas renderer

## Purpose

The frontend half of the drawing: the canvas that shows the P&I diagram Core has laid out. The hard
problem -- a layout a designer recognises -- is Core's ([`28`](../20-core-domain/28-layout-solver.md));
this document is the part that turns a finished scene into pixels and keeps it readable at every
zoom and size.

## Responsibilities

**Owns.** SVG rendering, the viewport, interpretation of Core-owned symbol definitions, and the axes.
It does not define component shapes (`D-20`, `D-24`) and it does not compute a placement or a
route (`D-103`): the renderer maps world units to pixels, draws each symbol's strokes inside the
inner box it is given, draws each route's polyline, and puts each label where the scene says.

**Explicitly does not own.** Layout hints ([`25-layout-hints`](../20-core-domain/25-layout-hints.md)),
interaction and write-back ([`54-interaction-and-writeback`](54-interaction-and-writeback.md)), colours
and theming ([`55-design-system`](55-design-system.md)), and how a solved property becomes a colour
gradient or a legend ([`57-state-visualization`](57-state-visualization.md) — this document draws the
shapes, that one decides what colour they are filled with).

## As built

P5.6 (2026-09-18) is `frontend/src/features/canvas`: `prepareScene(model)` in `scene.ts`, a pure
function of the wire's `layout` and `symbols` returning the prepared scene below; `SceneView`, one
React SVG component that draws it in world units under the pane's root transform, and is also what
the golden test renders and what `59` will serialize; `viewport.ts`, the pan, zoom, fit and reset
arithmetic; `CanvasPane`, the pane with the axes, the grid and the level of detail. What it draws:
every placement with a box as its symbol's strokes under the Core instrument's transform (mirror,
then the clockwise quarter turn, the y flip once at the root), an inline placement (a zero box on
the wire, `D-105`) as nothing for a two-pipe node, a hollow dot for a boundary and a label for a
pipe; every route from the back, cut around its own hops, with `fillet` as a quadratic corner of
a quarter margin and, from `layout.flow`, one arrowhead per drawn run rather than per connection: the compiler splits a pipe the reader sees as one line into several connections through inferred inline elements (`D-105`, rule I7), and the arrow goes on the longest route of the chain between two drawn symbols (the user's correction to the first pictures, 2026-09-18); the label as the tag
or the id at `labelAt`; a badge for the worst diagnostic addressed to the component and a hollow
square for one carrying a sized or defaulted value; the `state` fill slot as a flat colour from
the placement's `scale` through `55`'s fluid ramp (`fluidFill`, a `color-mix` of the two
neighbouring stops), pulled forward from P5.10 so the first pictures read as a plant; an unsolved
model drawn whole in the error colour (`57`, the user's ask on the first day); the rest of `57`
stays there. One world unit is 60 px at 1× (`worldUnitPx`), the Core instrument's scale, so
the ladder's pictures and the canvas agree in size. Strokes keep their pixel width at every zoom
(`vector-effect`), text scales with the drawing.

**Not a Web Worker, and not until M4.** The threading section below was written when the layout
was the frontend's; since `D-103` the geometry is Core's and what the frontend computes per model
is a few hundred transform strings, which is not work worth a message boundary. `prepareScene` is
pure so it can move to a worker when frame deltas (`43`, M4) give one something to do, and the
budget criterion below stays unticked until it is measured in a browser, which P5.5's benchmark
environment could not launch (`U-4`).

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

## The layout is Core's

Placement and routing are computed in Core by the layout solver
([`28-layout-solver`](../20-core-domain/28-layout-solver.md), `D-103`, `D-106`, `D-107`): its
model, the standard every drawing is held to, and its rules are specified there and nowhere else.
This document specifies what the renderer does with a finished `Scene`: the viewport, the pixel
mapping, the symbols' strokes, labels, level of detail, threading. Where an older paragraph below
says "the renderer chooses", read "the solver chose" -- the code is Core's.

### Component spacing

Every symbol has a bounding box, and the solver keeps every two boxes at least the margin apart
(`28` A2). **Sparse by default**: the reference drawings this convention comes from set valves,
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

### Label geometry

Every symbol carries its tag as drawn text (`D-34`), and at 200 components the label layer holds more
boxes than the symbol layer does. Labels are laid out, spaced and asserted on exactly as symbols are;
a diagram whose symbols are disjoint and whose labels overlap each other is the ordinary way a
generated P&I drawing becomes unreadable.

**A label's box comes from a declared advance-width table, never from measuring the rendered text
(`D-73`).** [`55-design-system`](55-design-system.md) already forbids layout depending on a specific
font's metrics, and `measureText` would break that, break invariant 1's byte-determinism, and be
unavailable in the layout worker and in a headless test alike. The box is `advance x characters x size`
from the table for that type-scale entry; the resolved font is only required to *fit inside* it. A
wider fallback overflows its own reserved box and moves nothing else, which is the only degradation
available if placements are to be stable across machines.

Labels are placed on the side of their owner away from the run it sits on, and displaced along the run
when that would collide. A label that cannot be placed clear takes a leader line to its owner rather
than being dropped: a symbol whose tag is invisible is worse than a slightly busier diagram, because
the tag is what a reader matches against the equipment schedule.

### The prepared scene

The layout engine's output, and the artefact everything downstream consumes: the canvas draws it, the
exporter serializes it ([`59-static-export`](59-static-export.md)), and every layout predicate in
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md) asserts on it. **`D-71` makes it
the verification target instead of the SVG**, which means it is a specified structure rather than an
internal, and that it carries *resolved* geometry — a predicate that has to re-derive a bounding box
from a symbol id and a rotation would be a second implementation of the renderer's arithmetic, which is
the thing `D-71` exists to avoid.

```typescript
export interface PreparedScene {
  readonly sourceHash: string;
  readonly topologyHash: string;          // what layout.worker keys on (invariant 2)
  readonly bounds: Box;                   // world units, Y up
  readonly symbols: readonly PreparedSymbol[];
  readonly routes: readonly PreparedRoute[];
  readonly labels: readonly PreparedLabel[];
  readonly degraded: boolean;             // set only with FS5001
  readonly metrics: SceneMetrics;
}

export interface PreparedSymbol {
  readonly id: string;                    // stable id, never a tag (D-34)
  readonly kind: string;
  readonly definition: string;            // SymbolDefinition id (D-24)
  readonly origin: Point;
  readonly orientation: "north" | "east" | "south" | "west";
  readonly bounds: Box;                   // after orientation, world units
  readonly ports: readonly PreparedPort[];
  readonly junction: boolean;             // a flow group of three or more ports
  readonly inferred: boolean;
}

export interface PreparedPort {
  readonly name: string;
  readonly anchor: Point;                 // absolute, world units
  readonly side: "north" | "east" | "south" | "west";
}

export interface PreparedRoute {
  readonly id: string;
  readonly from: PortId;                  // component id plus port name
  readonly to: PortId;
  readonly points: readonly Point[];      // polyline; every segment axis-aligned
  readonly arrow: "forward" | "reverse" | "none";
}

export interface PreparedLabel {
  readonly ownerId: string;
  readonly text: string;
  readonly bounds: Box;                   // from the declared metric table (D-73)
  readonly leader?: readonly Point[];
}

export interface SceneMetrics {
  readonly symbolCrossings: number;       // route segments crossing a symbol box
  readonly routeCrossings: number;        // route/route crossings, drawn as hops
  readonly labelCollisions: number;
  readonly reflowIterations: number;      // against D-72's cap
  readonly routeLengthRatio: number;      // route length over Manhattan distance
  readonly areaUtilisation: number;       // symbol area over scene-bounds area
  readonly aspectRatio: number;
}
```

The state, style and provenance payload the scene also carries is
[`57-state-visualization`](57-state-visualization.md)'s and `59`'s, cited here rather than restated;
what this document specifies is the geometry, because that is what the layout engine decides.

**`metrics` is not a gate.** `labelCollisions`, `symbolCrossings` and `reflowIterations` have hard
limits stated in the invariants below; the rest are recorded per fixture and trended. A number that
moves is how a refactor that quietly degrades the diagram becomes visible, which no screenshot review
achieves in practice.

**Routes are port-to-port, and that is load-bearing.** `from` and `to` name a port, not a component,
because a route landing on `3WV.a` where the graph says `3WV.b` draws a bypass as a through-leg — a
diagram that satisfies every geometric predicate and depicts a plant nobody described.

**Heat moves left to right and every flow loop runs clockwise; fluid need not move right.** Both are
hard constraints of the solver (`28` B, H9 and H10, `D-108`); a return runs right to left along a
loop's bottom and its arrow must say so. Reversing a solved duty
during playback moves nothing, because the layout is fixed by the run snapshot's design point.

**Why not force-directed.** Force-directed layout is the default reach for a graph, and it produces a
diagram that is different on every run, drifts as values change, and never quite settles into the
rectangles a designer expects. It fails `R-21`'s implicit requirement that the diagram be *stable*
while typing more than it fails on aesthetics.

**Determinism is a hard requirement.** The same hints must produce the same placement, byte for byte.
Any iteration must be bounded and seeded. A layout that shifts by two pixels per keystroke is worse
than an ugly one.

### Routing and placement

Both are the solver's (`28`): a pipe is an orthogonal polyline from port to port, leaving each port
along its outward direction for a whole margin, with a hop where it passes behind another route
(`28` C16: the picture is drawn from the back -- signals, then return pipes, then supply pipes, each
route's `layer` on the wire -- and the route behind owns the crossing and is broken a quarter margin
either side of it, so the route in front runs through; a route's `hops` are its own breaks). The
`style` corner treatment (`fillet`) is applied when the polyline is drawn. Routes and placements are
recomputed only when the graph changes -- never when a value changes -- so a transient run animates
values over a fixed drawing ([`51-frontend-architecture`](51-frontend-architecture.md)). v1 has
computed, deterministic placement only; manual overrides are post-v1 (`D-29`).

## Declarative symbols

One `SymbolDefinition` per delivered component kind arrives in the model/metadata contract, drawn in a
normalised unit box and scaled at render time. The table is a release inventory, not a TypeScript shape
library: Core owns each primitive, port anchor, and label anchor (`D-20`, `D-24`). M3 requires only
kinds delivered through M2b; M4 adds its two rows before M4 exits.

| Kind | First required | Symbol |
|---|---|---|
| `node` | M3 | A junction (three or more connections) is a small filled circle, 0.2 across, with one pipe per side; **a node with two connections draws nothing** (`D-105`). Every node, a two-port or boundary node included, is nonetheless *laid out* with a box and an outer boundary (`28` A6, `D-108`); drawing nothing is the renderer's choice, not the layout's |
| `pipe` | M3 | The connection line itself: an inline element with no box, its label beside the line; a discretized pipe shows tick marks per internal node |
| `heat_exchanger` | M3 | The standard crossed-rectangle exchanger glyph; an arrow indicates heat in or out |
| `valve` | M3 | Two opposed triangles (bowtie), with a fill proportion showing position |
| `three_way_valve` | M3 | Bowtie with a third stub; the switched ports are **labelled** `a` and `b` and the inlet triangles filled, because the glyph alone no longer says which is which (`D-112`: the layout may draw either switched port on the straight run) |
| `pump` | M3 | Circle with an internal triangle pointing in the flow direction |
| `tank` | M4 | `D-32` vessel divided into `layers` bands; materialized inlet/outlet anchors sit at their normalized elevations, and layer fills use their own temperatures |
| `controller` | M4 | Dashed circle with the loop tag, connected to its actuator by a dashed line, and to its measurement point by a second, lighter one. Both ends come from the `control` binding (`D-40`) via `hints.nonFlowElements`; the renderer infers neither from the graph, where a controller has no ports |
| `t_sensor`, `p_sensor`, `flow_sensor` | M3 | ISA-5.1's instrument bubble -- a circle with the tag letters (`TE`, `PE`, `FE`) as its label -- at its `attachedTo` node via `hints.nonFlowElements`. `D-61` added the kinds after `D-23` had deferred them; this row replaced a sentence that still said there was no sensor symbol (2026-09-15) |

**Shipped in P5.1c (2026-09-15):** every row above has its strokes in Core's `SymbolCatalog`, on
the wire as `symbols[].primitives`, and the table in
[`docs/functions/model-contract.md`](../../docs/functions/model-contract.md) is generated from it.
A primitive carries `fill: "state"` where the colour scale paints and `fill: "stroke"` for a solid
mark, and the controller's bubble is `dashed`; the position bar, layer bands, heat arrow, badges and
the sized-versus-stated marker below are the renderer's, drawn from the state and not from the
symbol. The standards' figures were not reproduced -- the glyphs are this project's reading of the
conventions, and the user has said the symbols can be adjusted later; because layout works on the
box and anchors alone, adjusting them moves nothing.

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

The layout's own invariants -- clearance, no pipe through a symbol, routes port to port, corners on
bare pipe, determinism, edit stability -- are [`28`](../20-core-domain/28-layout-solver.md)'s
standard and are asserted in Core. The renderer's are:

1. The same scene renders to a byte-identical SVG; nothing rendered depends on the machine.
1b. Changing `spacing` changes the scene's geometry and changes no other Core output (`D-37`).
1c. No element is keyed, selected, committed, or exported by an equipment tag (`D-34`).
2. The renderer computes no placement and no route: every coordinate it draws is in the scene.
3. Layout is requested only on a topology change, never on a value change; a transient run animates
   values over a fixed drawing.
3a. No label box overlaps another label box, any symbol but its own owner, or any route but its own
   leader, at supported scale. Label boxes come from the declared metric table (`D-73`).
4. Rendering a model with `solved: false` succeeds, showing topology with no state.
5. The prepared scene contains every symbol, route, label, state, and provenance input required by
   [`59-static-export`](59-static-export.md), with no second drawing implementation.
6. No rendering code performs a unit conversion beyond display formatting.
7. Every route's arrow direction agrees with the sign of the solved flow on that branch
   (`layout.flow`). A reviewer looking at a screenshot does not independently know the answer, so
   this is only ever caught here.
8. The prepared scene is the verification target (`D-71`): it is produced headlessly, carries
   resolved geometry, and is byte-identical for a given scene and spacing.

## Error cases

| Situation | Behaviour |
|---|---|
| A supported post-collapse model would overlap | Deterministically increase stage/row spacing and reflow until clear; failure is a renderer invariant breach, not a degraded success |
| A placement would land a component on a corner | Lengthen the affected run and reflow deterministically until every component sits on a straight section; exhausting that is `FS5002`, a renderer invariant breach (`D-44`) |
| An unsupported/degraded model still overlaps after collapse/reflow | Place deterministically with overlap, log `FS5001` (warning), mark the scene degraded, and never present it as satisfying the supported layout gate |
| A route cannot avoid crossing a symbol | Route through it, drawn beneath — a visible imperfection beats a missing connection — and count it in `metrics.symbolCrossings`. Non-zero on a sample or reference circuit fails invariant 3b |
| Reflow reaches `D-72`'s iteration cap | `FS5002`, a renderer invariant breach. Live since `C-101` (2026-09-18) for a different breach: Core audits every layout it hands out (`28` A10) and raises one `FS5002` per hard finding, with the rule and the two parts in the message; the client shows it as any other warning. The cap is a backstop for a wrong monotonicity argument; a fixture consuming more than half of it is a finding about the repair |
| A label cannot be placed clear of everything | Place it at the least-collided position with a leader line to its owner, and count it in `metrics.labelCollisions`. Non-zero at supported scale fails invariant 3a |
| The resolved font is wider than the declared metric table | The label overflows its own reserved box and nothing moves (`D-73`). Placements are stable across machines; a font-dependent reflow is not |
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

The layout's worked examples are the ladder's steps ([`29`](../20-core-domain/29-layout-ladder.md)),
each with its script, its text and its picture. What the renderer adds to a scene is shown by
the `Scene` → SVG instrument in Core.Tests (`SceneSvg`): the pixel mapping (60 px per world unit
there, the design system's scale here), the y flip, each symbol's strokes inside its inner box, and
the labels.

## Acceptance criteria

- [x] The prepared scene passes the shared renderer/export golden test in [`59-static-export`](59-static-export.md). (P5.6: one SVG per Api sample under `canvas/goldens`, rendered by the same component the pane uses; `59` reads the same markup.)
- [x] An unknown component kind renders a labelled rectangle rather than breaking the canvas. (P5.6)
- [ ] A 200-component model meets `07-quality-attributes`' frame and UI-thread budgets while panning. (Unmeasured: no browser launches in the build environment, `U-4`, and no 200-component golden exists on the frontend side. What Node measures, P5.11's `baseline.test.tsx`: 4.8 ms to prepare and render the 24-placement header to static markup, which extrapolates to ~40 ms at 200 -- over the 8 ms commit budget before the DOM is touched, so the budget is not met by extrapolation and a browser measurement is the next step, `U-10`.)
- [x] Inferred components are visually distinguishable from declared ones without hovering. (P5.6: `--canvas-symbol-inferred` and an italic label.)
- [x] Symbols carry their tag as a label, and a test asserts no DOM key, selection key, or export id
      contains a tag. (P5.6)
- [x] The header's two pumps render with distinct DOM keys from their identifiers (`PU_AHU`,
      `PU_RAD`) and distinct drawn labels from their tags (`101PU01`, `102PU01`). (P5.6)
- [ ] Setting `spacing` to twice the default widens every gap and leaves everything Core computes
      but the layout byte-identical.
- [ ] A modulating valve shows a 0–1 indicator whose accessible name states the numeric value. (P5.10, with the state readouts.)
- [ ] Layout/routing/render preparation is verified to run in the Web Worker; the UI thread performs
      only the bounded SVG commit. (Deferred to M4, see *As built*.)
- [x] Keyboard and screen-reader users can reach the same component state and diagnostic information. (P5.6: every symbol is focusable in layout order with a title. P5.11: `SceneTable`, the diagram as two tables built from the same cards as hover; focus shows the card; arrows pan, `+`/`-` zoom, Enter selects, `Ctrl+E` exports; axe passes over the mounted app under jsdom. A screen reader's reading and 200 % zoom are `U-10`.)
- [ ] Groups over 10 members and scenes over 500 elements apply the specified initial-collapse rule
      and still meet `07`'s budgets. (No collapse exists on either side yet; `U-7`.)
- [ ] A tank renders exactly its resolved layer count and only its materialized port anchors, placing
      30% on layer 2 and 90% on layer 5 for the five-layer reference. (The anchors are the layout's and drawn; the layer bands are P5.10's.)
- [x] A solved/transient flow reversal changes connection arrows and nothing else on the drawing. (P5.6: asserted on the markup with the arrows stripped.)
- [x] Every placement and every route the renderer draws comes from the scene; a test asserts the
      frontend computes no coordinate of its own. (P5.6: every `translate` in the markup is a placement centre or label point; every route piece is the wire's polyline.)

Everything about *where* things are -- corners, rails, bands, orientation, clearance -- is asserted
in Core against [`28`](../20-core-domain/28-layout-solver.md)'s standard, not here.

## Open questions

None open here. A secondary ordering key for header members the user did not order (design
temperature, hot at the top) was deferred by `D-100` and belongs to the layout ladder now.
