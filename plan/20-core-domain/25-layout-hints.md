---
id: 25-layout-hints
title: Layout hints
tier: 20-core-domain
status: draft
owns: [the classification the layout engine starts from (order, circuits and their attachment, distribution groups, non-flow elements, inferred components), the flow direction per connection for the arrows, thermal-stage classification and FS2403, stable component ids, ordering determinism]
depends_on: [23-topology-and-graph]
traces_to: [R-22, R-27, R-44, R-45, R-46, R-47, R-48]
open_questions: 0
last_review_pass: 6
---

# Layout hints

## Purpose

`LayoutHints` is what Core knows about the graph's *structure* before any geometry exists: the
order components are read in, which circuit each belongs to and how circuits attach to their
parents, which circuits share a header, where instruments and controllers belong, and what the
language inferred. The layout engine ([`28`](28-layout-solver.md)) starts from it; the frontend
reads the parts that are not geometry (tab order, arrows, groups to fold).

**What this document used to be.** Until `D-103` the frontend laid the diagram out from these
hints, and the payload carried everything a layered layout needs: a rank per component, a side per
port, every loop of the cycle basis with an orientation, a shape signature per branch. Core now
draws the diagram itself, and those fields had no reader left; `D-107` removed them from Core, the
wire and the tests. The rule for what belongs here is the one that survives: **only Core knows it,
it is a fact about the graph, and something reads it.**

## The payload

| Field | What it is | Who reads it |
|---|---|---|
| `Order` | Every graph component once, in a stable depth-first walk from each hydraulic part's pressure datum, ports in declaration order, back edges deferred (`FS2401`) | The engine (which component is placed first; ties); the frontend (tab order) |
| `Circuits` | Every circuit in declaration order: name, number, resolved role, parent, and for an attached circuit the parent's component it takes flow from and the one it returns to | The engine (a branch's entry and exit) |
| `DistributionGroups` | Sets of circuits sharing one supply/return pair on one parent (`D-33`); never fewer than two members (invariant 11) | The engine (a header) |
| `CircuitOf` | The owning circuit per component (`D-33`; the enthalpy-losing side of a two-sided component, `D-36`) | The engine (which components a branch has); the frontend (grouping, tags) |
| `NonFlowElements` | For each controller and placed instrument: the component it is drawn beside, what it reads, what it drives, its position in the tab order | The engine (instrument placement, signal lines) |
| `Inferred` | Components the language added rather than the script wrote | The engine (an inferred node is placed as a node, `28` A6, and is never the first component, C1); the frontend (hover) |
| `Groups` | Every graph element one written component expanded into (a pipe's internal nodes) | The frontend (folding, `FS2402`) |
| `Flow` | Per connection, the solved flow direction: `forward` as written, `reverse`, or `none` inside the zero-flow tolerance or unsolved | The frontend (the arrows) |
| `ThermalStages` | The heat-progression classification below | `FS2403` only; nothing places by it |

Everything is a pure function of the graph, the model and the solved branch flows, byte-identical
for one input (invariant 4).

## Thermal-stage classification

Not a placement input any more (`D-107`): the classification exists because it is how `FS2403`
knows a circuit's name contradicts its duties. It stays as specified.


Thermal staging is computed on a separate **thermal group graph** (`D-31`):

1. Collapse every `Loops` entry and every `ComponentGroup` to one vertex; every remaining component is
   its own vertex. A `D-32` tank vertex is always classified as Storage. A component may appear in only
   one collapsed vertex, with pipe expansion groups
   taking precedence over loop membership for presentation while inheriting the loop's local order.
2. Add directed transport edges from nominal connection direction. Add a directed heat-transfer edge
   across each extended exchanger, from the side losing nominal enthalpy to the side gaining it.
3. Classify vertices before ranking: boundary groups injecting enthalpy and cooling/source circuits are
   `Source`; extended exchangers are `Conversion`; `tank` is `Storage`; boundary groups extracting
   useful heat are `Consumer`; everything else is `Neutral`. Stated duty sign and terminal
   temperatures are authoritative.

   **A circuit role is evidence, not an override** (`D-35`). A vertex whose components all belong to a
   circuit with a resolved role adopts that role's stage when the rules above leave it `Neutral`, and
   is overruled by them when they do not. A circuit named `radiators` whose duty sign says it is
   giving heat away is a source regardless of its name: the name is what the user called it, the duty
   is what the physics says, and where they disagree the physics wins and `FS2403` says so. This
   ordering is what keeps a mislabelled circuit from silently reversing a diagram. A boundary with stated flow but no temperature or duty is then
   classified from nominal connection direction: an edge leaving the boundary makes it `Source`, and
   an edge entering it makes it `Consumer`. Only a directionally ambiguous boundary remains `Neutral`;
   source order breaks ties but never reverses a nominal boundary role.
4. Condense strongly connected transport components. Within one condensed component, preserve
   the members' local order. Across heat-transfer edges and classified storage progression, assign
   stage rank by longest path from any `Source`, with equal-role parallel vertices sharing the same
   rank. `Conversion` and `Storage` precede every downstream `Consumer`.
5. Attach a `Neutral` vertex using directed nominal-flow distance. Prefer its nearest classified
   predecessor reachable along incoming edges; if none exists, use the nearest classified successor
   reachable along outgoing edges. Equal directed distances choose the lower stage rank, then ordinal
   component id. With no classified vertex, put the entire connected component in one rank-0
   `Neutral` stage. This prevents an undirected shortcut through a return branch from moving a
   source-side boundary to a consumer stage.

**How P5.1 read steps 1–5 (implementation precision, 2026-09-15).** Three places where the text
above admits more than one reading, and what shipped:

- *Loops are banded, not collapsed.* A cycle-basis loop is not always the small recirculation loop:
  on the distribution header the basis holds four loops and two of them run through both consumers'
  headers. One vertex spanning two circuits could carry neither's role, so step 1 collapses pipe
  expansions only, classifies per component, and then gives a loop's non-pivot members the highest
  rank among them (a recirculation loop off an exchanger sits wholly on its cold side).
- *Classification is relative to a pivot.* A pivot is an extended exchanger — one with its second
  side wired, or one that can be rated (`ua`, `area`, `u`) — or a tank. What reaches a pivot's losing
  or charging side without crossing another pivot is `Source`; what its gaining or discharging side
  reaches is `Consumer`; a vertex both reach, or neither, is not classified by topology. With no
  pivot in a part nothing is, which is what makes the cooling loop one `Neutral` stage even though it
  has a supply and a return boundary: the "boundary classified from nominal connection direction"
  sentence in step 3 is **not** applied on its own, because every open loop would then split into a
  source and a consumer either side of nothing.
- *A role is used only where it is `Source` or `Consumer`.* A registered `Neutral` role (`heating`,
  `cooling`, `distribution`) classifies nothing and is never contradicted by a duty sign: whether a
  positive duty is a source or a load depends on which of those two the circuit is, and the role is
  what says so. `FS2403` therefore fires only for a `Source`/`Consumer` role whose members' net stated
  duty has the other sign. This is project reasoning; the renderer work in P5.3 is where it gets
  tested against real diagrams, and the open question below records what it leaves undecided.

**Ownership of a two-sided vertex follows `D-36`, and it is read off the same edge.** Step 2 already
adds a directed heat-transfer edge from the side losing nominal enthalpy to the side gaining it; the
owning circuit is the one on the losing end. No separate traversal, no geometry, and the same edge
that decides *where* a component sits decides *whose* it is — which is why the two rules cannot
disagree. An indeterminate edge falls back to the lower circuit number with `FS2216`.

Sort stages by rank and components within a stage by source order then ordinal id. The result is a
deterministic total sequence of stage records representing a stable partial thermal order, not a claim
about every connection. Several groups may share any rank. A transient duty/flow reversal changes
`Flow` and state but never `ThermalStages`; otherwise a frame could relayout the canvas (`D-31`).

## Stable ids

The renderer must preserve selection, keyed DOM nodes, worker commits, and export identity across the
next keystroke. That requires an id that survives an edit elsewhere in the script.

**The id is the component's name.** Declared components use the user's identifier; inferred ones use
their derived name (`HE1__3WV`, `N1`, `P1__2`). Both are stable under edits that do not touch them.

**The equipment tag is not the id, and must never be used as one** (`D-34`). `400PU01` is a label:
it is derived from declaration order, so inserting a pump above another changes the tags of every
pump below it while changing no identifier. A renderer that keyed selection, DOM nodes, worker commits
or export identity by tag would invalidate all four on that insertion — the exact churn the decision
exists to prevent, and it would look like a mysterious flicker rather than an obvious bug. Tags reach
the frontend through the model contract as component metadata
([`26-model-contract`](26-model-contract.md)) and are used for display only.

**Identifiers are unique across the whole model, not per circuit** (`D-41`), so every id-keyed
structure here stays a flat `string` and no qualification is needed anywhere. `CircuitOf` records
*membership* — which circuit owns a component, for grouping and tagging — and is never a
disambiguator. Two pumps on two branches of a header are `PU_AHU` and `PU_RAD` in the script, and
`101PU01` and `102PU01` on the drawing.

The failure mode is renaming: `HE1` → `HX1` looks to consumers like one component disappearing and
another appearing. Options considered were a content hash (unstable under a parameter edit), a source
position (unstable under an insertion above), and a synthetic id in a sidecar (violates the one-file
source model). Name identity is the least-bad choice. `IScriptEditor.Rename`
([`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md)) therefore reports an
old-id/new-id mapping so the frontend can migrate selection and focus without guessing. A rename typed
directly is remove-plus-add; computed layout remains deterministic in either case.

## Ordering determinism

`Order` must be identical for identical graphs, because an unstable order makes the diagram
jump on every keystroke — the single most annoying possible failure of the live-render loop.

Sources of nondeterminism to eliminate:

- **Dictionary iteration order.** Every traversal iterates a sorted or explicitly-ordered collection.
- **Parallel branch order.** Branches from a junction are visited in the order their connections appear
  in the *script*, not in graph-construction order.
- **Tie-breaking.** Every tie breaks on the component name, ordinally.

This is invariant 6 of [`23-topology-and-graph`](23-topology-and-graph.md) extended into the hints, and
it deserves its own test: build a graph, permute the input statement order in ways that do not change
the topology, and assert `Order` is unchanged for the components that did not move.


## Invariants

1. No field carries a coordinate, a dimension, a pixel, a tag, a spacing or a layout-mode name.
2. Every graph component appears in `Order` exactly once and in `CircuitOf` exactly once.
3. `Circuits` lists every circuit in declaration order; an attached circuit names both anchors or
   neither.
4. Identical input, identical hints, across builds.
5. A `DistributionGroup` has two or more members, in declaration order.
6. `NonFlowElements` names only components that exist, and the navigation order is unique across
   flow components and non-flow elements together.
7. A transient reversal changes `Flow` and never anything else.

## Error cases

Derivation reports and never throws. `FS2401` (a closed circuit has no first component; the walk
starts at the datum), `FS2402` (a group past ten members, or a scene past five hundred elements,
starts folded), `FS2403` (a circuit's name and its duties disagree). All informational.

## Acceptance criteria

- The hints tests cover order stability under statement permutation, the four inferred components
  of the cooling loop, the header's one distribution group of two subcircuits, the substation's
  circuit ownership either way round, the controller's anchoring, `FS2403`, `FS2402`, and the
  no-geometry invariant by reflection over every hint type.
- Every sample's components are all ordered and all staged.
