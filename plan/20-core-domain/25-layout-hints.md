---
id: 25-layout-hints
title: Layout hints
tier: 20-core-domain
status: draft
owns: [the layout hint payload, topological ordering, thermal stages, port sides, grouping, stable component ids, circuit membership, distribution grouping]
depends_on: [23-topology-and-graph]
traces_to: [R-22, R-27, R-44, R-45, R-46, R-47, R-48]
open_questions: 0
last_review_pass: 0
---

# Layout hints

## Purpose

Implements `D-03`'s backend half while consuming `D-20`'s Core-owned symbol identity: Core tells the renderer everything it knows about *structure*, and
nothing about *pixels*. The line between those two is the whole content of this document, and it is
worth drawing carefully — put too little here and the frontend re-derives topology from a flat graph
(badly); put too much and Core is a layout engine that cannot be tested without a canvas.

## Responsibilities

**Owns.** The `LayoutHints` payload, topological ordering, port-side assignment, grouping, and the
stable-id scheme the renderer keys placements to.

**Explicitly does not own.** Coordinates, routing, symbol shapes, or anything measured in pixels
([`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md)); the graph itself
([`23-topology-and-graph`](23-topology-and-graph.md)); the wire format
([`26-model-contract`](26-model-contract.md), which carries this payload).

## The test for what belongs here

> **Would two competent renderers, given only the graph, disagree about it? And is the disagreement a
> topology question rather than a taste question?**

Both yes → it belongs in hints. "Which node is upstream" is a topology fact the renderer would have to
re-derive by walking the graph. "How far apart to place them" is taste, and Core has no business
holding an opinion.

| Fact | In hints? | Why |
|---|---|---|
| Flow direction along a branch | **Yes** | Topology. Determines arrow direction; recovering it needs the solved flow sign. |
| Rank (distance from the source) | **Yes** | Topology. Every layered layout needs it; computing it means a graph traversal. |
| Thermal stage (source → conversion/storage → consumer) | **Yes** | Thermodynamic structure. Keeps heat progression left-to-right across coupled and parallel circuits (`D-31`). |
| Which port is inlet vs outlet | **Yes** | Component metadata the renderer does not otherwise have. |
| Loop membership | **Yes** | Already computed for the solver; recomputing it in TypeScript is duplicated cycle detection. |
| Grouping (subsystem) | **Yes** | Semantic. Only Core knows a set of components is a subsystem. |
| Component `x`, `y` | **No** | Pixels. `D-03`. |
| Pipe route polyline | **No** | Pixels. |
| Symbol size | **No** | Presentation. |
| Label placement | **No** | Presentation. |
| Colour | **No** | Style; comes from the `style` directive, not from hints. |
| Circuit membership | **Yes** | Structural. Only Core knows which circuit a component was declared in, and `D-36` decides which one owns a two-sided component. |
| Which circuits form a distribution group | **Yes** | Topology. Whether a set of subcircuits shares one supply/return pair is a graph question the renderer would re-derive by walking attachments. |
| Which layout mode to use | **Yes**, as structure | Core states the grouping; the renderer chooses the mode from it (`D-38`). Core never names a mode, because "header" and "rectangle" are shapes and shapes are pixels. |
| Component spacing | **No** | A distance. `spacing` travels in style settings and never enters this payload (`D-37`, invariant 1). |
| Equipment tag | **No** | Not structure. Tags are component metadata and cross the wire in the model contract ([`26`](26-model-contract.md)), not as a placement hint (`D-34`). |

## The payload

```csharp
/// <summary>Structural advice for a renderer. Contains no geometry (D-03).</summary>
public sealed record LayoutHints
{
    /// <summary>Components in a stable topological order, sources first.</summary>
    /// <remarks>
    /// A left-to-right or top-to-bottom layout can follow this directly. Cyclic graphs — every
    /// closed circuit — have no true topological order, so this is the order of a depth-first
    /// walk from the pressure datum with back edges deferred. Deterministic, and stable
    /// under edits that do not change the graph.
    /// </remarks>
    public required ImmutableArray<string> Order { get; init; }

    /// <summary>Rank for non-loop components. Pure trees use hops from the pressure datum; a tree
    /// attached to a loop uses hops from its attachment. Loop members are deliberately absent.</summary>
    /// <remarks>The local layer index in a layered layout. Components in a parallel branch share a rank.</remarks>
    public required ImmutableDictionary<string, int> Rank { get; init; }

    /// <summary>Nominal heat-progression stages, ordered left to right (`D-31`).</summary>
    /// <remarks>
    /// Stages are fixed at compile/design point and do not change during a transient. Existing
    /// hydraulic <see cref="Rank"/> is local ordering within or around a stage; it cannot move a
    /// source to the right of a consumer. Parallel groups share a stage rank.
    /// </remarks>
    public required ImmutableArray<ThermalStage> ThermalStages { get; init; }

    /// <summary>Per-connection flow direction at the solved operating point.</summary>
    /// <value>
    /// <c>Forward</c> as written, <c>Reverse</c> when the solved flow is negative,
    /// <c>None</c> when it is within the zero-flow tolerance (a dead leg draws no arrow).
    /// </value>
    public required ImmutableDictionary<ConnectionId, FlowDirection> Flow { get; init; }

    /// <summary>Suggested side for each port, from its role and the component's orientation.</summary>
    /// <value>Inlets <c>West</c>, outlets <c>East</c>, a three-way valve's bypass <c>South</c>.
    /// A hint, not a constraint — the renderer may rotate a component and reassign.</value>
    public required ImmutableDictionary<PortId, PortSide> PortSides { get; init; }

    /// <summary>Components forming each independent loop, in traversal order.</summary>
    /// <remarks>A renderer can lay a loop out as a closed circuit rather than a tree.</remarks>
    public required ImmutableArray<ImmutableArray<string>> Loops { get; init; }

    /// <summary>Orientation per loop, aligned by index with Loops.</summary>
    /// <remarks>Derived from solved flow leaving the loop's first flow-driving component; ties use
    /// declaration order. Supply is placed on top and return below (`D-30`).</remarks>
    public required ImmutableArray<LoopOrientation> LoopOrientations { get; init; }

    /// <summary>Semantic groupings — all expanded children of a discretized pipe, a future subsystem.</summary>
    /// <remarks>
    /// A group should be drawable as one collapsible unit. Backs the collapse/expand
    /// interaction for a pipe with 20 internal nodes, without which the diagram is unreadable.
    /// </remarks>
    public required ImmutableArray<ComponentGroup> Groups { get; init; }

    /// <summary>Placement/navigation anchors for rendered non-flow elements such as controllers.</summary>
    /// <remarks>
    /// These elements are absent from the hydraulic graph. Each is placed beside the component that
    /// owns its actuated parameter; the measurement target is routed as an observer line and does not
    /// affect thermal stage or hydraulic rank.
    /// </remarks>
    public required ImmutableArray<NonFlowElementHint> NonFlowElements { get; init; }

    /// <summary>Which circuit each component belongs to, by component id (`D-33`).</summary>
    /// <remarks>
    /// For a two-sided component this is the owning circuit under `D-36` — the side losing nominal
    /// enthalpy — which may differ from the circuit block its declaration sits in. Every component in
    /// the graph appears exactly once.
    /// </remarks>
    public required ImmutableDictionary<string, string> CircuitOf { get; init; }

    /// <summary>Every circuit, in declaration order, with the structure a renderer needs (`D-33`).</summary>
    public required ImmutableArray<CircuitHint> Circuits { get; init; }

    /// <summary>Sets of circuits sharing one supply/return pair (`D-38`).</summary>
    /// <remarks>
    /// A renderer draws a group of two or more as a header — supply along one edge, return along the
    /// other, members stacked between — and anything else as a loop rectangle. <b>Core states the
    /// grouping and never the mode</b>: "header" is a shape, and shapes are the renderer's under
    /// `D-03`. Groups are disjoint and every circuit appears in at most one.
    /// </remarks>
    public required ImmutableArray<DistributionGroup> DistributionGroups { get; init; }

    /// <summary>Components created by inference (I1/I2/I3) rather than written.</summary>
    /// <remarks>Rendered differently — lighter, or hidden behind a toggle — so the user can tell
    /// what they wrote from what the language added (principle P3).</remarks>
    public required ImmutableHashSet<string> Inferred { get; init; }
}

/// <summary>One circuit's structural facts (`D-33`, `D-35`).</summary>
public sealed record CircuitHint
{
    public required string Name { get; init; }

    /// <summary>The circuit's number, stated or resolved. The leading part of every tag it owns.</summary>
    public required int Number { get; init; }

    /// <summary>Resolved role, or null when the name matched no registry entry (`D-35`).</summary>
    /// <remarks>Feeds thermal classification: a consumer role biases the circuit's stage rightward,
    /// a source role leftward. Null means Neutral and is not an error.</remarks>
    public CircuitRoleHint? Role { get; init; }

    /// <summary>Parent circuit name, or null when this circuit stands alone (`D-33`).</summary>
    public string? ParentCircuit { get; init; }

    /// <summary>The parent's component this circuit takes flow from, when attached.</summary>
    public string? SupplyAnchorId { get; init; }

    /// <summary>The parent's component this circuit returns flow to, when attached.</summary>
    public string? ReturnAnchorId { get; init; }
}

public sealed record CircuitRoleHint(string CanonicalName, ThermalStageRole Stage);

/// <summary>Circuits sharing one supply/return pair, in stacking order (`D-38`).</summary>
/// <remarks>
/// <see cref="Members"/> is ordered by each member's declaration order, which is what keeps the
/// stacked branches from reordering between renders. <see cref="ParentCircuit"/> owns the two header
/// lines themselves.
/// </remarks>
public sealed record DistributionGroup
{
    public required string ParentCircuit { get; init; }
    public required ImmutableArray<string> Members { get; init; }
}

public sealed record ComponentGroup
{
    /// <summary>Stable logical id of the declared component represented by the group.</summary>
    public required string ParentComponentId { get; init; }

    /// <summary>All lowered graph children owned by that component, in deterministic local order.</summary>
    /// <remarks>For a pipe with <c>nodes=4</c>, this contains its four thermal-node ids followed by
    /// its five hydraulic sub-pipe ids. Child ids occur in exactly one group.</remarks>
    public required ImmutableArray<string> Children { get; init; }
}

public sealed record ThermalStage
{
    public required int Rank { get; init; }
    public required ThermalStageRole Role { get; init; } // Source | Conversion | Storage | Consumer | Neutral
    public required ImmutableArray<string> Components { get; init; }
}

public sealed record NonFlowElementHint
{
    public required string ComponentId { get; init; }
    public required string PlacementAnchorId { get; init; }
    public required string MeasurementTargetId { get; init; }
    public required string ActuationTargetId { get; init; }
    public required int NavigationOrder { get; init; }
}
```

### Thermal-stage derivation

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
   `Rank`/`Loops` local order. Across heat-transfer edges and classified storage progression, assign
   stage rank by longest path from any `Source`, with equal-role parallel vertices sharing the same
   rank. `Conversion` and `Storage` precede every downstream `Consumer`.
5. Attach a `Neutral` vertex using directed nominal-flow distance. Prefer its nearest classified
   predecessor reachable along incoming edges; if none exists, use the nearest classified successor
   reachable along outgoing edges. Equal directed distances choose the lower stage rank, then ordinal
   component id. With no classified vertex, put the entire connected component in one rank-0
   `Neutral` stage. This prevents an undirected shortcut through a return branch from moving a
   source-side boundary to a consumer stage.

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

Two components in different circuits may share an identifier — `101PU1` and `102PU1` are both `PU1` —
so **the id is qualified by circuit** wherever it crosses a boundary. `CircuitOf` gives the
qualification; within one circuit's own structures the bare name is unambiguous.

The failure mode is renaming: `HE1` → `HX1` looks to consumers like one component disappearing and
another appearing. Options considered were a content hash (unstable under a parameter edit), a source
position (unstable under an insertion above), and a synthetic id in a sidecar (violates the one-file
source model). Name identity is the least-bad choice. `IScriptEditor.Rename`
([`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md)) therefore reports an
old-id/new-id mapping so the frontend can migrate selection and focus without guessing. A rename typed
directly is remove-plus-add; computed layout remains deterministic in either case.

## Ordering determinism

`Order` and `Rank` must be identical for identical graphs, because an unstable order makes the diagram
jump on every keystroke — the single most annoying possible failure of the live-render loop.

Sources of nondeterminism to eliminate:

- **Dictionary iteration order.** Every traversal iterates a sorted or explicitly-ordered collection.
- **Parallel branch order.** Branches from a junction are visited in the order their connections appear
  in the *script*, not in graph-construction order.
- **Tie-breaking.** Every tie breaks on the component name, ordinally.

This is invariant 6 of [`23-topology-and-graph`](23-topology-and-graph.md) extended into the hints, and
it deserves its own test: build a graph, permute the input statement order in ways that do not change
the topology, and assert `Order` is unchanged for the components that did not move.

## What the renderer does with this

Not normative — [`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md) owns it — but stated so the
payload can be judged against a real consumer:

1. `ThermalStages` gives the global left-to-right band; parallel groups at one stage stack vertically.
2. `Rank` gives local columns within a band; components sharing a rank stack vertically.
3. `Loops` overrides local columns for loop members, which are laid out as a closed circuit without
   violating the global thermal-stage order.
4. `PortSides` orients each symbol.
5. `Flow` orients arrows.
6. `Groups` collapse into a single glyph until expanded.
7. `NonFlowElements` places controllers beside their actuation targets and orders them for keyboard
   navigation; observer lines route to `MeasurementTargetId`.
8. `Inferred` renders at reduced emphasis.

If the payload cannot drive those eight steps, it is under-specified. That is the review test for this
document.

## Invariants

1. `LayoutHints` contains no coordinate, dimension, or pixel value. `spacing` is the case most likely
   to be added here by mistake, because it reads as a layout input; it is a distance, it travels in
   style settings, and Core never interprets it (`D-37`).
2. Every component in the graph appears exactly once in `Order`.
3. `Rank` is defined for every non-loop component and absent for every loop member. Pure trees anchor
   rank 0 at the pressure datum; attached trees anchor rank 1 next to the loop attachment.
4. `Order` and `Rank` are deterministic functions of the graph.
5. Every `ConnectionId` in `Flow` exists in the graph.
6. Groups are disjoint — no component is in two.
7. `Inferred` is exactly the set of components whose `Origin` is not `Declared`.
8. Every component belongs to exactly one thermal stage. Stage ranks never decrease along a nominal
   heat-transfer edge; parallel source or consumer groups may share a rank.
9. Every rendered non-flow component appears exactly once in `NonFlowElements`; its placement anchor
   is its actuation target's component, and `NavigationOrder` is unique after the anchored component.
10. Every component in the graph appears exactly once in `CircuitOf`, and its value names a circuit in
    `Circuits`.
11. `DistributionGroups` are disjoint; every circuit appears in at most one, and a group's
    `ParentCircuit` is never also one of its own `Members`.
12. No field of `LayoutHints` holds an equipment tag, a spacing value, or a layout mode name.
    Invariant 1 already forbids the spacing on dimensional grounds; the other two are forbidden for
    the separate reason that they are not structure — a tag is display metadata (`D-34`) and a mode is
    a shape (`D-38`).

## Error cases

Hints are advisory: a hint that cannot be computed is omitted, never an error. Two info-level cases
exist because the user can see the consequence and would otherwise wonder:

| Code | Trigger | Severity |
|---|---|---|
| `FS2401` | The graph has no clean topological order (always true for a closed loop) — a DFS order is used | Info, suppressed by default |
| `FS2402` | A group exceeds 10 members or the scene exceeds 500 elements and will render collapsed | Info |
| `FS2403` | A circuit's role contradicts its solved duty direction; the duty is used | Info |

## Worked example

The **cooling loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)). Graph:
`N1 → N2 → PU1 → PU1__HE1 → HE1 → HE1__3WV → 3WV → {N2, 3WV__P1 → P1 → N3}`, pressure datum `N1`
(the first node with a stated `p`).

```
Order:  [N1, N2, PU1, PU1__HE1, HE1, HE1__3WV, 3WV, 3WV__P1, P1, N3]
        DFS from the datum, with the back edge 3WV→N2 deferred.

Rank:   N1 1 · 3WV__P1 1 · P1 2 · N3 3
        Loop members N2, PU1, PU1__HE1, HE1, HE1__3WV, 3WV are absent.

Flow:   every connection Forward — this circuit has no dead legs and no
        reverse flow at the design point.

PortSides:  PU1.in West · PU1.out East · HE1.in West · HE1.out East
            3WV.a West · 3WV.b North · 3WV.c South
            P1.in West · P1.out East

Loops:  [[N2, PU1, PU1__HE1, HE1, HE1__3WV, 3WV]]     one independent loop

LoopOrientations: [Clockwise]                flow leaves PU1 along the top supply

Groups: []                              no discretized pipes in this circuit

Inferred: {N2, PU1__HE1, HE1__3WV, 3WV__P1}           four of ten — the user wrote six
```

The cooling loop is one local thermal group, so its existing rectangle is unchanged. The **storage
header** reference is the cross-group case:

```
ThermalStages:
  0 Source   [S1, S2]
  1 Storage  [T1]
  2 Consumer [RAD_NETWORK, AHU_NETWORK]

PortSides: T1.in1 West · T1.in2 West · T1.out1 East · T1.out2 East
```

Sources and consumers at the same rank stack in source order. Their X coordinates do not interleave;
fluid-flow arrows remain independently governed by `Flow`.

### The distribution header

`D-33`'s sixth reference circuit is the multi-circuit case: parent `100` with subcircuits `101` (AHU)
and `102` (radiators) on one supply/return pair.

```
Circuits:
  { Name: heating,   Number: 100, Role: null,     ParentCircuit: null }
  { Name: AHU,       Number: 101, Role: ahu,      ParentCircuit: heating,
    SupplyAnchorId: N3, ReturnAnchorId: N5 }
  { Name: radiators, Number: 102, Role: radiator, ParentCircuit: heating,
    SupplyAnchorId: N4, ReturnAnchorId: N6 }

DistributionGroups:
  [ { ParentCircuit: heating, Members: [AHU, radiators] } ]

CircuitOf:
  N3 → heating · N5 → heating · PU1 → heating
  TV1 → AHU · PU1 → AHU          ← two PU1s, disambiguated by circuit
  TV1 → radiators · PU1 → radiators

ThermalStages:
  0 Source   [heating's boundary]
  1 Consumer [AHU, radiators]      ← both roles resolve to Consumer, so they share a rank
```

Three things this fixture pins that the storage header does not:

**Two components named `PU1` coexist.** Each circuit has its own symbol table, so `CircuitOf` is what
distinguishes them; a renderer that keyed a dictionary by bare name would silently collapse the two
into one and draw one pump. That is the failure this example exists to catch.

**The group is one entry, not two.** `AHU` and `radiators` attach to the same parent through different
nodes (`N3`/`N5` and `N4`/`N6`) and still form one distribution group, because grouping is by shared
*parent circuit*, not by shared node. Grouping by node would produce two groups of one and lose the
header entirely.

**The subcircuits share a stage rank.** Both roles classify as `Consumer`, so neither is placed
upstream of the other — they are parallel branches of one header, and a diagram that ranked `101`
before `102` would imply heat flows through the AHU on its way to the radiators.

Two observations. `3WV.b North` returns the recirculation branch to the junction it came from while
`3WV.c South` sends the primary return downward, so the two outlets separate without the renderer
having to guess which is which — a small hint doing real work. And **four of the ten components are
inferred**: the user wrote six declarations — four flow components plus two boundary nodes — and got a
ten-element graph, which is the ratio that makes `Inferred` worth carrying. A canvas that draws all ten
at equal weight is unreadable; one that dims the four the user never wrote reads as the circuit they
have in mind.

`N1` and `N3` are **declared**, not inferred, because they carry boundary conditions and the user
wrote them ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)'s inference inventory).
They draw at full weight, which is right: a node with a stated pressure is a real decision, not
scaffolding the language put in.

## Acceptance criteria

- [ ] The worked example's `Order`, `Rank`, `Loops`, `LoopOrientations`, and `Inferred` are reproduced exactly, including
      the **four** inferred components of ten — `N1` and `N3` are declared boundary nodes, not I1
      inferences ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)).
- [ ] A test asserts no field of `LayoutHints` has a length, position, or pixel dimension.
- [ ] Permuting independent statements in a sample leaves `Order` unchanged.
- [ ] Every non-loop component in every sample has a `Rank`; no loop member does.
- [ ] A pipe with `nodes=4` produces exactly one group containing the four internal-node ids and five
      hydraulic sub-pipe ids; the declared logical pipe id remains the group's stable parent id.
- [ ] Reverse flow in a bypass produces `Reverse`, and the canvas draws the arrow reversed.
- [ ] The renderer's eight-step consumption above runs on the payload with no additional graph query.
- [ ] The storage header produces the three thermal stages above. `S1`/`S2` share the left rank,
      `T1` is central, and both network boundaries share the right rank.
- [ ] The cooling loop becomes one rank-0 Neutral stage; the substation places its source-side circuit
      before `HX1`'s Conversion stage and its heating circuit after it; the result is byte-identical
      across 100 builds.
- [ ] A demand-step controller has one `NonFlowElementHint`, anchored to `3WV`, with its measurement
      target at `N2` and a navigation position immediately after `3WV`.
- [ ] Reversing a solved flow or duty in a transient changes `Flow` but leaves `ThermalStages`
      byte-identical to the run snapshot.
- [ ] The distribution header produces one `DistributionGroup` with both subcircuits as members, and
      `CircuitOf` distinguishes the two components named `PU1`.
- [ ] Both subcircuits share a `Consumer` stage rank; neither is ranked upstream of the other.
- [ ] A circuit whose role says `radiator` but whose duty sign says source classifies as `Source` and
      emits `FS2403` — the name never overrules the physics.
- [ ] The substation's exchanger appears in `CircuitOf` under the circuit on its enthalpy-losing side,
      and swapping the two circuit blocks in the source leaves that value unchanged.
- [ ] A test asserts no field of `LayoutHints` holds a tag, a spacing value, or a mode name — the same
      reflection test that already asserts it holds no dimension.

## Open questions

None. `Loops` owns loop-member placement, so `Rank` excludes them. Core supplies one deterministic
`LoopOrientation` per loop from flow leaving its first flow driver; the renderer places supply above
return. These choices prevent pressure-datum changes from rotating a diagram (`D-30`).
