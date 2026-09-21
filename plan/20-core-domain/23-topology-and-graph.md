---
id: 23-topology-and-graph
title: Topology and the circuit graph
tier: 20-core-domain
status: reviewed
owns: [circuit graph construction, branch decomposition, boundary conditions, well-posedness validation, lowering from the semantic model]
depends_on: [15-semantic-model, 21-fluid-and-state, 22-component-model]
traces_to: [R-06, R-11, R-16, R-43, R-45, R-46, R-47]
open_questions: 0
last_review_pass: 6
---

# Topology and the circuit graph

## Purpose

Turns the semantic model — names, kinds, and connection statements — into the graph the solver runs
on, and decides whether that graph can be solved at all. Most solver failures are topology failures
wearing a numerical disguise: a missing pressure datum presents as a singular Jacobian, an isolated
subgraph as a solution that will not converge. Catching them here, where the diagnostic can name a
component, is worth considerably more than catching them in the solver, where it cannot.

## Responsibilities

**Owns.** Lowering from the semantic model, the graph structure, branch decomposition, boundary
conditions, and well-posedness validation.

**Explicitly does not own.** Inference rules I1–I3, which the binder applies
([`15-semantic-model`](../10-language/15-semantic-model.md)); component equations
([`22-component-model`](22-component-model.md)); the numerical solve (tier 30); layout
([`25-layout-hints`](25-layout-hints.md)).

## The graph

```csharp
/// <summary>The solvable form of a circuit: nodes carrying state, components imposing equations,
/// and the branch decomposition the solver assigns flow unknowns to.</summary>
public sealed class CircuitGraph
{
    public string Name { get; }
    public ISubstance Substance { get; }
    public SolveMode Mode { get; }                        // Steady | Transient

    /// <summary>Every node, including inferred and internal-to-pipe ones.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; }

    /// <summary>Every component. Nodes appear here too — a node is a component.</summary>
    public IReadOnlyList<IComponent> Components { get; }

    /// <summary>Branches: maximal paths between junction elements containing no junction element.</summary>
    public IReadOnlyList<Branch> Branches { get; }

    /// <summary>Vertices of the branch graph: components with a flow group of three or more ports,
    /// plus terminals.</summary>
    /// <remarks>
    /// A component's ports partition into <b>flow groups</b> — sets of ports that must carry the
    /// same flow. A component is a junction element when any group has more than two ports, because
    /// only then do its ports carry different flows and it cannot be interior to a branch, whose
    /// defining property is that every component along it sees one flow.
    /// <para>
    /// Port count alone is the wrong test. A two-sided heat exchanger has four ports in
    /// <i>two</i> groups of two, so it is interior to a branch on each side and is not a junction;
    /// a three-way valve has three ports in one group and is.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IComponent> JunctionElements { get; }

    /// <summary>Independent loops, one per element of the cycle basis.</summary>
    /// <remarks>
    /// Sized <c>Branches.Count − JunctionElements.Count + 1</c> for a connected graph. The
    /// second term counts junction elements and terminals — the vertices of the <i>branch</i>
    /// graph — not every node: nodes interior to a branch are not vertices of it.
    /// <para>
    /// <b>Loops contribute no equations.</b> The formulation is nodal, so loop closure is
    /// satisfied identically and writing a pressure equation per loop would over-determine the
    /// system by exactly this collection's count. See "Loops are not equations" below. This is
    /// a layout and reporting artefact: the renderer partitions the diagram by it
    /// (<see href="25-layout-hints.md"/>) and <c>FS2214</c> names the offending loop.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Loop> Loops { get; }

    /// <summary>The node supplying the pressure datum, anchoring the whole pressure field.</summary>
    /// <remarks>
    /// The first node with a stated <c>p</c>, or an auto-picked one when none states a pressure.
    /// This is the datum, not "the pressure boundary condition" — a circuit may have many of
    /// those, and an open primary side normally does.
    /// </remarks>
    public GraphNode PressureDatum { get; }

    /// <summary>Boundary nodes: those with a stated t, p or flow, plus those created by rule I3.</summary>
    public IReadOnlyList<BoundaryNode> Boundaries { get; }
}

/// <summary>A maximal path between two junctions, carrying one flow unknown.</summary>
/// <remarks>
/// Every component along a branch sees the same mass flow, which is why the branch — not the
/// component — owns the unknown. A branch with three pipes and a valve in series contributes one
/// flow unknown and four pressure-drop equations.
/// </remarks>
public sealed record Branch
{
    public required BranchEnd From { get; init; }
    public required BranchEnd To { get; init; }
    public required ImmutableArray<IComponent> Path { get; init; }
}

/// <summary>One end of a branch: the junction element it meets, and the port it meets it at.</summary>
/// <remarks>
/// <b>Not a <c>GraphNode</c>.</b> A branch ends at a junction <i>element</i>, and a multi-port
/// component is a junction element without being a node — the cooling loop's branches end at
/// <c>3WV.ab</c>, <c>3WV.a</c> and <c>3WV.b</c>, which no node type can name. Typing both ends as
/// <c>GraphNode</c> made the branch table this document tabulates unrepresentable.
/// <para>
/// <see cref="Port"/> is null when <see cref="Element"/> is a node, since a node's ports are
/// unnamed and interchangeable.
/// </para>
/// </remarks>
public sealed record BranchEnd
{
    public required IComponent Element { get; init; }
    public string? Port { get; init; }
}
```

### Flow groups

Each component declares how its ports partition into sets that must carry the same flow (`D-63`:
`IFlowComponent.FlowGroups`, one entry per port holding that port's group id). This is the test
everything structural is built on, and port count alone is not it.

| Component | Flow groups | Junction element? |
|---|---|---|
| `node`, 2 connections | one group of 2 | No |
| `node`, 3+ connections | one group of 3+ | **Yes** |
| `node`, 1 connection | one group of 1 | **Yes** — a terminal, a branch has to end somewhere |
| `pipe`, `valve`, `pump` | one group of 2 (`in`, `out`) | No |
| `three_way_valve` | one group of 3 (`a`, `b`, `c`) | **Yes** |
| `heat_exchanger`, duty mode | one group of 2 (`in`, `out`) | No |
| `heat_exchanger`, rated mode | one group of 2 (`in`, `out`); side 2 is an external profile | No |
| `heat_exchanger`, coupled mode | **two** groups of 2 — `{in, out}` and `{in2, out2}` | **No** |
| `tank`, 2 materialized ports | one group of 2 | No; it is interior to one branch |
| `tank`, 3+ materialized ports | one group containing every materialized port | **Yes**; branches meet and mass balances at the vessel |

**The Coupled exchanger is the case that forced this refinement** (`D-17`, amended by `D-19`). Under the old "three or more
ports" test it counted as a junction, which would have split all four of its branches at it and given
it a mass balance it cannot satisfy: nothing flows from side 1 to side 2, so `Σ ṁᵢ = 0` across all four
ports is false whenever the two sides carry different flows — which is always. With flow groups it is
simply interior to a branch on each side, contributing one pressure relation per side and no mass
balance at all, which is what it physically is.

**A branch is written from its lower-numbered end** (`C-25`). A decomposition walks from whichever
junction it reached first, so without a rule `Path` comes out in that order or its reverse, and both
readings are defensible. The solver does not care; a golden test over a rendered branch table does, and
so will write-back. The canonical orientation is therefore: **`From` is the end whose element comes
first in `CircuitGraph.Components`, `To` is the other, and `Path` runs between them in that direction.**

That is the orientation the decomposition already produces --- measured across the corpus when the rule
was written, every branch of every sample ran ascending, 20 of 20 --- so this fixes what was already
true rather than changing anything, and `BranchOrientationTests` keeps it that way. Nothing downstream
should read `Path` order as *flow* direction: flow direction is the sign of the branch's solved mass
flow, and a branch whose flow is negative runs against its written orientation, which is legal and
common.

**A component may therefore appear in more than one `Branch.Path`.** A Coupled exchanger appears in two,
one per side. `Path` is not a partition of the component set and was never claimed to be, but it is
worth stating because the natural implementation — walk every component once, assign it to a branch —
silently drops one side.

**Branches, not components, own flow unknowns.** A series chain shares one flow by mass conservation,
so giving each component its own unknown would add equations that say only "these are equal" — more
unknowns, a larger Jacobian, worse conditioning, and no more information. This is the single most
consequential structural decision in tier 20 for solver performance.

## Lowering

The semantic model arrives with inference already applied. Lowering does five things:

1. **Instantiate components.** Each `ComponentSymbol` that carries flow becomes an `IComponent` via
   the registry, with its stated parameters converted to SI. **An observer is not one of them** — see
   below. **A node is built after step 3, not here**: a node's ports are unnamed and positional, so
   nothing but the connection list says how many it has, and that count decides both its port count and
   whether it carries a mass balance. **A pipe's bore is not a stated parameter either**: the script
   states `dn`, and the designation becomes a bore through [`27`](27-component-catalog.md), which ships
   a package later — lowering takes the mapping as an injected lookup (`C-24`). **An implicit pipe
   (I7, `D-110`) is a `pipe` symbol like any other by the time it arrives here**; lowering does not
   know which lines were written as `P1 pipe length=25` and which as `N5 - N1 length=25`, and a
   zero-length one is a real edge that drops nothing (`22`).
   Indexed tank ports have already been materialized by the binder; lowering maps each normalized
   level to exactly one bottom-to-top layer and does not create ports absent from source (`D-32`).
   **Heights arrive resolved** (`D-70`): the binder has already propagated every stated `elevation`
   into `SemanticModel.Heights`, so a node takes its `Elevation` from the map and a pipe its `Rise`
   as `z(out) − z(in)`; lowering computes no height of its own. A subdivided pipe shares its rise
   evenly and each internal node sits one share above the last.
2. **Materialise pipe internals.** A `pipe` with `nodes=n` expands into n internal thermodynamic nodes
   and n+1 hydraulic sub-pipes, each 1/(n+1) of the length. The internal nodes separately own equal
   shares `V/n` of the pipe's thermal volume; endpoint nodes own none. This happens here rather than in
   the component so the solver and renderer see the same state nodes (`R-10`). `nodes=0` creates one
   hydraulic pipe and no internal thermal storage; [`22`](22-component-model.md) owns the mapping.
3. **Build adjacency** from connections.
4. **Decompose into branches**, by walking from each junction until the next.
5. **Compute the cycle basis** — a spanning tree, then one independent loop per non-tree edge. Loops
   are used by layout and by `FS2214`, and by nothing in the equation system.

Lowering is where the semantic model's names stop mattering and the graph's structure starts. After
this point nothing knows a script existed, which is what makes the solver testable from a
hand-constructed graph.

### Heights span pipes and bare links, and nothing else

`D-70` made `elevation` an absolute height on every single-height kind, and the consequence for the
graph is one rule: **a component has one height and every port of it sits there; a pipe and a bare
node-to-node connection are the only edges that join two heights.** So heights flood from every
stated `elevation` through pumps, valves, exchangers, tanks and nodes alike, and stop at a pipe or a
bare link. A pipe's rise is the difference between the two classes it joins and its `ρgΔz` sits in
its own momentum row, with `−ṁgΔz` injected through `D-69`'s flux. A bare link between two nodes at
different heights is `D-25`'s ideal link with a hydrostatic term: the assembler's row is
`p_A − p_B − ρ̄g(z_B − z_A) = 0` at the mean density of the two ends, and the enthalpy arriving at the
higher node is read `g·Δz` lower (`ArrivingSource.Lift`), because there is no component between them
to inject it. Sizing counts the same term when it walks a loop's drop (`BranchResistance.Along` over
a `Branch`), which is what keeps a pump sized to friction alone on a loop that climbs through a pipe
and comes down through a link — measured at 45.8 m against 5.3 m before the walk counted it.

**An omitted height is inherited, and 0 only where nothing states one** (`D-95`). A pump wired
straight to a load on the roof is on the roof. Two *stated* heights meeting without a pipe between
them are a missing riser, `FS2219`, named on the later declaration with both components in the
message; the tool never picks one, because the difference is up to 10 kPa per metre of fabricated
pressure. The propagation is a union-find over connection endpoints in the binder
([`15`](../10-language/15-semantic-model.md) step 8b), after inference so the I2 node between two
components at different heights is the case the diagnostic describes.

**The tank is a single height for now.** `D-70` gives its ports `z_tank + f·H`; the tank has no
height `H` until P6.2 gives it geometry, so every port sits at the vessel's `elevation` and the pipes
reaching it carry the rise. Recorded as a deliberate partial, not an oversight.

### Observers are lowered past the graph, not into it

`D-61`'s instruments — `t_sensor`, `p_sensor`, `flow_sensor` — have no ports, no `DrivesFlow` and no
residuals, and **nothing about them reaches `CircuitGraph`**. A pass-through instrument would carry
two ports, gain an inserted node from rule I2 and contribute equations that are all identities; a
hundred sensors would double the size of the solve to compute nothing. Attachment is what keeps them
out.

They are still part of the model: `ModelObservers.Collect` builds them from the semantic model
alongside lowering, and each holds the *name* of the node it is placed on. That name is resolved
against the lowered graph when a result is reported or a controller reads one, which is the only
coupling between the two. An instrument whose node is absent — because it was never declared, or
because the `at` clause named something that is not a node — was already `FS1533` or `FS1532` at bind
time and is dropped here rather than reported again (`C-15`).

The consequence for this document's own counting is that **an observer changes no invariant below**.
It adds no node, no branch, no loop, no unknown and no equation, and invariant 5's unknown/equation
balance is unaffected by how many instruments a script places. That is the property worth testing
directly: the same circuit with and without a dozen sensors lowers to an identical graph.

### Loops are not equations

Step 5 previously read "loops are what the pressure equations are written around", which described a
**mesh** formulation the rest of the tree does not use. It is worth stating the correction plainly,
because the two formulations look interchangeable and are not.

The system assembled in [`31-solver-architecture`](../30-solver/31-solver-architecture.md) is
**nodal**: every node carries a pressure unknown, and every component contributes
`p_in − p_out = Δp(ṁ, …)`. Walking any cycle and summing those relations telescopes to zero
identically, for any iterate, because pressure is a single-valued field on the nodes. **Loop closure
is therefore not an equation to impose — it is a property of the unknowns.** Adding one pressure
equation per loop over-determines the system by exactly `Loops.Count`; on the cooling loop that is 21
equations against 20 unknowns, and `FS2210` fires on this tree's own reference circuit.

The sign convention is what makes the telescoping work with no special cases: pressure drop is
positive in the nominal flow direction for every component, negative for a pump (`22`'s convention 1).

A mesh formulation — branch flows as the only unknowns, one pressure equation per loop, node pressures
recovered afterwards — is a legitimate alternative and is smaller (B unknowns rather than 2N + B). It
was not chosen: it cannot express a node pressure boundary without a separate mechanism, it makes the
energy balance awkward because enthalpy lives on nodes the formulation does not carry, and every
diagnostic that names a node ("pressure at N3") has to be reconstructed. Both are correct; mixing them
is not.

## Boundary conditions

A circuit needs boundaries or it has no unique solution.

**Two different things are called "pressure boundary", and conflating them is a real error** — it makes
every open circuit with a supply and a return look over-specified. They are:

| Concept | What it is | How many |
|---|---|---|
| **Pressure datum** | The arbitrary zero the pressure field is measured from. Carries no engineering meaning. | Exactly **one** per connected component of the graph |
| **Pressure boundary condition** | A real constraint: this node is held at this pressure by something outside the model. Each one admits an unknown external mass flux. | **Any number**, including zero |

A stated `p` supplies a boundary condition. The *first* one in a connected component also serves as its
datum, so a circuit with one or more stated pressures needs no auto-pick. A circuit with none — a closed
loop, which is the common case — gets an auto-picked datum instead.

| Kind | Set by | Fixes |
|---|---|---|
| **Pressure datum** | the first stated `p`, or auto-picked | The pressure zero |
| **Pressure boundary** | `p` on a node, or `port.p` on a component touching it (`D-124`: the binder copies it onto the node) | That node's pressure, plus an unknown external flux |
| **Temperature boundary** | `t` on a node, or a heat exchanger's `in`/`out` | The energy datum |
| **Flow boundary** | `flow` on a node | A known injection or extraction |

**A boundary declares itself** (`D-64`, spelled `inlet`/`outlet` by `D-115`), **and is a terminal with one connection** (`D-115`, `FS2205`): the flow splits after the inlet, or merges before the outlet, at a node the script writes. `inlet` and `outlet` are node kinds that say which end of an
open circuit they are: a `inlet` requires `t` and exactly one of `flow` or `p`, and a `outlet`
requires nothing but gives its mass balance an unknown external flux instead of the zero-flow closure
a bare terminal `node` gets. That distinction is not derivable from parameters — a terminal with
nothing stated is a legitimate dead leg — so it is stated rather than inferred, which is what `P3`
requires of it. A closed circuit needs neither.

### Several circuits are one graph, not several graphs

`D-33` lets a script declare several circuits. **This does not multiply `CircuitGraph`.** The model is
one graph whose components carry circuit membership, for the reason the next subsection already
establishes: a rated exchanger produces more than one hydraulic connected component inside a single
graph, and energy spans what pressure does not. Circuits are the same shape of thing one level up —
an organisational and naming layer over a graph that was already prepared to hold disconnected
hydraulic parts.

So the existing rules carry over unchanged and need no per-circuit variants: one pressure datum per
*hydraulic connected component* (not per circuit), mass balance per hydraulic component, one energy
system over every node in the model, `FS2213` only for a subgraph coupled by nothing at all.

Because identifiers are unique across the model (`D-41`), an attachment endpoint is an ordinary
symbol-table lookup with no qualification: `inlet N3` finds the one `N3` there is. Had names been
scoped per circuit, each of the four attachment lines in the distribution header would have needed a
qualified form the language does not have — which is the argument that decided `D-41`.

**A subcircuit's attachment lowers to ordinary connections.** `inlet N3` in circuit 101 becomes a
connection from the parent's `N3` to 101's first unconnected inlet, and `outlet N5` a connection from
101's last unconnected outlet to `N5`. After lowering there is nothing structurally special about a
subcircuit: it is a set of components connected to the rest, and every well-posedness rule below
applies to it without modification.

That is the whole point of making attachment explicit. An inferred attachment would have to guess
which port of which component the header meets, and a wrong guess yields a graph that is well-posed,
solvable, and describes a different plant.

Two consequences worth stating because they surprise:

- **A subcircuit is usually not its own hydraulic component.** Attaching it to the parent connects
  them by flow, so parent and subcircuit share one pressure datum. A circuit boundary is a naming
  boundary, never automatically a hydraulic one.
- **A circuit may span hydraulic components, and a hydraulic component may span circuits.** Neither
  containment holds in either direction, which is why membership is a component-level field rather
  than a partition of the graph.

### Which circuit owns a two-sided component

`D-36`: a component touching two circuits belongs to the one on the side **losing** nominal enthalpy
across its heat-transfer edge. The graph already builds that directed edge for
[`25-layout-hints`](25-layout-hints.md)'s thermal staging — "from the side losing nominal enthalpy to
the side gaining it" — so ownership is read off it rather than computed a second way.

Resolution order, first match winning:

| Situation | Owner |
|---|---|
| A heat-transfer edge with a determinate direction | The circuit on the losing side |
| Both sides in one circuit | That circuit |
| One side against a boundary, the other in a circuit | The circuit side |
| Otherwise | The lower circuit number, with `FS2216` (info) naming the ambiguity |

**`FS2217` and `FS1518` partition one mistake between them and never both fire.** `FS1518` is the
binder's: the name resolves to nothing. `FS2217` is this document's: the name resolves, to a component
of the attaching circuit itself. Splitting by *whether resolution succeeded* rather than by document
convenience is what keeps a single typo from producing two errors — the outcome
[`16-diagnostics`](../10-language/16-diagnostics.md)'s rule 4 exists to prevent, and one that two
documents each owning a near-identical check would have produced.

The intuitive form of this rule is "the leftmost circuit owns it", and under `D-31` the losing side
*is* the left one — but leftmost is a layout outcome, and `D-03` forbids Core from computing anything
from geometry. Stated as enthalpy the rule is testable with no renderer, which is what makes the
substation acceptance criterion ("the tag does not change when the two circuit blocks are swapped in
the source") checkable at all.

**Ownership is a tagging and grouping question, never a solver one.** No equation, unknown, datum or
balance depends on it. A test asserts the solved state is identical with ownership forced either way,
because an ownership rule that leaked into the physics would make a drawing convention change results.

**Where it is computed (`P4.3`).** In the binder, as the step before tags: `BindingRun.ResolveOwnership`
walks each side's ports through inferred nodes to the first declared component and takes its circuit,
reads the losing side from the duty's sign (a role word carries it, `D-91`) or from whichever side's
stated terminals drop, and rewrites `ComponentSymbol.CircuitName` — which is what the tag, the graph's
`CircuitOf` and the layout hints then read. The "heat-transfer edge" `25` builds for thermal staging
is the same information, read before the graph exists rather than from it, because a tag is a binder
product and the binder runs on every keystroke whether or not anything lowers. The fallbacks are as
tabled, with one precision: a side wired to nothing is a boundary *profile* (Rated mode), so the
declaring circuit keeps the component; `FS2216` is raised from the binder, anchored on the
declaration.
### Hydraulically separate, thermally coupled

A rated heat exchanger joins two streams that never mix, so a circuit containing one has **more than
one hydraulic connected component** and that is correct rather than an error (`D-17`, and the
substation reference circuit). The rules follow from taking "connected" to mean *by flow*:

| Concern | Rule |
|---|---|
| Pressure datum | **One per hydraulic component.** The substation's primary gets its datum from `NPS p=600`; its secondary states no pressure and gets an auto-picked one with `FS2201`. |
| Mass balance | Per hydraulic component, with its own redundancy rule — a closed one drops a balance, an open one does not. Both mechanisms can apply in the same solve, to different components. |
| Energy balance | **Spans them.** One energy system over every node in the model, because that is exactly what the exchanger couples. |
| `FS2213` (isolated subgraph) | Fires only when a hydraulic component is coupled to the rest by **nothing** — no shared node *and* no shared component. A subgraph reachable through a two-sided exchanger is not isolated. |
| `FS2214` (loop with no driver) | Per hydraulic component, unchanged. |

**The energy block spanning what the pressure block does not is the whole structural content of
`D-17`.** It is also why the exchanger is not a junction element: coupling is through the *energy*
equations, and giving it a mass balance would assert that fluid crosses between the sides.

**The coupling makes the Jacobian less block-diagonal than it looks.** The hydraulic blocks of two
circuits are genuinely independent and could be factorised separately; the energy block is not, because
`Q̇` depends on both sides' flows and both inlet temperatures. A segregated solver that split by
circuit would iterate against a stale duty and converge slowly or not at all —
any future block-decomposition experiment in
[`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md) must preserve this coupling.

### The datum is mandatory and usually implicit

A closed loop with no stated pressure has a singular system — every solution shifted by a constant is
also a solution. Rather than erroring, the graph **picks one and says so** (`FS2201`, a warning since `D-115`: the static pressure of a closed circuit is a design number, and the script should state it): the
suction node of the first pump in graph order, and where the circuit has no pump, the node with the
most connections, ties broken by declaration order — deterministic and stable across edits either way.

**Why the suction (`D-98`).** The datum sits at 0 gauge, and until `D-121` the property backend
had a floor there — water was said to have no state below 100 kPa absolute — so the pick was not
arbitrary to the solver even though it is arbitrary to the physics. The most-connected node is usually
a header downstream of the pump, and the suction then sits *below* the datum by the losses between
them; on the substation that was −21 kPa gauge, and the first property read there failed before
Newton took a step. A single pump's suction is its loop's low point, so a datum there keeps every
other node at or above zero gauge.

**What the suction cannot do (`D-121`).** With two pumps on one ring the first pump's suction is the
second pump's discharge, and the second suction sits its own head below the datum (`S-29`: 29.4 kPa
for a 3 m booster). In a ring of *k* pumps every suction is a local low and which is lowest depends
on how the heads and losses fall out, which the solve finds and no pick can know. So the suction pick
stays for what it is — a stable, deterministic zero that is right for the common case — and the
property table no longer decides whether the solution is admissible: water's floor is its triple
point, the solve reaches a field with nodes below its datum, and `FS2221` says afterwards what the
relative figures cannot, which is the fill pressure that would keep the plant out of vacuum.

This is a deliberate softening of principle P3 ("infer only what is unambiguous"). The choice of *which*
node is arbitrary, but the choice's *consequence* is not — every pressure in the result is relative,
and the diagram displays them as such. The alternative, erroring until the user adds `p=`, makes the
syntax reference unsolvable as written, and the information the user would add carries no engineering
meaning in a closed loop.

**Two stated pressures are normal, not an error.** The cooling loop states `N1 p=300` and `N3 p=280`,
and it must: those two are what drive flow through the primary side. `FS2212` therefore fires only in
the genuinely degenerate case — two or more stated pressures inside a single loop with **no
through-flow path between them**, where the second is not a boundary condition at all but a second,
contradictory datum.

### I3's boundary nodes

Inference rule I3 terminates open ports. What condition the created node carries:

| Situation | Condition | Reasoning |
|---|---|---|
| Open port on a valve's bypass (`b`) | **Dead leg**: zero flow | A three-way valve used as a two-way. Zero flow is the physical truth. |
| Open port on any other component | Zero flow, plus `FS2202` (warning) | Almost certainly an unfinished script |
| A node with exactly one connection that is not an `inlet` or `outlet` | Zero flow, plus `FS2107`; a stated `p=` there is a datum on a stub, not a boundary (`D-86`, `D-115`) | Same |

The first two rows are the **inferred** cases and the third the **declared** one, which is what `C-7`
asked for: an I3 node carries zero flow and is a boundary in its own right. That is why the binder
exempts it from `FS2107` --- it *is* the boundary that rule created, so it terminates a port rather than
dead-ending on one, and it is `FS2202` that reports it if anything does.

Zero flow everywhere is the conservative choice: it changes no other result and it makes the graph
solvable, so the user sees a diagram with a visibly dangling stub rather than an error message.

## Well-posedness

Checked before the solver is invoked, because every one of these produces a much better message here
than in the linear algebra.

| Check | Failure | Code |
|---|---|---|
| Equation count equals unknown count | Over- or under-determined | `FS2210` / `FS2211` |
| A pressure datum exists per connected component | None stated → auto-picked (warning, `D-115`) | `FS2201` |
| A boundary has one connection | An inlet or outlet wired to several pipes | `FS2205` |
| A closed circuit's stated duties sum to zero | Heat with nowhere to go | `FS2203` |
| Fluid that enters a circuit can leave it | A boundary with no counterpart | `FS2204` |
| `FS2205` | A boundary node with more than one connection | Error | `'{node}' is an {kind} with {count} connections. A boundary has one; split or merge the flow at a node after it.` |
| Two stated pressures in one loop with no through-flow path between them | A second, contradictory datum | `FS2212` |
| Every branch is reachable from the pressure datum | Isolated subgraph | `FS2213` |
| No node has exactly one connection without being an `inlet` or `outlet` | Dead end | `FS2107` |
| Every loop lies in a block with a flow-driving component or a boundary pair | A passive block can only have zero flow | `FS2214` (**warning**) |
| Substance is resolvable and every state is inside its valid range at the initial guess | | `FS2215` |

### The counting argument

**A count that always balances is not a check.** The obvious version — B branch flows plus N node
pressures plus N node enthalpies against N mass balances, B pressure drops, N energy balances and a
datum, less one redundant mass balance — comes to 2N + B on both sides *for every possible graph*. It
can never detect an over- or under-specified circuit, so it cannot be what `FS2210` and `FS2211` are
raised from. The real count has to include the things a user actually varies: boundary conditions, and
the parameters a stated constraint promotes into unknowns.

**Vocabulary.** A **junction element** is a component with a flow group that does **not** hold exactly
two ports. A group of three or more is a split, and a group of one is a terminal — so the two halves
of the rule are one test on data the component already declares (`D-63`), rather than a port-group
test plus a degree the component cannot see. Port count alone is never the test: a four-port coupled
exchanger has two flow groups of two and is not a junction element, while a three-way valve has one
group of three and is. A **branch** runs between two junction elements and carries one flow. Nodes
*interior* to a branch (degree two) still carry pressure and enthalpy unknowns, but contribute no
independent mass balance — their "flow in equals flow out" is already expressed by the branch owning
a single flow unknown.

| Unknowns | Count |
|---|---|
| Branch flows | B |
| Node pressures | N |
| Node enthalpies | N |
| External mass flux, one per node that admits one and does not state its `flow` | X |
| Sized parameters promoted to unknowns (below) | P |
| **Total** | 2N + B + X + P |

| Equations | Count |
|---|---|
| Pressure relation, one per 2-port component; k−1 per k-port component; one per bare ideal link | C |
| Mass balance, one per junction element and terminal (**not** per interior node) | M |
| Energy balance, one per node | N |
| Stated pressure boundaries | X<sub>p</sub> |
| Component constraints beyond the component's own governing equation | K |
| Pressure datum, **only when no `p` is stated** in that connected component | D |
| less the redundant mass balance, **only when no external flux is unknown** | −R |
| less the redundant energy balance, one per closed steady circuit that couples to nothing | −L |
| **Total** | C + M + N + X<sub>p</sub> + K + D − R − L |

**A node admits an external flux when it is a `inlet`, a `outlet`, or states a pressure** — and when
it carries a mass balance at all, since a node interior to a branch has nowhere for external mass to
enter. A bare terminal admits none: its flux is zero, which is a dead leg (`D-64`).

**A stated `flow` is not an equation.** It names the flux outright, so the unknown never appears and
there is nothing for an equation to fix. Counting it as a row *and* keeping the unknown gives the same
total and a table that reads as though the circuit had to work to meet it; an earlier version of this
document did exactly that, as `X`<sub>f</sub>. The storage header is where it shows: every one of its
four boundaries states a flow, so it has no unknown flux at all.

**The datum and the redundant mass balance are two mechanisms, not one.** An earlier version of this
document said they were the same equation seen twice, and they are not: the datum fixes the *pressure
level* and is needed exactly when no pressure is stated, while the redundancy is about *mass* and
appears exactly when no external flux is unknown. A circuit whose only stated pressure sits mid-branch
has a datum and no redundancy; the storage header, whose four boundaries all state flows, has a
redundancy and an auto-picked datum. Both conditions are tested separately.

**The implementation must drop both redundant balances explicitly**, not rely on the linear solver to
cope. A singular-by-construction Jacobian handed to a factorisation is undefined behaviour dressed as
an algorithm.

### The enthalpy level is the temperature's datum, and only the script can supply it

Every energy relation in the model is a difference: `h_out = h_in + Q̇/ṁ` at each node, and nothing
else. Add the same offset to every enthalpy in a closed circuit and all of them are still satisfied,
so its energy block is rank-deficient by exactly one and the temperature field is determined only up
to a constant. That is the `L` row, and it is the exact mirror of the pressure datum (`D-65`).

**`L` is subtracted from the equations and is not added to the unknowns, and an earlier version of
this document had it the other way round** (`D-75`, `S-24`). The offset is a *null direction of the
node enthalpies already counted*, so there is no column for it to be: a column equal to the sum of
other columns is singular by construction, and no layout can allocate one. What is actually true is
the dual statement. Along any branch the two ends contribute the same upwind enthalpy times the same
flow with opposite signs — exactly, and through the smoothing band, since `Upwind(f, a, b) +
Upwind(−f, b, a) = a + b` for any blend with `σ(f) + σ(−f) = 1` — so summing such a circuit's node
energy balances cancels every convective term and leaves the injected duties, which sum to zero or
`FS2203` has already said the circuit has no steady state. One energy balance is therefore redundant
whatever the iterate, exactly as one mass balance is, and it is dropped the same way: first element in
graph order, so appending a component does not move which row went. Counted as an unknown instead, the
table read square on the simple loop while the assembler produced thirteen rows for twelve columns.

**What fills it is a stated temperature** — a node's `t`, or an exchanger's `in` or `out`. Those are
already counted as constraints, so the dropped row and the statement cancel and the count stays
square, in the same way `X` and `X`<sub>p</sub> do. A closed circuit that states no temperature anywhere is genuinely
under-determined and reports `FS2211` naming a temperature to add, which is the only script-reachable
route to that code found so far.

**The graph must not pick this datum for itself.** An arbitrary pressure zero leaves every result
correct; an arbitrary temperature zero does not, because 20 °C and 60 °C are different physics and
every property call reads the absolute value. So there is no `FS2201` for temperature.

Three conditions, each removing the freedom for a different reason:

| Condition | Why it removes the freedom |
|---|---|
| **Closed** | External mass arrives carrying an enthalpy, and that enthalpy *is* the level |
| **Steady** | A transient starts from an initial state, which fixes the level before the first step |
| **Coupled to nothing** | A two-sided exchanger's duty reads absolute temperatures on both sides, so a uniform offset on one side alone no longer satisfies its relation |

The substation's secondary is closed and needs no stated temperature of its own, because `HX1` is in
both hydraulic components. The simple loop is closed and uncoupled, and `HE1 in=20` is its level.

### Promotion: a stated constraint turns a sized parameter into an unknown

This is `D-02` reaching the solver, and it is what makes the count balance on a real circuit.

A parameter the user left unstated is normally chosen by sizing in the outer loop
([`24-auto-sizing`](24-auto-sizing.md)) and is a **fixed coefficient** by the time the solver runs. But
when the user states a constraint that the circuit can only satisfy by moving such a parameter, that
parameter becomes a solver **unknown** instead, and the constraint becomes its equation. The two arrive
together, so the system stays square.

| Stated constraint | Promotes | Because |
|---|---|---|
| A heat exchanger's `in` (mixed inlet temperature) on a circuit with a mixing valve | that valve's `position` | Only the mixing split can move the inlet temperature |
| A heat exchanger's `power` + `out` (fixing the flow) on a loop whose flow the pump sets | that pump's `head` | Only the head can move the loop flow |
| A node `t` downstream of a controlled branch | the controlling element's setting | Same argument, one component further away |
| **A duty that fixes the flow of a branch in a parallel set** | **that branch's `kv`**, on the first unsized valve along it | Parallel branches share their endpoint pressure difference, so a branch's flow can only be moved by changing its own resistance |
| **An extended-mode exchanger's design point, on a side nothing else pins** (`D-97`) | whatever the rows above would promote for a `FixedFlow` there — the substation's primary has stated boundary pressures, so its balancing valve's `kv` | The design point says what the side runs at; where a load's `dt` or a stated `flow` already says so, the exchanger's is design information only |

**The parallel row is the one that makes the common case work**, and it was missing. Two radiators on
two branches, each stating `power` and `dt`, pin two flows; nothing in the first three rows can move
them, so the circuit would count as over-specified by two and report `FS2210` — on the most ordinary
hydronic circuit there is. What actually moves a parallel branch's flow is its own resistance, which
is precisely what a balancing valve exists to provide.

**Promotion requires a candidate, and the diagnostic must say so when there is none.** The rule is:
find the first component along the branch with an unstated `kv` (a `valve`, then a `three_way_valve`).
If the branch has no valve at all, the flow is unreachable and the constraint is `FS2210` naming the
branch and suggesting one — *"nothing on the branch through RAD1 can change its flow; add a valve"* —
rather than the bare "over-specified by 1" that sends the user hunting.

**"Unstated" means unstated, un-defaulted and not chosen by a rule — and a bootstrap provisional is
none of those** (`D-96`). The outer loop's first lowering gives every unstated valve the catalogue's
largest Kv so that the valve exists; the graph carries those labels as `ProvisionalParameters`, and
`IsFree` reads one as free. Without that the row above and the pump row's fallback were dead from the
first count on (`C-75`): `PU1 pump head=15` on the simple loop, whose ring needs 5.28 m, reported
`FS2210` naming `HE1.in`/`HE1.out`, where the valve should have closed on the surplus — and does now,
solved to Kv 0.77. A promoted provisional stays provisional; the rule that would have sized it is
skipped, so nothing reports an authority for a Kv the solver chose.

**Promotion is what `FS2210`/`FS2211` measure against.** A constraint with nothing to promote is an
over-specification (`FS2210`); a free sized parameter with no constraint to pin it is left to sizing,
not to the solver, and only becomes `FS2211` when nothing determines it at all.

**`FS2211`'s advice reads the constraint list, not the script** (`D-90`, `S-52`, 2026-09-19). A
closed circuit's dropped enthalpy level is paid for by a stated temperature that promotes nothing;
a terminal pair that pins a flow and a stated inlet a mixing valve answers are each matched to a
promotion and pay for nothing. So a component can state four temperatures and the level still
float, and the message then leads with *a temperature on …* -- the value the graph could not have
chosen -- and keeps a pressure as the last resort. The earlier wording scanned the script for any
stated temperature, found the matched ones, and told the header to add a pressure to a hydraulic
half that was already square; taking that advice made the count square and the Jacobian singular.

**A stated flow is a constraint, and until P5.13b it was not** (`S-72`, 2026-09-20). `HE1 flow=0.3`
set the flow its 20 kPa was measured at and `PU1 flow=0.3` its curve's duty point, and the simple
loop solved to 0.086 kg/s and then out of the fluid's range -- against the invariant that a stated
parameter is always a constraint (`D-02`, `D-32`). Now `flow` on an exchanger's side (`flow`,
`in[2].flow`) or on a pump whose `head` is unstated is a `FixedFlow` row pinning that branch at the
number, answered by the rows above -- a pump's head, a parallel branch's `kv` -- but never by the
exchanger's own `power`, which does not appear in a flow residual. `vflow` is the same row through
the density of the side's inlet node *as solved*: `ṁ − ρ(p, h)·V̇ = 0`, the identity `V̇ = ṁ/ρ` written
where it holds; 0.3 l/s of 60 °C water pins 0.2950 kg/s, of 20 °C water 0.2995, and a conversion at
bind time with one density gets one of them wrong by 1.5 %. A pump with both `head` and a flow
stated is describing its curve (the point it passes through) and pins nothing. A node's `flow` stays
a boundary flux (`D-64`). The seed takes a stated volume flow at the density of the side's stated
inlet temperature, else 20 °C, and the solve corrects it.

**A consumer switched off asks nothing of its split and pins its branch at zero** (`S-56`,
2026-09-20). `power=0` is an operating state, not a missing size. With it, a stated `in` is
documentation of the coil's design point and not a demand: there is no flow to deliver 50 °C to, and a
`MixedInlet` row asking the split to hold it is 0/0 — so the row and the `position` it would promote
disappear together, and the count stays square. The `out` with `in` still pins the flow, at exactly
zero, and the pump's `head` still answers it; [`32`](../30-solver/32-steady-state-newton.md) says what
that head means and how the stopped branch's nodes are closed. The same change fixed the candidate
order for a mixed inlet, which took the first free split in the hydraulic: with one coil off and its
valve free, the *next* coil's inlet was handed the off coil's valve, which reaches its node through
nothing. The split at either end of the coil's own branch now comes first, as `S-45` already had it
for a node temperature.

**A promoted parameter may not also be stated.** `3WV position=0.78` on a circuit that also states
`HE1 in=20` is two things setting one unknown: `FS2210`, naming both, with the fix being to remove
either. This is the trap `D-02` creates and it is worth naming explicitly, because both lines look
individually reasonable and the interaction is invisible.

## Invariants

1. Every graph node is reachable from every other **through flow or through a shared component**, or
   the graph is reported as disconnected. A model may contain several hydraulic components coupled
   only by a rated heat exchanger (`D-17`).
2. `Loops.Count == Branches.Count − JunctionElements.Count + 1` for each connected component, where
   a junction element is a terminal or a component with a flow group containing at least three ports.
   Counting *all* nodes or raw component ports here is wrong whenever a branch has an interior node or
   a multi-sided component has several two-port flow groups.
3. Exactly one pressure **datum** per *hydraulic* connected component. The number of stated pressure
   **boundary conditions** is unconstrained.
3a. No component appears as a junction element unless one of its flow groups exceeds two ports, or it
   is a terminal.
4. Every component's ports are attached to a node — no component connects directly to another
   (guaranteed by inference rule I2).
5. Unknown count equals equation count after the redundant mass balance is dropped.
6. Lowering is deterministic: the same semantic model yields an identical graph, with identical node
   ordering, every time. **Ordering stability matters beyond determinism** — the renderer's placement
   memory and the solver's variable ordering both key off it.
7. `CircuitGraph` holds no reference to any syntax or semantic-model type.
8. A tank with K materialized ports contributes K−1 independent pressure relations and contributes a
   mass balance exactly when it is a junction/terminal. Normalized elevation never enters a hydraulic
   pressure equation.
9. No observer appears in the graph, and adding or removing observers leaves the graph byte-identical.
   Invariant 4 is about components with ports and an observer has none, so it is satisfied vacuously
   rather than by exemption.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS2201` | No pressure stated anywhere in a connected component | Warning | `Using '{node}' as the pressure datum. Pressures are relative to it.` |
| `FS2202` | Open port terminated | Warning | `'{component}' port '{port}' is not connected; treating it as closed.` |
| `FS2203` | A closed circuit whose stated duties do not sum to zero, solved as a steady state | Error | `'{circuit}' is closed and its heat does not balance: {power} with nowhere to go. Add a load, a source, or a boundary.` |
| `FS2204` | A hydraulic component with an `inlet` and no `outlet`, or the reverse | Error | `'{circuit}' has an {present} and no {missing}. Fluid must both enter and leave, or neither.` |
| `FS2210` | More equations than unknowns | Error | `This circuit is over-specified by {n}. Remove one of: {list}{advice}.` -- `{advice}` is `, or add a valve: nothing on the branch through {components} can change its flow` when an unmatched flow sits on a branch nothing can throttle, and empty otherwise (`C-28`); with no unmatched constraint `{list}` is every stated pressure, boundary or datum, and a stated pressure on a one-connection `node` adds `, or write '{node} outlet' (or inlet) if fluid crosses there: a node's p= holds the pressure level and passes no mass` (`C-106`) |
| `FS2211` | Fewer equations than unknowns | Error | `This circuit is under-specified by {n}. Add one of: {list}.` |
| `FS2212` | Two stated pressures in one loop with no flow path between them | Error | `'{a}' and '{b}' both set a pressure on the same closed loop, with no path between them for flow to take. Remove one, or connect them.` |
| `FS2213` | Isolated subgraph | Error | `'{list}' are not connected to the rest of the circuit.` |
| `FS2214` | Loop with no flow driver | Warning | `Nothing drives flow around {loop}; it will carry none. Is a pump on the wrong leg?` |
| `FS2215` | Initial state outside the substance's range | Error | `{substance} cannot be at {state}.` |
| `FS2216` | A two-sided component's owning circuit could not be determined from enthalpy | Info | `'{component}' touches {a} and {b} with no clear heat direction; tagging it into {chosen}.` |
| `FS2217` | A subcircuit's attachment endpoint resolves to its own circuit | Error | `'{circuit}' attaches to '{node}', which is one of its own components. A subcircuit attaches to another circuit.` |
| `FS2218` | A flow constraint answered by a pump on none of its owner's branches | Warning | `'{constraint}' is held by '{pump}', which is not on its branch. Every pump on that branch is stated or already claimed; if one was meant to hold this flow, free it.` |

| `FS2219` | Two stated heights joined by nothing that could span them | Error | `'{second}' at {b} m is wired directly to '{first}' at {a} m. Put a pipe between them, or give them one height.` |

| `FS2220` | The static head above the datum takes a node below the pressure its fluid can exist at | Error | `'{node}' is {rise} m above '{datum}', which puts it {short} kPa below the lowest pressure {substance} can be at. State a pressure on '{datum}' of at least {needed} kPa.` |
| `FS2221` | A converged node sits below atmospheric pressure | Warning | `'{node}' is {short} kPa below atmospheric pressure. State a pressure on '{datum}' of at least {needed} kPa.` |

**`FS2220` is the fill-pressure check, and it runs before the seed** (`S-60`). A script that states
no pressure has its datum picked at 0 gauge, and the top of a 32 m riser is then 213 kPa below
atmospheric — a state water does not have, which used to surface as `FS3007` after 0 Newton steps
with nothing said about height. With every node placed (`D-70`) it is arithmetic:
`p_datum − ρg(z − z_datum)` at the highest node of each hydraulic part, at the density the plant is
filled at (20 °C), against the substance's `MinimumAbsolutePressure`. One diagnostic per part, on the
highest node, suggesting the static head plus half a bar in whole tens of kPa — the margin
expansion-vessel sizing uses (pre-charge = static height + 0.2 bar, fill = pre-charge + 0.3 bar:
Flamco's *Reference Guide*, Reflex's *Professional planning, calculation and equipment*, IMI
Pneumatex's Statico manual, all after EN 12828). An error because the solve cannot reach a state, not
a warning about good practice. Since `D-121` the substance's floor is water's triple point, so this
fires only where the top would be at or below no pressure at all — the 32 m riser still is, at
−112 kPa absolute — and a top under partial vacuum solves.

**`FS2221` is the same check after the solve, on the solved field** (`S-29`). Heights are known before
the seed; where a loop's pressures fall relative to its datum — the second pump's suction on a ring,
the losses behind a header datum — is what the solve finds out. `FillPressure.ReportSolved` runs on a
converged solve, finds the lowest node of each hydraulic part, and where it sits below atmospheric
reports it with the pressure to state on the datum: the datum's stated value (0 for a picked one)
plus the depth of the vacuum plus the same half-bar margin, in whole tens. A warning, because the
circuit solved and in a loop with no stated pressure the figures are relative — the plant as
*written* would draw a vacuum there, and the message is the number that fixes it. A plant whose every
node sits above its datum is not reported, which is every closed sample in the corpus.

**`FS2219` is an error because the alternative fabricates pressure.** Only a pipe or a bare link
spans two heights (`D-70`); a valve that says 0 m wired straight to a load that says 32 m has left
the riser out, and picking either height would put up to 313 kPa into the loop that nothing wrote.
Reported by the binder on the later declaration's `elevation`, naming both, so either fix is one edit.

**`FS2218` reports a reach and does not stop it.** Promotion reaches across the plant on purpose: two
parallel branches below one shared pump are both served by it, the first taking its head and the
second falling to its own balancing valve (`S-45`). What the reach cannot tell apart is a shared
upstream pump from a sibling consumer's, and the case it was written for was silent — a source whose
own pump had been sized before its constraint was matched took a consumer's pump, that consumer took
the next, and the plant reported over-specified by one three promotions later with nothing naming
the first wrong claim (`S-59`). A warning, because the count is still right and the solve may be.

**`FS2214` asks the loop's block, not the loop** (`S-55`, 2026-09-20). The graph's loops are a
fundamental cycle basis, and a cycle with no pump on it still carries flow when a pump on another cycle
of the same biconnected block pushes through it: the pump-free mixing header's source valve and
exchanger form the small cycle `TV_MAIN.b → TV_MAIN.a → HS1 → N1`, driven by the consumer pumps that
draw from the supply and return through the same block, and the check reported it as carrying none on
a circuit that solves 45/45 in one iteration. `HydraulicBlocks.ForDrivers` labels every branch with its
block (Tarjan over branch endpoints) and a block is driven when any branch in it carries a
`DrivesFlow` kind or when a virtual ground joins two of its boundary nodes -- an `inlet`-to-`outlet`
path is driven by their pressures. A loop is reported only when nothing in its block moves anything;
a ring of one (`N1 - PU1 - HE1 - N1`) is its own block.

**`FS2214` is a warning, not info.** A loop with no driver is almost always a mis-placed pump — the
mistake the cooling loop's own history records ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) —
and its consequence is silent: the loop simply carries no flow, and every temperature downstream of it
is wrong in a way that still looks like a solved circuit. It was info; that was too quiet.

**`FS2203` and `FS2204` are consistency, not squareness, and the counting argument cannot see
either.** A closed loop with a 30 kW source and no sink has exactly as many equations as unknowns and
no solution: summing its energy balances gives `Σ Q̇ = 0`, and the stated duties do not. The mass
analogue is the same shape — a stated `flow` is a known injection, so a circuit that injects mass with
no `outlet` to take it is square and inconsistent. Both are cheap to check and impossible to reach by
counting, which is why they are listed here rather than folded into `FS2210`.

**`FS2203` fires in steady mode only.** The same circuit solved in time is perfectly valid: the water
heats up, which is what the storage term is for. A pump adds no heat in this model — it contributes a
pressure relation and no energy row — so a closed loop of pumps and pipes balances at exactly zero
rather than nearly zero, and the check needs no tolerance argument.

`FS2210` and `FS2211` **must name candidates**. "Under-specified by 1" is a puzzle; "add a pressure to
one of N1, N2, N3, or a flow to HE1" is a fix. Generating that list means tracking which unknowns are
unconstrained during the counting pass, which is real work, and it is the difference between a usable
tool and a frustrating one.

## Worked example

The **cooling loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)):

```
connections
N1 - N2
N2 - PU1
PU1 - HE1
HE1 - 3WV
3WV - N2
3WV - P1
P1 - N3

N1 inlet t=6 p=300
N3 outlet p=280
```

**Nodes**: `N1` and `N3` are declared (they carry boundary conditions), `N2` comes from I1, and
`PU1__HE1`, `HE1__3WV`, `3WV__P1` from I2 — every pair of directly-connected non-node components gets
one. `3WV`'s three ports are all connected
(`a` ← `HE1__3WV`, `b` → `N2`, `c` → `3WV__P1`), and so are both ports of `PU1`, `HE1` and `P1`, so I3
does not fire at all. **Six nodes, ten components**, of which the user wrote six — the four
flow components plus the two boundary nodes ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)'s
inference inventory).

**Junction elements — four, and terminals count.** `N2` (three connections: from `N1`, to `PU1`, from
`3WV.a`), `3WV` itself (three ports), and the two terminals `N1` and `N3`, each with one connection and
a stated boundary. Terminals are junction elements for the purpose of invariant 2 and the mass-balance
count: they are vertices of the branch graph, since a branch must end somewhere. Counting only the two
degree-≥3 elements gives `Loops = 4 − 2 + 1 = 3`, which is wrong — this circuit has one loop.

**Branches** — four, each carrying one flow:

| # | From → To | Interior components and nodes |
|---|---|---|
| 1 | N1 → N2 | — (a bare connection: an ideal zero-drop link, `D-25`) |
| 2 | N2 → 3WV.ab | `PU1`, `PU1__HE1`, `HE1`, `HE1__3WV` |
| 3 | 3WV.a → N2 | — (the recirculation branch) |
| 4 | 3WV.b → N3 | `3WV__P1`, `P1` |

The branch graph has four vertices (`N1`, `N2`, `N3`, `3WV`) and four edges, so **one independent
loop**: `N2 → PU1 → HE1 → 3WV → N2`. That loop contains the pump, which is what makes the
recirculation flow non-zero and the whole circuit work; `FS2214` fires if it does not.

**Counting**, with N = 6 nodes, B = 4 branches:

| Unknowns | | Equations | |
|---|---|---|---|
| Branch flows | 4 | Pressure relations: `PU1`, `HE1`, `P1` (one each), `3WV` (two: a→b, a→c), `N1-N2` ideal link (one) | 6 |
| Node pressures | 6 | Mass balance at `N1`, `N2`, `N3` and the `3WV` split — **not** at the three interior nodes | 4 |
| Node enthalpies | 6 | Energy balance, one per node | 6 |
| External mass flux at `N1`, `N3` | 2 | Stated pressures `N1 p=300`, `N3 p=280` | 2 |
| `PU1.head`, promoted by `HE1 out=50` fixing the flow | 1 | `HE1 in=20` | 1 |
| `3WV.position`, promoted by `HE1 in=20` fixing the mix | 1 | `HE1 out=50` | 1 |
| **Total** | **20** | **Total** | **20** ✓ |

`HE1 power=30` and `N1 t=6` add no equations — they supply known coefficients, to the energy balance at
`HE1__3WV` and at `N1` respectively. No datum equation appears, because `N1` states a pressure; no
mass balance is redundant, because the external fluxes are unknown and make their sum a real equation;
and no enthalpy level appears, because the circuit is open and the fluid arrives carrying one.

**Where a component's duty enters, since the table shows no row for it.** There are N energy
equations for N enthalpy unknowns, one per node, and a heat exchanger's `Q̇ = ṁ(h_out − h_in)` is not
an extra row — it is the **duty term inside its outlet node's balance**. The node's upwinded
`h_upstream` for an inflow is the enthalpy at the *upstream component's outlet port*, and a component
that adds heat defines that port enthalpy as its inlet enthalpy plus `Q̇/ṁ`. A pump, a pipe and a
valve define it as a pass-through.

Counting a component energy row *and* a per-node energy balance would over-determine the enthalpy
block by the number of duty-bearing components — the same class of error as writing loop equations on
top of a nodal pressure field. [`22-component-model`](22-component-model.md)'s "the heat exchanger
contributes one equation" means it contributes this term; it does not mean an additional row.

**Two constraints, two promotions, and they pair off exactly.** `in=20` can only be met by moving the
mixing split, so it promotes the valve position; `out=50` with `power=30` fixes the secondary flow,
which only the pump head can deliver. Remove either constraint and both the equation and its unknown
disappear together — which is the check that the counting scheme is the right one.

Solved values are in [`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md): secondary flow
0.2392 kg/s, primary 0.1630 kg/s, recirculation 0.0763 kg/s.

## Acceptance criteria

- [ ] The cooling loop produces exactly the six nodes, four branches and one loop tabulated above,
      and its counting table balances at 20 = 20.
- [ ] The counting check passes for every sample in `samples/`.
- [ ] **The assembled system is as square as the table says it is, sample by sample.** The table
      agreeing with itself is not the property: the simple loop read `Excess = 0` while assembling
      thirteen rows against twelve columns, because its enthalpy level was counted as an unknown no
      layout allocates (`S-24`, `D-75`). Held against `SystemLayout` and `EquationLayout`, not
      against the totals above.
- [ ] The simple loop, whose one enthalpy level is filled by `HE1 in=20`, drops exactly one energy
      balance and assembles twelve rows against twelve columns.
- [ ] A closed loop with no stated pressure produces `FS2201` and solves.
- [ ] The cooling loop's two stated pressures (`N1 p=300`, `N3 p=280`) produce **no** diagnostic —
      they are boundary conditions on an open primary, not competing datums. Nor does the `inlet`
      and `outlet` pair carrying them produce `FS2204`.
- [ ] A closed circuit whose stated duties do not sum to zero produces `FS2203` **and a square count**
      — the check is worthless if the circuit it fires on is one `FS2210` would have caught anyway.
- [ ] The same circuit in a `dynamic` model produces no `FS2203`.
- [ ] The substation's closed secondary produces no `FS2203`, although it holds a coupled exchanger
      whose duty sign the graph cannot read.
- [ ] A circuit with a `inlet` and no `outlet` produces `FS2204`; one with both produces none, and so
      does a closed circuit with neither.
- [ ] A `inlet` states `t` and exactly one of `flow` and `p`; the missing one produces `FS2117` or
      `FS2118` and both together `FS2101`. A `outlet` requires nothing.
- [ ] A closed steady circuit carries exactly one enthalpy level, and one that states no temperature
      anywhere produces `FS2211` naming a temperature rather than a pressure.
- [ ] Two stated pressures on one closed loop with no path between them produce `FS2212`.
- [ ] The counting scheme is exercised by a deliberately over-specified circuit that produces
      `FS2210`, and an under-specified one that produces `FS2211` — a count that cannot fail is not a
      check.
- [ ] `FS2211` names at least one specific candidate for every under-specified sample.
- [ ] Lowering the same semantic model twice yields graphs equal including node ordering.
- [ ] An architecture test asserts `CircuitGraph` references no tier-10 type.
- [ ] A pipe with `nodes=4` produces four internal thermodynamic graph nodes and five hydraulic
      sub-pipes; the four cells each own one quarter of pipe volume, the five sub-pipe lengths sum to
      the declared length, and one `ComponentGroup` contains all nine expanded child ids.
- [x] The **substation** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) lowers to
      two hydraulic components, gets two pressure datums — one stated, one auto-picked with `FS2201` —
      and produces **no** `FS2213`. `WellPosednessTests.TheSubstationHasTwoHydraulicComponentsOneStatedDatumAndOnePicked`,
      `.TheSubstationIsNotReportedAsTwoIsolatedSubgraphs`; the picked datum is `SR__SP` (`D-98`).
- [x] A rated heat exchanger is **not** a junction element, appears in two `Branch.Path`s, and
      contributes no mass balance. A three-way valve, with the same "more than two ports", is a
      junction element — the flow-group test separates them and a port-count test does not.
      `LoweringTests.AThreeWayValveIsAJunctionAndAFourPortExchangerIsNot`,
      `WellPosednessTests.TheCoupledExchangerLiesOnTwoBranchesAndIsNeitherAJunctionNorABalance` (P4.2).
- [x] Removing the exchanger from the substation leaves two genuinely isolated subgraphs and **does**
      produce `FS2213`, so the check still catches what it was written for.
      `WellPosednessTests.RemovingTheExchangerLeavesTwoGenuinelyIsolatedSubgraphs`.
- [ ] The storage header materializes four tank ports, decomposes into four branches meeting at `T1`,
      and assembles one tank mass balance plus three pressure equalities. No hydrostatic term appears.
      Every one of its four boundaries states a flow, so it has no unknown external flux and one of
      its five mass balances is dropped as redundant.

## Open questions

None. `D-25` makes bare connections ideal. `ComponentKindInfo.DrivesFlow` is explicit registry
metadata used by `FS2214`; inspecting residual code or parameter names is forbidden (`D-30`).
