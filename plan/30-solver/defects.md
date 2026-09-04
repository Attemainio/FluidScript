---
id: 30-solver-defects
title: What implementing against the solver tier found
tier: 30-solver
owns: [defect and observation record for documents 31-36]
---

# What implementing against the solver tier found

Defects, deferrals and observations from implementing against `31`–`36`. The rule and its reasoning
are in [`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

**No solver has been built.** What has happened is that four packages in tier 20 — `P3.0` through
`P3.4a` — were written *against* this tier's contracts without implementing any of them: the component
interface waits on `31` for the shape of `SolveContext`, the residual functions implement `36`'s
smoothing constants, and the graph exists to be assembled by `32`. Everything below was found from
that side. **The absence of an entry about `33`, `34` or `35` means nothing has looked**, not that
nothing is wrong.

This file was started late. `P3.0`–`P3.4a` should each have appended to it as they went, and instead
the findings sat in [`20-core-domain/defects.md`](../20-core-domain/defects.md) where the tier that
owns them would not have seen them. The entries below are that backlog, written up in one pass.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| S-1 | [`31`](31-solver-architecture.md) | **`SolveContext` and `UnknownDeclaration` are named as this tier's and defined nowhere** | [`22`](../20-core-domain/22-component-model.md) says in as many words that "`IFlowComponent` waits for tier 30 to fix the shape of `SolveContext` and `UnknownDeclaration`", and `31` defines neither — not a field, not a sketch. `P3.3` could not build a single residual without them, so it defined both in `FluidScript.Core.Components`: a `readonly ref struct` holding the substance, a span of `PortState`, a span of branch flows and a span of the component's own unknowns. Tier 30 must either adopt those shapes or say what it wants instead, and the second is much more expensive after six components implement the first. Recorded from the other side as `C-17`. |
| S-2 | [`31`](31-solver-architecture.md), [`32`](32-steady-state-newton.md) | **The iteration loop must pre-evaluate every port state, and neither document says so** | `EvaluateResiduals` may not call the property backend *at all* — not "as little as possible". The measurement: one water (T,p) property is ~63 µs and a seven-property `FluidState` ~204 µs. A residual runs N+1 times per Newton iteration for a numerical Jacobian, so a 20-unknown circuit with ten components fixing one state each is 21 × 10 × 204 µs ≈ **43 ms per iteration**, which exceeds [`07`](../00-foundation/07-quality-attributes.md)'s whole interactive budget before any linear algebra. `P3.3` closed the component half by putting evaluated properties in `SolveContext.Ports` (`C-16`); what is still open is that the assembler has to *fill* them, once per iterate rather than once per residual, and no document assigns that. |
| S-3 | [`36`](36-numerics-and-convergence.md) | **Residual scaling is specified and unimplementable where it is asserted** | `22`'s invariant 5 says every residual is scaled so its magnitude is comparable across component kinds, and `36` says the system is scaled before solving. `P3.3` could not assert it: scaling is a property of the *assembled* system against a convergence test, and a component evaluated alone has nothing to be comparable to. The invariant was moved here rather than left looking unchecked in tier 20 (`C-2`). It stays open until an assembler exists — and it is the one `36` names as costing a week of "the solver does not converge" when skipped. |
| S-4 | [`36`](36-numerics-and-convergence.md), [`07`](../00-foundation/07-quality-attributes.md) | **A property-backend call can never return, and nothing has a cut-off** | Measured, not theorised: `Water + Ethanol 60/40` flashed at `(h, s)` ran past a 600 s timeout with no output and no progress. `36` covers a *solver* that does not converge and says nothing about a *backend* that does not — and a flash is itself an iteration, inside the residual, inside the Newton step. `D-22`'s stop requirement ("any isolation breach stops the run at its last verified frame") has no mechanism here: a hung flash is not a breach, it is a call that never comes back, and cancellation cannot reach a native frame. The performance harness works around it with a background thread and a five-second join, which is a diagnostic's answer and not a solver's. |
| S-5 | [`36`](36-numerics-and-convergence.md) | **"Continuous in value and first derivative" does not say how to check it, and the obvious check fails on correct code** | The natural test — take finite differences either side of a blend join and assert they are close — is not what C¹ promises. C¹ says the *one-sided derivatives agree at the join*, not that the derivative varies slowly near it, and across the valve's 100 Pa regularisation band the true derivative sweeps from 0 to 3.75 × 10⁷. The test failed on a correct implementation and was rewritten to probe the one-sided derivative at each join with a shrinking step. `36` asks for the property in one line and every implementer will write the wrong test first; the working form belongs in the document. |
| S-7 | [`31`](31-solver-architecture.md) | **The residual-row-to-component mapping is built and consumed by nothing** | `31` makes the point that this mapping exists only at the component layer — "HX1 energy balance off by 4.2 kW" is actionable and "residual[17] = 4200" is not — and that if that layer does not carry it, no later one can recover it. `P3.3` implemented `DeclareEquations`, which names every row with its owner and its residual unit, and a test asserts each component declares exactly as many as it writes. Nothing reads it yet. Recorded because the first assembler is where it gets dropped: the rows are contiguous and the temptation to index them by integer alone is strongest exactly there. |
| S-9 | [`31`](31-solver-architecture.md) | **Nothing consumes the counting table, so its terms are asserted against a document rather than against an assembly** | `WellPosedness.CountingTable` says the cooling loop has 20 unknowns and 20 equations, and that number is checked against `23`'s hand-tabulated table. When `32` assembles the real system, the row and column counts of the Jacobian are the same two numbers computed a second way — and if they disagree, one of the two is wrong with nothing to say which. The assembly should be built to *consume* this table (size its buffers from it, and assert its own totals against it) rather than to re-derive the counts, which is the only way the agreement means anything. |
| S-12 | [`36`](36-numerics-and-convergence.md) | **Per-branch flow scaling is right and the reason `36` gives for it does not hold** | The document says one circuit-wide scale lets the convergence test "declare success while that balance is off by 10 % of the flow through it". With the table's own `newton.residual_tol` of 1e-8 that does not follow: a 10 kg/s primary beside a 0.05 kg/s bypass, one scale of 10, permits an absolute mass error of 1e-7 kg/s — two parts per million of the bypass, not 10 %. The claim needs a tolerance three orders looser than the table gives. Per-branch scaling is still correct for two other reasons, and they are the ones that should be written down: the convergence test stops being a *relative* statement when one scale covers a 200× span, and the scaled Jacobian's bypass columns come out 200× smaller than the primary's, which is a conditioning problem in the linear solve and makes `newton.step_tol` meaningless on the small branch. Implemented per branch; the justification in `36` still needs correcting. |
| S-13 | [`36`](36-numerics-and-convergence.md) | **A mass balance belongs to a node and `36` scales mass flow per branch, so nothing says which scale a node's mass row takes** | The residual is a sum of the flows incident on the node, so its natural magnitude is set by the largest of them; scaling by the smallest would inflate a residual that can never get that small and the row would never converge. `P3.6a` takes max-of-incident-branches and says so, because the document does not. It matters exactly where per-branch scaling was introduced to help — a node joining a 10 kg/s primary to a 0.05 kg/s bypass — so the two rules interact on the one topology they were both written for. |
| S-17 | [`33`](33-transient-time-domain.md) | **Quasi-static pressure is justified by the wrong quantity, and the right one nearly fails for air** | `33` argues the split from acoustic transit: 1500 m/s in water, milliseconds across a plant. Sound speed in air is **343 m/s** — only 4.4× slower — so a 50 m duct is 146 ms and transit time would clear air comfortably. It is not the criterion. What decides whether pressure can be treated as instantaneous is **pneumatic capacitance**: from `dp = a²dρ`, `C = V/a²` and `τ = (V/a²)(2Δp/ṁ)` across a square-law resistance. A 1 m³ hydronic loop at 200 kPa and 5 kg/s gives **0.036 s** and the conclusion holds by one to two orders; a 100 m³ AHU and duct system gives **0.34 s** at design flow and **1.7 s** throttled to 0.5 kg/s, which is the same order as the timestep and the frame interval. The physical point is not that pressure signals travel slower in air — it is that **air stores mass and water essentially does not**. The split is still right for v1; the argument for it is not, and the air-side limit is currently unstated and unguarded. |
| S-18 | [`33`](33-transient-time-domain.md), [`12`](../10-language/12-grammar.md) | **A schedule has no ambient to drive, so an outdoor-temperature scenario cannot be written at all** | `schedule` sets `component.parameter`, and there is no ambient temperature in the model to be a target. `33` also states that v1 has no ambient loss and no wall conduction, so even given one, no component would read it. The only path an outdoor condition has into a model today is a boundary node's stated temperature — which then transports downstream correctly, but is a district-supply scenario rather than a weather one. The gap is small and structural rather than deep: an ambient scalar the schedule can reach, plus `−UA(T̄ − T_amb)` contributed by the pipe. That term already has its home — it is `D-69`'s flux member, which is on `IFlowComponent` rather than on the exchanger for exactly this reason — so the component half costs nothing. Recorded because "outdoor temperature drops and the front reaches the air handling unit late" is the first transient a user will try to write, and nothing today says why they cannot. |
| S-19 | [`33`](33-transient-time-domain.md) | **The `nodes=` guidance inverts on the air side, and the CFL limit gets 24× tighter for a shorter delay** | The step limit is the residence time `Vρ/ṁ`, and air is ~800× less dense than water. `33`'s DN20 water cell — 0.0925 l, 988 kg/m³, 0.0763 kg/s — gives τ = 1.20 s and a step of 1.08 s. A Ø315 duct cell of the same 0.25 m length holds 19.5 l at 1.2 kg/m³ and 0.467 kg/s: τ = **0.050 s**, a step of **0.045 s**. Same `nodes=`, **24× more steps** — roughly 13 000 over a 600 s horizon against 1 400, and two Newton solves each. The payoff moves the other way: a front crosses 8 m of duct at 5 m/s in **1.6 s**, so duct transport is barely visible while the air handling unit's thermal mass, which v1 does not model, dominates the response. So the document's advice — that `nodes=` is the single most important modelling decision in a transient run — is true on the water side and close to backwards on the air side, and `FS3101` will fire on every air-side model. |
| S-16 | [`31`](31-solver-architecture.md), [`22`](../20-core-domain/22-component-model.md) | **`DeclareUnknowns` is on the component interface and consumed by nothing, so a control volume's own state is modelled nowhere** | `IFlowComponent.DeclareUnknowns()` has existed since `P3.3` and neither `SystemLayout` nor `CountingTable` calls it. Exactly one kind uses it: the tank declares one enthalpy unknown (`T1.h`) and one energy balance, and `EnergyBalances` is `Nodes.Length`, so the table counts **neither** of them. The two cancel, `Excess` reads zero, and `m4-storage-header` looks square while the system it would assemble is a row and a column short of the real one. That cancellation is why four packages have not noticed. It needs a decision rather than a fix — whether a control volume owns an unknown and a row the table must model, or whether `D-69`'s treatment of energy as a flux applies to a tank too — and until it is taken, `RowAllowance.ControlVolumeRows` subtracts it in one place and every reconciliation names it. |
| S-14b | [`22`](../20-core-domain/22-component-model.md) | **A coupled heat exchanger has no side-2 pressure relation** | On `m2-substation` two branches cross `HX1`, so `Relations()` gives it two pressure relations, and `HeatExchanger` declares one — `side-1 drop`. Side 2 has no momentum equation at all. Here too the table is right and the component is incomplete: a real second stream has a real pressure drop. Already tracked from the component side as `C-4` (mode selection unimplemented) and belongs to `P4.1`, which builds the rated and coupled models; recorded here because it is the second of the two things that stopped the equation rows reconciling, and because the substation cannot assemble a square system until it lands. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| S-15 | [`31`](31-solver-architecture.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **The counting table counted the ideal zero-drop links and could not say which nodes they joined** | `PressureRelations` aggregates three things, and two of them are some component's own equation reaching the assembler through `DeclareEquations`. The third is not: `D-25` makes a bare `A - B` between two nodes an ideal link, there is nothing in the branch's path to declare `p_A = p_B`, and the assembler has to write that row itself. A count cannot say between which nodes, so the equation layout would have had to walk the branches a second time — the drift the table's own `FluxNodes` note warns about, one term further down. | `WellPosedness.Relations` now collects the pairs while it counts them, and `CountingTable.IdealLinks` names them beside `FluxNodes`, `PressureNodes`, `DatumComponents` and `LevelComponents`. `EquationLayout` writes one row per link from that array, and `EquationRowReconciliationTests` deleted its own walk in favour of `counting.IdealLinks.Length` — which is the point: the second implementation existed, in a test, and naming the links removed it. |
| S-14a | [`22`](../20-core-domain/22-component-model.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **A three-way valve wired as a two-way still wrote a Kv law for the port nothing was connected to** | The registry has always made port `c` optional and the page has always said leaving it open is how a two-way valve is written; `ThreeWayValve` declared three equations regardless, and its residual read `Ports[2]` for a port with no node. `m2-distribution-header` and `m1-syntax-tour` hold two each, so both reported square while the system they would assemble was over-specified by two — invisible, because nothing assembled yet. Lowering now counts every component's connections and the qualified port names, and the factory hands the valve its wiring: with the bypass open it is a pass-through with two ports, one flow group, one Kv law and no mass balance of its own, and its mode reads `two_way`. The count is decided by topology and never by a solved value, exactly as an exchanger's mode is. `EquationRowReconciliationTests` now holds the declared pressure rows against `Relations()` on every sample, so the next kind that under-declares fails a test rather than a milestone. |
| S-11 | [`22`](../20-core-domain/22-component-model.md), [`23`](../20-core-domain/23-topology-and-graph.md), [`31`](31-solver-architecture.md) | **The rows the components declared and the rows the counting table counted disagreed, by exactly one per heat exchanger** | Found by `P3.6a` doing what `S-9` asks — comparing the two counts before writing an assembler that would have had to pick one. `WellPosedness.Relations` counts a crossed component once as a *pressure* relation and `EnergyBalances` is `Nodes.Length` unconditionally, so nothing counted the exchanger's duty row, and the excess equalled the number of non-node components declaring an energy row on every sample. Not a miscount but a contradiction about ownership. `D-69` settles it: energy is a flux a component contributes, the counting table is unchanged, and the excess disappears because the exchanger stops owning a row. Recorded from the component side as `C-40`. |
| S-6 | [`36`](36-numerics-and-convergence.md) | **The smoothing constants were `36`'s numbers living in tier 20's code, with no shared source** | `valve.dp_regularization` and `upwind.smoothing_band` had been transcribed by hand into two unrelated component files, and a change to the table reached neither. `P3.6a` made the table itself a type — `Solvers.Tolerances`, all twenty-five rows — and `ValveLaw.RegularizationDrop`, `Smoothing.UpwindBand` and `Quantity.DefaultRelativeTolerance` now read from it. The direction is the point: the table is the source and a component is one of its consumers, so a component reaches into `Core.Solvers` rather than the reverse. `ToleranceTableTests` parses `36`'s own markdown and asserts **both** directions — every constant carries its documented value, and every documented row is implemented — because the failure that happened was the second kind: a value transcribed correctly, and then the document moved on without it. |
| S-10 | [`23`](../20-core-domain/23-topology-and-graph.md), [`31`](31-solver-architecture.md) | **`CircuitGraph` did not carry the port-level adjacency the assembler needs** | Lowering computed `_peerElement`/`_peerPort` to decompose the branches at all, then discarded them. `Branch.Path` records the order a walk crossed the elements and not *which port* faced which — and a two-port pass-through walked from the other end is entered at its outlet, so the direction is not recoverable from the order. A component's residual reads `Ports[i]` as the state at the node port `i` touches, so no `SolveContext` can be built without it. `P3.6a` publishes it as `CircuitGraph.Adjacency`, and a test walks every branch through the table and reproduces `Decompose`'s own `Path` exactly. Recorded rather than done quietly because it is a tier-20 type changed from a tier-30 package. |
| S-8 | [`36`](36-numerics-and-convergence.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **`FS2211` (under-specified) appeared to be unreachable from any script**, so half of the well-posedness count was untested against real input | It was reachable, and the reason nothing reached it was a missing row rather than a dead code path: the count had no **enthalpy datum** (`C-30`, `D-65`). A closed, steady circuit coupled to nothing has one enthalpy its own relations cannot determine, and a script that states no temperature anywhere in such a circuit is genuinely under-specified by exactly one. `P3.4c` counts it, and `Understated` now names a temperature ahead of a pressure — the one candidate the graph could not have picked for itself, since a datum covers the pressure case before it can reach this code. The original entry guessed at the two possibilities correctly and picked neither: the equation set *was* missing a row, and `FS2211` is not dead. |

## Observations

**The two counts had to be compared before either could be trusted, and comparing them is what found
`S-11`.** `S-9` asks the assembler to *consume* the counting table rather than re-derive it, and the
cheapest possible version of that — summing `DeclareEquations` over the graph and holding it against
`CountingTable.Equations` on every sample — cost one throwaway test and found a contradiction that had
survived four packages. Neither number was checkable alone: the table matched `23`'s hand-tabulated
worked example exactly, and the declarations matched `22`'s component list exactly. It is the
disagreement that carries the information, which is the whole argument for building the assembler
against the table instead of beside it.

**The counting table's own note said what to do, one term before it was needed.** `FluxNodes` carries a
paragraph explaining why it is *named* rather than counted — "an assembler has to declare these unknowns
and a count cannot say which nodes get one" — and `S-15` is the identical argument one row down, on the
equation side, found only when an assembler actually tried to write the row. Four terms had already been
converted from counts to named arrays for exactly this reason; the fifth was missed because nothing had
yet needed it. The lesson is cheap to state and was not: **a table meant to be consumed should name every
term whose consumer cannot re-derive it**, and the test of that is not whether a count is enough for the
diagnostic, but whether the assembler can write the row from it.

**A count that is short on both sides is worse than a count that is wrong.** `S-16` is the tank
declaring an unknown and a row the table models on neither side. `Excess` reads zero, `FS2210` and
`FS2211` stay quiet, and every reconciliation that compares two totals agrees — because both totals are
short by one. The reconciliation this session added is what caught it, and only because it holds the
*assembled row count* against the table rather than holding the table against itself: an error that
cancels inside one number is invisible until something outside that number depends on it.

**A defect found this way names both sides and neither.** `S-11` was recorded against `22`, `23` and
`31` together, because the exchanger's row and the node's row are each defensible in isolation and only
the pair is wrong. A finding filed against one document would have been fixed there, and the fix would
have been the direction-dependent one that does not survive a flow reversal.


**The nodal formulation's consequences are now structural, not just stated.** `23` argues that loop
closure is a property of the unknowns rather than an equation, and `P3.4a` built the cycle basis as
data with no equation attached: `CircuitLoop` is read by layout and by `FS2214`, and by nothing in the
system. The failure mode `23` warns about — 21 equations against 20 unknowns on this tree's own
reference circuit — is now not reachable by accident, because there is no code path from a loop to a
row.

**Branch-owned flow is implemented, and the unknown count follows from it.** A branch with three pipes
and a valve in series is one flow unknown and four pressure relations, not four flows and three
identities. `23` calls this the most consequential structural decision in tier 20 for solver
performance; it is now a fact about the graph rather than an intention, and `32` assembles against it.

**Node ordering is part of the contract, not an implementation detail.** `23`'s invariant 6 ties the
renderer's placement memory *and* the solver's variable ordering to lowering's deterministic order, and
`P3.4a` asserts it by lowering the same model twice and comparing a canonical rendering. A solver that
re-sorts variables for its own reasons — by degree, for fill-in — breaks the renderer, which is not an
obvious coupling from inside tier 30.

**The energy block spans what the pressure block does not.** A rated exchanger produces more than one
hydraulic connected component in one graph, and the energy system runs over every node in the model.
`23` states the consequence for tier 30 plainly and it is worth repeating where a solver author will
read it: the hydraulic blocks are genuinely independent and could be factorised separately, the energy
block is not, and a segregated solver that split by circuit would iterate against a stale duty. Any
block-decomposition experiment in `36` has to preserve that coupling.

**A residual that returns `NaN` at zero flow poisons the whole Newton step, and the laminar branch is
where it comes from.** `f = 64/Re` diverges as velocity goes to zero while the term it multiplies goes
to zero with it — literally `∞ × 0`. Substituting Re gives `32·μ·L·v/D²`, which is linear in velocity,
exactly zero at rest and has a finite derivative there. `36`'s "regularised where the physics is not"
covers this in spirit; it does not name the case, and zero flow is the initial guess.

**Every residual function in `P3.3` is allocation-free and was asserted so in the package that wrote
them.** `08` said to write that test with the components rather than retrofit it across six types, and
that was right for a reason worth keeping: the first version of the test measured 21 600 bytes, all of
it the test's own collection expressions inside the measured region. Retrofitted later, that number
would have been read as a real allocation in the component.

**The energy block's rank deficiency is tier 30's to respect, and no synthetic row fixes it.** A
closed, steady, uncoupled circuit's energy equations determine every enthalpy *difference* and no
enthalpy, so the assembled Jacobian is singular by one unless the script states a temperature
(`D-65`). `32`'s singular-handling list now names it beside the missing pressure datum, and the two
are not symmetric in what the solver may do about it: a pressure datum can be invented because every
pressure is relative, and a temperature cannot be, because every property call reads the absolute
value. An assembler that "helpfully" pins an enthalpy would return a plausible answer to a question
the user never asked.

**A worked example is a regression test for the case it was written from.** `23`'s counting table
reproduced term for term on the first run, 20 = 20 with both promotions, which read as strong evidence
that the counting pass was right. It was evidence about one open circuit. Both of the count's real
defects — the stated-`flow` flux (`C-26`) and the missing enthalpy level (`C-30`) — are invisible on
that circuit and were found by running the whole sample corpus and by a user asking for a *different*
circuit to be fixed. The sweep over every sample, recorded as a dictionary of outcomes rather than as
a list of exceptions, is what makes the next one visible.

**Well-posedness runs on the graph alone, and that is what makes it testable.** Nothing in the pass
reaches back into the semantic model; it reads `IComponent.StatedParameters`, which lowering now fills
from the bound symbol. So a solver test can build a graph by hand, ask whether it is square, and get
the same answer the pipeline would — and `23`'s invariant 7 stays true without an exemption.

**Stated-ness is the whole input, and it was being thrown away.** `IComponent.StatedParameters`,
`SizedParameters` and `DefaultParameters` have existed since `P3.3` and were empty on every component
lowering built. Nothing downstream can recover the distinction from the value: a stated `position=1`
and the registry's own default are the same number and mean opposite things to the count. Filling them
was the precondition for promotion existing at all, and it is `D-02` made observable rather than a
convenience.

**A dropped component leaves a branch ending nowhere, and the cycle basis threw on it.** A pipe whose
bore no catalogue resolves is not built and its connections go with it, so `Decompose`'s "the branch
ends where the graph does" path produces an end that is not a vertex — and `CycleBasis` indexed it
directly. `KeyNotFoundException` on a script that is merely incomplete, which no stage may do. Found by
a test written to reach `FS2211`, which is the second time this package a test written for one reason
found something else.
