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
/// <c>3WV.a</c>, <c>3WV.b</c> and <c>3WV.c</c>, which no node type can name. Typing both ends as
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

Each component declares how its ports partition into sets that must carry the same flow. This is the
test everything structural is built on, and port count alone is not it.

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

1. **Instantiate components.** Each `ComponentSymbol` becomes an `IComponent` via the registry, with
   its stated parameters converted to SI.
   Indexed tank ports have already been materialized by the binder; lowering maps each normalized
   elevation to exactly one bottom-to-top layer and does not create ports absent from source (`D-32`).
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
| **Pressure boundary** | `p` on a node | That node's pressure, plus an unknown external flux |
| **Temperature boundary** | `t` on a node, or a heat exchanger's `in`/`out` | The energy datum |
| **Flow boundary** | `flow` on a node | A known injection or extraction |

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
symbol-table lookup with no qualification: `supply N3` finds the one `N3` there is. Had names been
scoped per circuit, each of the four attachment lines in the distribution header would have needed a
qualified form the language does not have — which is the argument that decided `D-41`.

**A subcircuit's attachment lowers to ordinary connections.** `supply N3` in circuit 101 becomes a
connection from the parent's `N3` to 101's first unconnected inlet, and `return N5` a connection from
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
also a solution. Rather than erroring, the graph **picks one and says so** (`FS2201`, info): the node
with the most connections, ties broken by declaration order, so the choice is deterministic and stable
across edits.

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
| Open port on a valve's bypass (`c`) | **Dead leg**: zero flow | A three-way valve used as a two-way. Zero flow is the physical truth. |
| Open port on any other component | Zero flow, plus `FS2202` (warning) | Almost certainly an unfinished script |
| A node with exactly one connection and no stated boundary | Zero flow, plus `FS2107` | Same |

Zero flow everywhere is the conservative choice: it changes no other result and it makes the graph
solvable, so the user sees a diagram with a visibly dangling stub rather than an error message.

## Well-posedness

Checked before the solver is invoked, because every one of these produces a much better message here
than in the linear algebra.

| Check | Failure | Code |
|---|---|---|
| Equation count equals unknown count | Over- or under-determined | `FS2210` / `FS2211` |
| A pressure datum exists per connected component | None stated → auto-picked (info) | `FS2201` |
| Two stated pressures in one loop with no through-flow path between them | A second, contradictory datum | `FS2212` |
| Every branch is reachable from the pressure datum | Isolated subgraph | `FS2213` |
| No node has exactly one connection without a boundary condition | Dead end | `FS2107` |
| Every loop contains at least one flow-driving component | A passive loop can only have zero flow | `FS2214` (**warning**) |
| Substance is resolvable and every state is inside its valid range at the initial guess | | `FS2215` |

### The counting argument

**A count that always balances is not a check.** The obvious version — B branch flows plus N node
pressures plus N node enthalpies against N mass balances, B pressure drops, N energy balances and a
datum, less one redundant mass balance — comes to 2N + B on both sides *for every possible graph*. It
can never detect an over- or under-specified circuit, so it cannot be what `FS2210` and `FS2211` are
raised from. The real count has to include the things a user actually varies: boundary conditions, and
the parameters a stated constraint promotes into unknowns.

**Vocabulary.** A **junction element** is either a terminal or a component with at least one flow
group containing three or more ports. Port count alone is never the test: a four-port coupled
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
| External mass flux, one per node with a stated `p` | X<sub>p</sub> |
| Sized parameters promoted to unknowns (below) | P |
| **Total** | 2N + B + X<sub>p</sub> + P |

| Equations | Count |
|---|---|
| Pressure relation, one per 2-port component; k−1 per k-port component; one per bare ideal link | C |
| Mass balance, one per junction element and terminal (**not** per interior node) | M |
| Energy balance, one per node | N |
| Stated pressure boundaries | X<sub>p</sub> |
| Stated `flow` boundaries | X<sub>f</sub> |
| Component constraints beyond the component's own governing equation | K |
| Pressure datum, **only when no `p` is stated** in that connected component | D |
| **Total** | C + M + N + X<sub>p</sub> + X<sub>f</sub> + K + D |

**The mass-balance redundancy and the datum are the same equation seen twice.** In a fully closed
circuit with no external flux, summing every mass balance gives 0 = 0, so one is redundant and the
auto-picked datum replaces it. In an open circuit the same sum gives Σ ṁ_ext = 0, which is a real
equation in real unknowns, so nothing is redundant — and a stated pressure has already supplied the
datum. Exactly one of the two mechanisms applies, never both and never neither.

**The implementation must drop the redundant mass balance explicitly** in the closed case, not rely on
the linear solver to cope. A singular-by-construction Jacobian handed to a factorisation is undefined
behaviour dressed as an algorithm.

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

**Promotion is what `FS2210`/`FS2211` measure against.** A constraint with nothing to promote is an
over-specification (`FS2210`); a free sized parameter with no constraint to pin it is left to sizing,
not to the solver, and only becomes `FS2211` when nothing determines it at all.

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

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS2201` | No pressure stated anywhere in a connected component | Info | `Using '{node}' as the pressure datum. Pressures are relative to it.` |
| `FS2202` | Open port terminated | Warning | `'{component}' port '{port}' is not connected; treating it as closed.` |
| `FS2210` | More equations than unknowns | Error | `This circuit is over-specified by {n}. Remove one of: {list}.` |
| `FS2211` | Fewer equations than unknowns | Error | `This circuit is under-specified by {n}. Add one of: {list}.` |
| `FS2212` | Two stated pressures in one loop with no flow path between them | Error | `'{a}' and '{b}' both set a pressure on the same closed loop, with no path between them for flow to take. Remove one, or connect them.` |
| `FS2213` | Isolated subgraph | Error | `'{list}' are not connected to the rest of the circuit.` |
| `FS2214` | Loop with no flow driver | Warning | `Nothing drives flow around {loop}; it will carry none. Is a pump on the wrong leg?` |
| `FS2215` | Initial state outside the substance's range | Error | `{substance} cannot be at {state}.` |
| `FS2216` | A two-sided component's owning circuit could not be determined from enthalpy | Info | `'{component}' touches {a} and {b} with no clear heat direction; tagging it into {chosen}.` |
| `FS2217` | A subcircuit's attachment endpoint resolves to its own circuit | Error | `'{circuit}' attaches to '{node}', which is one of its own components. A subcircuit attaches to another circuit.` |

**`FS2214` is a warning, not info.** A loop with no driver is almost always a mis-placed pump — the
mistake the cooling loop's own history records ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) —
and its consequence is silent: the loop simply carries no flow, and every temperature downstream of it
is wrong in a way that still looks like a solved circuit. It was info; that was too quiet.

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

N1 node t=6 p=300
N3 node p=280
```

**Nodes**: `N1` and `N3` are declared (they carry boundary conditions), `N2` comes from I1, and
`PU1__HE1`, `HE1__3WV`, `3WV__P1` from I2 — every pair of directly-connected non-node components gets
one. `3WV`'s three ports are all connected
(`a` ← `HE1__3WV`, `b` → `N2`, `c` → `3WV__P1`), and so are both ports of `PU1`, `HE1` and `P1`, so I3
does not fire at all. **Six nodes, ten components**, of which the user wrote six — the four
flow components plus the two boundary nodes ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)'s
inference inventory).

**Junction elements — four, and terminals count.** `N2` (three connections: from `N1`, to `PU1`, from
`3WV.b`), `3WV` itself (three ports), and the two terminals `N1` and `N3`, each with one connection and
a stated boundary. Terminals are junction elements for the purpose of invariant 2 and the mass-balance
count: they are vertices of the branch graph, since a branch must end somewhere. Counting only the two
degree-≥3 elements gives `Loops = 4 − 2 + 1 = 3`, which is wrong — this circuit has one loop.

**Branches** — four, each carrying one flow:

| # | From → To | Interior components and nodes |
|---|---|---|
| 1 | N1 → N2 | — (a bare connection: an ideal zero-drop link, `D-25`) |
| 2 | N2 → 3WV.a | `PU1`, `PU1__HE1`, `HE1`, `HE1__3WV` |
| 3 | 3WV.b → N2 | — (the recirculation branch) |
| 4 | 3WV.c → N3 | `3WV__P1`, `P1` |

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
`HE1__3WV` and at `N1` respectively. No datum equation appears, because `N1` states a pressure; and no
mass balance is redundant, because the external fluxes make their sum a real equation.

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
0.2394 kg/s, primary 0.1629 kg/s, recirculation 0.0764 kg/s.

## Acceptance criteria

- [ ] The cooling loop produces exactly the six nodes, four branches and one loop tabulated above,
      and its counting table balances at 20 = 20.
- [ ] The counting check passes for every sample in `samples/`.
- [ ] A closed loop with no stated pressure produces `FS2201` and solves.
- [ ] The cooling loop's two stated pressures (`N1 p=300`, `N3 p=280`) produce **no** diagnostic —
      they are boundary conditions on an open primary, not competing datums.
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
- [ ] The **substation** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) lowers to
      two hydraulic components, gets two pressure datums — one stated, one auto-picked with `FS2201` —
      and produces **no** `FS2213`.
- [ ] A rated heat exchanger is **not** a junction element, appears in two `Branch.Path`s, and
      contributes no mass balance. A three-way valve, with the same "more than two ports", is a
      junction element — the flow-group test separates them and a port-count test does not.
- [ ] Removing the exchanger from the substation leaves two genuinely isolated subgraphs and **does**
      produce `FS2213`, so the check still catches what it was written for.
- [ ] The storage header materializes four tank ports, decomposes into four branches meeting at `T1`,
      and assembles one tank mass balance plus three pressure equalities. No hydrostatic term appears.

## Open questions

None. `D-25` makes bare connections ideal. `ComponentKindInfo.DrivesFlow` is explicit registry
metadata used by `FS2214`; inspecting residual code or parameter names is forbidden (`D-30`).
