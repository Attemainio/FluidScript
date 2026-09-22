---
id: 24-auto-sizing
title: Auto-sizing
tier: 20-core-domain
status: draft
owns: [sizing pipeline, per-component sizing rules, parallel-branch balancing, constraint propagation, default catalogue, sizing diagnostics]
depends_on: [22-component-model, 23-topology-and-graph, 27-component-catalog]
traces_to: [R-02, R-35, R-43, R-45]
open_questions: 0
last_review_pass: 0
---

# Auto-sizing

## Purpose

`D-02` made every parameter optional; this document is what fills the gaps. It is the subsystem that
makes `PU1 pump` — three characters and a keyword — into a specified machine, and it is the one most
likely to be quietly wrong, because a sized value always *looks* reasonable. The mitigation is not
better rules; it is that every sized value carries its basis, and the UI shows it.

## Responsibilities

**Owns.** The sizing pipeline, per-component sizing rules, constraint propagation from stated values,
the default catalogue, and sizing diagnostics.

**Explicitly does not own.** The catalogues it selects from and their provenance
([`27-component-catalog`](27-component-catalog.md)), component equations
([`22-component-model`](22-component-model.md)), the
solve (tier 30), optimization-based sizing
([`35-evolutionary-sizing`](../30-solver/35-evolutionary-sizing.md) — a different problem, run when a
user asks, not on every compile).

## Sizing is not optimization

Auto-sizing applies **deterministic engineering rules** to reach a defensible design in one pass. It
does not search. This distinction matters because the two are constantly conflated:

| | Auto-sizing (this document) | Evolutionary sizing (`35`) |
|---|---|---|
| Runs | Every compile, inside `07`'s draft-compile budget | On request, seconds to minutes |
| Method | Rules and lookup tables | Population search over a fitness function |
| Answer | Deterministic, reproducible | Stochastic, near-optimal |
| Question | "What size is standard practice here?" | "What sizes minimise cost subject to constraints?" |

## The pipeline

Sizing runs **between** graph construction and solving, and iterates with the solve, because most
sizing rules need flows and most flows need sizes.

```
1. Seed        Estimate flows from stated duties. A heat exchanger with power and ΔT
               implies its flow directly — no circuit solution needed.
2. Propagate   Push stated constraints along branches. Every component in a branch
               shares its flow, so one stated flow sizes the whole branch.
3. Size        Apply each component's rule using the current flow estimate.
4. Solve       Steady-state solve with the sized values (tier 30).
5. Re-size     Re-apply rules with solved flows. Components whose size changed by more
               than the tolerance mark the loop dirty.
6. Repeat      From 4, until clean or the iteration cap is hit (FS2301).
```

**A sized value is applied by lowering again, not by setting anything.** A pipe's bore is a
constructor argument, so the pass that uses a new diameter is the pass that built a new `Pipe`; the
loop carries a `SizingOverlay` — component name to parameter to value — and hands it to the component
factory, which puts it in `SizedParameters` and nowhere else. Three consequences worth stating.
A stated value and a chosen one stay distinguishable, which `D-02` depends on and which merging into
`StatedParameters` would destroy. A solve stays a pure function of its graph
([`31`](../30-solver/31-solver-architecture.md)'s invariant 6), because no component is ever mutated.
And **a parameter claimed by no map is promotable**, so the set of parameters the loop intends to size
is fixed before the first solve and only the values move afterwards — one that became sized *between*
passes would stop being promotable between passes, and the system's shape would change under a warm
start.

**The first lowering is a bootstrap.** A pipe with no diameter has no bore and is not built, so a
graph has to exist before flows can be estimated on it; each rule offers a provisional value for that
and nothing else. Flow estimates come from stated duties and stated flows rather than from
resistances, so the flow estimates do not depend on what it was built with. The rules that read a
loop's resistance do, and the bootstrap has none where an exchanger states no flow and no duty: such a
coil is built ideal until a rule gives it a design point (`ComponentFactory`). So the rules are applied
to the bootstrap **twice**, the second time on a graph lowered from the first application's sizes: one
application sized a pump no constraint had claimed to zero head, "no modelled resistance", on a loop
whose coil drops 20 kPa, and the first solve ran on a graph with no answer near its seed (`S-68`).
Measured on the corpus, the second application costs milliseconds and takes the simple loop from three
passes to two. **A provisional decides nothing** (`D-96`): the overlay flags it, the graph carries the flag, and counting treats the
parameter as free — so a constraint the loop cannot otherwise absorb may promote it, in which case the
solver determines it, the rule that would have sized it is skipped whole, and the flag stays. A rule's
choice written over a provisional clears the flag. Until `D-96` the provisional sat in the sized map
unflagged and counted as decided, which is why `23`'s balancing-valve promotion never fired (`C-75`).

**This is the same outer fixed-point loop as deferred expressions**
([`14-expressions-and-references`](../10-language/14-expressions-and-references.md)). They must be one
loop, not two nested ones: a deferred expression can feed a sizing input, and a sized value can feed a
deferred expression. Two loops would interleave unpredictably and could oscillate against each other.
[`31-solver-architecture`](../30-solver/31-solver-architecture.md) owns where that single loop lives.

**Step 1 is what makes convergence fast.** Seeding from stated duties rather than from zero puts the
first iterate close to the answer, and where the duty fully determines the flow it *is* the answer —
the worked example needs two passes for that reason, one to size and one to confirm.

**Every pass of this pipeline runs at one operating case** — and, once `D-143` lands, once per
declared scenario in turn, each a full run of the pipeline and each component's size the envelope of
its kind over them (*Sizing over scenarios*, below). Within one case `design` supplies each driver a
value, every curve short-circuits to a constant against it (`D-58`), and the sizes that come out hold
for the whole run. In a
static solve the design point is also the operating point and the distinction is invisible. In a
dynamic one it is not: sizing is **not** re-run per time step and it is not run at t = 0 either, which
would size the plant for whatever the weather is at midnight on the first of January. **No rule here
reads `ProjectSettings.Design` directly, and `P3.8` measured that none needs to**: the binder folds
every curve to its design-point value before lowering, so a `power=heating` under `design tout=-26`
reaches these rules as a stated 50 kW and is sized against exactly as a typed one. A component that
should *not* be sized at the peak — the heat pump of a bivalent pair — says so with `sized_at tout=-5`
on its own declaration (`D-94`), reads the curve there, and the closed-circuit closure in step 1
gives its backup the remainder. The fraction of peak that results is reported as the parameter's
basis, never taken as an input (`C-51`, closed).

## Sizing over scenarios

`D-143`, the user's call on 2026-09-22, superseding `D-138`'s driver sweep; **not built** — it is
`08`'s P6.8.

**Duties are inputs, not functions of a driver.** A plant's loads are set by occupancy, lighting,
equipment and the building's structure, which is a building-energy problem `01` puts outside this
tool. A plant model takes demands and setpoints as given, so it must never derive a duty from a
driver nobody wrote, and the cases a plant is sized for are the cases an engineer names.

### Scenarios

```fluidscript
scenarios winter summer
design winter

HX1 heat_exchanger power = [30, 10]
N3  node t = [30, 40]
PU1 pump                                # no array: one pump serving both
```

- `scenarios` declares the set once, with the other whole-file lines. The names are the vocabulary
  every basis string, report and UI switcher uses.
- An array states one value per scenario, positionally against that declaration.
- **A scalar is not a short array.** It is the same value in every scenario.
- **Any array whose length is not the declared count is an error**, naming the parameter, its length
  and the count. Nothing is padded (`D-143`; `D-60`'s rule against inferring from data).
- `design <name>` names the **operating** scenario and sizes nothing: the state the canvas draws,
  the numbers a static export carries, the inputs a run starts from.

### The pipeline, and why it is four steps

1. **Solve each scenario, sizing freely.** N static solves, N sets of candidate sizes.
2. **Merge parameter by parameter**, under the kind's rule below — never by adopting one scenario's
   component wholesale, because the pump a plant needs is the one whose curve covers every
   scenario's duty point and that can be no single scenario's pump.
3. **Re-solve every scenario against the merged sizes, frozen.** Not optional and not only a check:
   after merging, the sizes exceed what any single solve used, so none of step 1's equilibria is a
   state of the merged plant — a scenario solved with a DN20 pipe does not describe a plant that
   ended up with DN32. This step produces the operating states and catches a component short
   somewhere; if one is, merge again and repeat.
4. **Draw the merged plant**, with `design`'s scenario supplying the numbers on it.

Step 3's loop should terminate because sizes grow under a maximum and the catalogue is finite, and
the outer loop already caps sizing passes; **that is to be measured, not asserted.** A valve whose
Kv moves with a pipe that moves with a pump is the shape that could cycle.

**The envelope is per kind, and it is not a plain maximum.**

| Kind | Envelope | Also checked at |
|---|---|---|
| Pipe | Maximum flow | — |
| Heat exchanger | Maximum UA | — |
| Pump | The case demanding the largest head at its flow; the curve must cover every other case | Every scenario: on the curve |
| Control valve | Kv from the maximum-flow case | The **minimum-flow** case: authority and turn-down |
| Tank | Not sized here; a profile (`C-115`) | — |

A plain maximum on a valve gives one that sits 15 % open in the light case and hunts. The
rangeability figures a turn-down check needs are looked up and cited here when the package lands;
they are not this project's to derive. Load-case and load-combination conventions are to be looked
up too: structural engineering has settled vocabulary for exactly this idea and it is worth
borrowing rather than reasoning out.

The basis of every size names its governing scenario — "sized at winter, 50 kW" — and a component
that is off in one scenario but on in another is sized where it is on, which closes the case `S-56`
left.

### What this gives up

A discrete list only checks what someone thought to write. With heating 50 kW at the cold end,
cooling 40 kW at the warm end and a recovery exchanger taking the lesser of the two, a file stating
only `winter` and `summer` sizes that exchanger at **zero in both** — it does not come out small, it
disappears:

| Case | Heating | Cooling | Recovery `min(Q_heat, Q_cool)` |
|---|---|---|---|
| `winter` | 50 kW | 0 | 0 |
| A case between them | 5 kW | 5 kW | **5.0 kW** — the exchanger's real governing point |
| `summer` | 0 | 40 kW | 0 |

Two mitigations, neither settled, both P6.8's to decide with measurement: a driver range kept as
**sugar that generates scenarios into the same list**, so one candidate list has two spellings that
cannot disagree; and a diagnostic on the detectable pattern — a sized quantity at zero or a bound in
every scenario while the inputs feeding it are active in different ones.

The flow trap `D-138` recorded outlives it and is worth stating in its new form: a chilled side at
7/12 °C carries 1.91 kg/s for 40 kW where a heating side at 45/35 °C carries 1.20 kg/s for 50 kW.
The smaller duty has the larger flow, so a pipe or pump merged from the heating case alone is 60 %
short in the cooling one. This is why the merge is per parameter and per kind rather than per
component.

### How the run executes

(The user's call, 2026-09-22, option C of three; not built.) Scenarios are solved by a fixed set of
workers, `sizing.workers` (default 6, a setting and never a constant), each taking a contiguous
chunk of the list and warm-starting each scenario from the previous one in its chunk. A fully
parallel fan-out would cold-start every scenario; a sequential run would keep the warm start on one
core; chunks keep both, at one cold start per chunk. Warm starting across scenarios is weaker here
than it was across a 1 K grid, since adjacent scenarios need not be near each other, so the chunk
assignment is worth measuring rather than assuming.

The workers are dedicated threads, not the pool: the property state is per-thread and its native
half is never returned (`21`, `C-76`), so a fixed set costs one state each while thread churn costs
one per thread that dies. The solvers hold no mutable state and are shared; each scenario lowers its
own graph, because sizing writes to it. Steps 1 and 3 each drain the whole list, and step 3 is
embarrassingly parallel because nothing is sized in it. Both check cancellation between scenarios,
because a session re-sizes on every debounced edit and supersedes a stale solve (`41`); a superseded
run leaves the previous sizes in place and the report says how far it got.

**The sizing report**, alongside the solve report (`62`): per scenario the iterations, passes,
termination, wall time and worker; per size the governing scenario and its value, which is the basis
string; totals — scenarios, solves, wall time, the longest scenario, and solver time against wall
time, which is the parallel efficiency. `PipelineTimingDiagnostics` gains a row, so whether `C-68`
and `F-19` ever become a sizing problem is decided from evidence. Measured today (Debug, water): one
outer-loop solve is 21–55 ms on the samples, so a handful of scenarios costs well under a second
even sequentially, and the transient meets `C-68` long before sizing does.

## Constraint propagation

`D-02`'s second half: a stated value constrains rather than seeds. Concretely:

| Stated | Propagates to |
|---|---|
| Heat exchanger `power` + `in` + `out` | The branch's mass flow, exactly |
| Node `flow` | Its branch's flow, exactly |
| Pipe `dn` | Its branch's pressure drop at any flow |
| Valve `kv` | Its branch's pressure drop at any flow |
| Pump `head` | The loop's available pressure — and therefore the flow the loop settles at |
| Valve `authority` | The valve's target `kv` given the branch's other drops |

Propagation runs to a fixed point over the branch graph before any rule fires. A branch whose flow is
determined by propagation is **not** sized — its components are sized *to* that flow. A branch with two
conflicting stated flows is `FS2302`, naming both.

## Per-component rules

Each rule states its basis, and the basis string is carried on the result and shown in hover (`R-23`).
"Sized to 32 mm — 150 Pa/m target, 0.9 m/s" is a number a user can argue with; "32 mm" is one they have
to trust.

### Tank — explicit defaults, not sizing

A hydraulic/thermal solve cannot infer how much storage the designer intends or where a nozzle is
physically located. `D-32` therefore resolves omitted tank `volume`, `layers`, and port elevations as
visible defaults (300 dm³, 5, and 0.5), before this sizing pipeline. They are `FromDefault`, never
`FromSizing`, and the sizing loop must not vary them. A stated value remains a constraint. This narrow
exception is preferable to an invented "sizing" basis that has no demand-duration input.

### Pipe — `dn`

1. Compute the required volume flow from the branch flow and density.
2. Look up the smallest nominal diameter whose pressure gradient at that flow is ≤ the target
   (default **150 Pa/m**). The gradient is Darcy–Weisbach with Colebrook–White on the catalogue's
   **inside** diameter; [`27-component-catalog`](27-component-catalog.md) owns the worked table and
   this document never restates it.
3. Check velocity against the limit for that diameter (below DN50: **1.0 m/s**; DN50–DN150:
   **1.5 m/s**; above: **2.0 m/s**). If exceeded, step up one nominal size and re-check.
   **This is a guard, not a sizing step** (`C-54`, the user's decision 2026-09-21): at the default
   150 Pa/m a size that meets the gradient is already well under its velocity limit -- the gradient
   falls as roughly D⁻⁵ and the velocity as D⁻², so DN25 at 0.411 m/s has a factor 2.4 in hand,
   and a sweep from 0.05 to 12 kg/s never steps up. The guard binds only when a script states a
   much looser gradient target, or at the top of the series where DN150 meets 150 Pa/m at about
   1.65 m/s and nothing larger exists (`FS2305` then names both misses). The limits themselves are
   the noise-driven convention (CIBSE Guide C; manufacturer selection charts) and are not lowered
   to make the step fire.
4. Basis: `"DN{n} — {gradient} Pa/m, {velocity} m/s"`.

**The three bounds have an explicit precedence, because two of them can disagree** (`C-48`).
`velocity_max` is **hard** — a size that exceeds it is never selected. The gradient target is a
**target** — the smallest size meeting it wins, and if none does, the largest catalogue size is taken
with `FS2305`. `velocity_min` is **soft**: if the selected size falls below it, step *down* one size
and report `FS2307`, unless doing so would breach `velocity_max`. Without that ordering the sizer
selects pipes it knows will make its own validator emit `FS4005`: 20 kW at 70/40 is 0.160 kg/s, which
on EN 10255 bores at 55 °C runs DN15 585 Pa/m · DN20 132 Pa/m at 0.438 m/s · DN25 42 Pa/m at
**0.276 m/s** — so a 100 Pa/m target picks DN25, under the 0.3 m/s minimum, with the step-down
available and unconsidered. The whole 100-versus-150 Pa/m argument is one catalogue step for that
branch and there is nothing in between.

**A per-pipe gradient rule cannot see the criterion that actually governs**, which is the index
circuit's total head budget: a domestic circulator offers 40–60 kPa and the gradient has to be
whatever fits it over the index path. That check belongs with `FS2303` after the network is sized, not
inside the per-pipe rule.

Pressure-drop target first, velocity as a check; this resolves the component model's former default-
criterion question and makes the catalogue choice deterministic.
Nominal diameters come from the shipped catalogue
([`27-component-catalog`](27-component-catalog.md)) — DN15…DN300 in v1 — not a continuous solve:
a pipe of 27.4 mm cannot be bought. Note that DN is a **designation, not a diameter**: DN25 steel pipe
has a 27.3 mm bore, and the sizing arithmetic uses the catalogue's inside diameter, never the DN
number.

### Pump — `head` and `flow`

1. `flow` = the loop's design flow, from propagation.
2. `head` = the sum of pressure drops around the loop at that flow, converted through ρ and g.
3. Multiply by the pump's explicit `margin`, default 1.0.
4. Basis: `"{head} m at {flow} l/s — loop drop {dp} kPa"`.

**A stated `dp` is the rise itself** (`C-109`, 2026-09-21). The pump's equation holds
`p_out − p_in = dp · n²` with no density in it, the head is reported as that rise over the solved
density and g -- the mean of the inlet and outlet densities, the convention the pump's residual uses,
so the report and the contract read one number ([`70`](../70-core-refactoring.md) R2) -- and nothing is sized; `head` and `dp` together are `FS2101`. Converting the
stated rise to a head at a reference density would miss by the density ratio, 2.7 % for water at
80 °C, and the rise is what the script asserted.

**No hidden safety margin.** `margin=1.1` is discoverable, recorded in the sizing basis, and multiplies
only auto-sized head; omitting it means 1.0. This represents deliberate design allowance, not missing
fittings. Physical local losses are stated separately as a pipe's `minor_loss` (`D-25`).

### Valve — `kv`

1. Target authority (default **0.5**).
2. Required valve drop = authority × (drop across the controlled branch, valve excluded) / (1 −
   authority).
3. **If the valve is on one of a set of parallel branches, take the larger of that and the balancing
   drop** — see below.
4. `kv` = Q / √(Δp_valve / 1 bar), with **Q the volume flow in m³/h and Δp in bar** — Kv's own
   definition units, not SI ([`22-component-model`](22-component-model.md) gives both forms).
5. Round **down** to the nearest catalogue Kv — an undersized valve has more authority, which is the
   safe direction.
6. Basis: `"Kv {kv} — authority {a} at {flow} l/s"`, or `"Kv {kv} — balanced to {dp} kPa, authority
   {a}"` when step 3 applied.

Rounding down rather than to nearest is an engineering judgement worth stating: it errs toward
controllability at the cost of a slightly higher pump head.

**A stated `dp` replaces steps 1–3** (`C-109`, 2026-09-21): the required Kv is the one that takes the
stated drop at the design flow, and the catalogue row is the **next larger** one, so the valve drops
no more than stated there -- the manufacturers' rule for a calculated Kv between two Kvs values
(Belimo, *Selection and dimensioning of control, open/close and changeover valves*, project planning
notes: a calculated Kv of 4.5 m³/h selects the 6.3 m³/h row). The authority is then reported as
achieved, not targeted, and the basis names both the asked and the achieved drop. This is the
opposite rounding from step 5, and deliberately: step 5 protects a target the rule chose, a stated
drop is a ceiling the user chose. A three-way valve with a stated `dp` takes this rule on its
common-port flow in place of `D-122`'s band.

### Parallel branches must be balanced, not sized independently

Step 3 is the rule that was missing, and its absence made the commonest hydronic sizing task come out
wrong.

**The physics.** Every branch in a parallel set runs between the same two junction elements, so all of
them see the same pressure difference — that is what "parallel" means, and
[`23-topology-and-graph`](23-topology-and-graph.md)'s nodal formulation enforces it exactly. Sizing
each branch's valve to a target authority *against its own branch* produces valves whose drops
generally differ. The solver then does the only thing it can: it redistributes the flows until the
drops match. The branch that got the low-resistance valve takes more than its design flow, the other
takes less, and **the sized design and the solved result disagree** — silently, with every individual
sizing basis reading correctly.

**The rule.** For each set of parallel branches, at the design flows:

1. Compute each branch's drop **excluding its balancing valve**.
2. Take the largest, `Δp_ref`. That branch is the **index branch**. If it has a valve, size that valve
   for target authority and set `Δp_set = Δp_ref + Δp_valve,index`.
3. If the index branch has no valve, it remains the fixed reference and `Δp_set = Δp_ref`; report
   `FS2313`. Other branches may still be balanced up to that drop, but the set makes no target-authority
   claim for the valve-less index branch and the pump is not raised merely to invent one.
4. Every other branch's valve takes `Δp_set − (that branch's drop excluding its valve)` — whatever it
   needs to bring its branch up to `Δp_set` at its own design flow.
5. Report each branch's **achieved** authority. On the non-index branches it is higher than the
   target, because they are absorbing surplus; that is what a balancing valve is for.

This is standard proportional balancing, and the outcome is that every branch carries its design flow
when the circuit is solved — which is the claim the sizing report is implicitly making.

**Two failures the rule must diagnose rather than paper over.**

- **A non-index branch with no valve to balance with.** Its drop is fixed below `Δp_set`, so its flow
  cannot be corrected. `FS2308` names it and says what to add. A valve-less index branch follows step
  3 and is not this error.
- **An index branch chosen by a component the user stated.** If `Δp_ref` comes from a stated `dn` or
  `kv`, the whole set's pump head follows from a number the user wrote, and they should know:
  `FS2309` (info) names the index branch and its drop.

**Worked, on two branches.** Take radiators of 70 kW and 50 kW at ΔT 20 K, both with the 20 kPa
exchanger default, both balancing valves at authority 0.4:

| | Branch 1 | Branch 2 |
|---|---|---|
| Design flow | 0.836 kg/s (3.06 m³/h) | 0.597 kg/s (2.19 m³/h) |
| Drop excluding valve | 20 kPa | 20 kPa |
| Index? | tie — first in declaration order wins | |
| Valve drop, 0.4 authority | 0.4 × 20 / 0.6 = **13.3 kPa** | balanced to 33.3 − 20 = **13.3 kPa** |
| Kv | 3.06 / √0.1333 = **8.39** | 2.19 / √0.1333 = **5.99** |

Both branches come to 33.3 kPa and both carry their design flow. **The equal drops here are a
coincidence of equal defaults**, and that is exactly why the rule is needed: give branch 1 an explicit
`dp=28` and independent sizing produces 46.7 kPa against 33.3 kPa, a 40 % mismatch, while the rule
above raises branch 2's valve to 26.7 kPa and both still deliver. Nothing about the independent
calculation looks wrong on either branch alone.

Ties are broken by declaration order so the choice is deterministic and stable across edits, the same
rule the pressure datum uses.

### Three-way valve — `kv`

**A three-way valve with its bypass connected is sized on the flow through its common port, to a
drop band, and its legs are linear** (`D-122`, closing `C-104`). The authority rule below is what the
script gets when it states `authority=` by name; it is kept in full because the definition of
authority against the variable circuit is the published one, and the achieved figure is still
reported under the band rule so that the two compare like with like.

**The band rule, verbatim from ESBE's rotary mixing valves** (series VRG130, VRG140 and 3F, the
mixing valve of Nordic heating practice): *"Start with the heat demand in kW and move vertically to
the chosen Δt. Move horizontally to the shaded field (pressure drop of 3–15 kPa) and select the
smaller Kvs-value."*[^esbe] The heat demand at the mixed circuit's Δt is the flow through the common
port — what the two legs mix, or what they split — and "the smaller Kvs" is the smallest catalogue
row whose drop at that flow is under **15 kPa** (`three_way.dp_max`); under **3 kPa**
(`three_way.dp_min`) the smallest row is still too large for the flow and the basis says so. ESBE's
Kvs series 0.4, 0.63, 1, 1.6, 2.5, 4, 6.3, 10, 16, 25, 40 is the R5 series the catalogue carries.
Nothing in the rule is a target; the achieved authority — the variable leg fully open against its
circuit, the two-way rule's own definition — is reported beside the Kv, and `FS4006` still fires
below 0.25: Spirax's band for three-port valves starts at 0.2, Johnson Controls' VM-12 shows a
constant-flow three-way valve doing its job at an authority of 0.1, and a low figure is a line, not
a refusal.[^vm12]

**Why the authority rule is the wrong criterion for a mixing valve.** Spirax's rule presumes the
controlled port carries its design flow *fully open* — a coil control valve, mixing only at part
load. A load whose stated inlet lies between the feed and its own return sits mid-travel at design by
construction: 60/40 to 50 is half and half. There an equal-percentage leg passes 14 % of its Kv, so
a Kv chosen for authority 0.5 fully open asks fifty times the chosen drop at the design point: the
ladder's series header was sized to Kv 1.6 and asked 15 bar of its pump (`C-104`); `S-58` had
recorded the mild form on the parallel header. The band rule reproduces the Kv 6.3 those scripts
stated by hand — 0.478 kg/s through Kv 4 drops 18.5 kPa, through 6.3 drops 7.5 — and with linear legs
the drop across the valve at the mixing point is the full-open drop whatever the ratio, because a
leg's opening and its share of the flow move together.

**The legs are linear, both of them.** A three-way valve is a constant-flow device — VM-12's design
requirement is *"a relatively constant system flowrate regardless of its stem position"* — and a
linear pair opening complementarily holds the total (Σφ = 1) where an equal-percentage pair drops it
to 28 % at mid-stroke (VM-12, fig. 2, which is exactly the model's 0.14 + 0.14). Siemens' VXG44 seat
valve is linear in the body with equal-percentage as an actuator option and is *"to be used only as a
mixing valve"*;[^vxg] Belimo's characterised three-way valves carry equal-percentage on A–AB and a
*"modified linear for constant flow"* on B–AB.[^belimo] So the registry defaults `three_way_valve`
to `characteristic=linear` and the two-way `valve` stays equal-percentage; `characteristic=` states
the other. A linear leg keeps 2 % of its Kv at its stop — the equal-percentage law's own φ(0), so a
closed leg passes the same on either characteristic — and the line continues through the stop, which
is what keeps the position column alive when the other leg opens fully (`ValveLaw.LegOpening`; the
two-way linear law is `S-26`'s, unchanged). The figure is this project's regularisation with a
physical reading, not a catalogue one: ESBE quotes under 0.05 % for the seat, Belimo under 2 % on the
B port.

[^esbe]: ESBE, *Mixing valve series VRG130* and *series VRG140* data sheets, "Dimensioning — radiator
    or underfloor heating systems"; *Rotary motorized valves series 3F*, "Dimensioning heating
    systems". https://www.esbe.eu/group/products/rotary-valves/vrg130

[^vm12]: Johnson Controls, *Application Note VM-12: Three-Way Valve Equal Percentage Flow
    Characteristic*, LIT-977AN12.
    https://docs.johnsoncontrols.com/bas/api/khub/documents/1HXsFMicFW1nDoZbAlSxow/content

[^vxg]: Siemens Industry, *VE VXG Electronic Three-way Valves*, Technical Instructions 155-113P25.

[^belimo]: Belimo, *2-way and 3-way characterised control valves*, notes for project planning.

#### The authority rule, for a stated `authority=`

A three-way valve is then sized on its **controlled path**, and the criterion is the same authority the
two-way rule uses. What differs is what authority is measured *against*, and — more consequentially —
that the drop is sometimes chosen and sometimes determined.

**Ports name themselves but do not identify the variable path, and the rule must not use them for
it.** [`22-component-model`](22-component-model.md) settles the names — `a` common, `b` controlled, `c`
bypass — but the binding is **positional**: ports take connections in the order the script writes them.
Measured on `m2-cooling-loop`, `b` binds to `N2` and carries the *recirculation* at 0.076 kg/s while `c`
binds toward `N3` and carries the *primary draw* at 0.163 kg/s, because `3WV - N2` was written before
`3WV - P1`. Reaching for port `b` would size the wrong leg on any script whose author typed the
connections the other way round.

**The variable path is found topologically**: it is the one that reaches a stated-pressure boundary or
an external flux, rather than the one closing back into the valve's own loop. That is the same
criterion the authority definition uses — the circuit whose flow changes when the valve strokes — and it
does not depend on how the script was written.

**Design flow** is the controlled path's flow at the design duty, not the flow through the common
port. In a mixing circuit the common port carries the constant loop flow and `b` carries the variable
draw, and the two differ by the recirculation — for `m2-cooling-loop`, 0.239 kg/s round the secondary
against 0.163 kg/s drawn from the primary.

**Authority is measured against the variable-flow circuit**, which is the part whose flow actually
changes as the valve strokes — the primary path in a mixing circuit. Measuring it against the branch,
as the two-way rule does, would fold in the constant-flow secondary loop, which sits on the other side
of the valve and does not respond to it at all.

This is not a derivation, it is the published rule. Spirax Sarco's control-valve sizing guidance states
that for three-port valves the authority calculation uses the valve's drop "in relation to the circuit
with the **variable flowrate**", and notes that a three-port valve is a constant-flowrate device —
whether mixing or diverting, the total flow through it does not change, so the constant side carries no
information about how well the valve controls.[^spirax]

**Target and bands, from the same sources.** Below **0.2–0.25** control is unstable; **0.25–0.5** is
fair to good; **0.5–1.0** gives excellent control at the cost of pumping energy.[^fluidflow] Spirax is
more conservative for three-port valves specifically — "between 0.2 and 0.5, the closer to 0.5 the
better", and near 0.5 "but not greater than".[^spirax] The two agree on where control goes bad and
differ on whether exceeding 0.5 is a fault; the disagreement is about **energy**, not
controllability, and `FS4006` therefore fires on a low result and not on a high one.

[^spirax]: Spirax Sarco, *Control Valve Sizing for Water Systems*.
    https://www.spiraxsarco.com/learn-about-steam/control-hardware-electric-pneumatic-actuation/control-valve-sizing-for-water-systems

[^fluidflow]: FluidFlow, *Valve Authority: Sizing Control Valves Right*.
    https://fluidflowinfo.com/valve-authority-technical-paper/

#### The drop is chosen on a pump-driven circuit and determined on a bounded one

This is the part with no two-way analogue, and getting it wrong sizes every primary-side valve wrong.

**Pump-driven** — the circuit's driving pressure is free, because a pump's head is sized or promoted
to whatever the loop needs. The valve's drop is then a *choice*, and the authority target makes it, as
in the two-way rule: `Δp_valve = a · Δp_rest / (1 − a)`. Round the catalogue selection **down**: a
smaller Kv drops more, raising authority, and the pump absorbs the difference.

**Pressure-bounded** — both ends of the variable circuit state a pressure, so the driving pressure is
fixed and the flow is fixed by the duty. The valve's drop is then *whatever closes the balance*:

```
Δp_valve = (p_supply − p_return) − Δp_rest
```

There is no freedom left for a target, and applying one anyway under-sizes the drop and leaves the
circuit over-driven. Round the catalogue selection **up** here, which is the opposite direction and for
a specific reason: rounding down makes the design flow unreachable at full travel, because a valve
whose Kv is below what the balance requires cannot pass design flow even wide open. Rounding up leaves
the valve slightly open-ended at the design point, its position a little below 1 — which is the
headroom a control valve is supposed to have.

> **Provenance.** The bounded/pump-driven split and the two rounding directions are reasoned from the
> hydraulics here, not taken from a published rule — a survey of manufacturer and industry guidance
> (Spirax Sarco, FluidFlow, Belimo, Danfoss) found the authority definition and its bands stated
> repeatedly and the catalogue-rounding direction stated nowhere. The physical argument stands on its
> own: at a fixed differential, a Kv below the required value cannot pass the design flow at any
> position. But it is this document's reasoning rather than an inherited convention, and the first
> implementation should treat it as the part most worth testing against measured behaviour.
>
> **It applies to the two-way rule above as well, and `D-89` is the decision that made it so.** That
> rule rounded down unconditionally, on the grounds that more authority is the safe direction — true on
> a pump-driven circuit, where the pump absorbs the extra drop, and false on a pressure-bounded one,
> where it silently makes the design flow unreachable. One rule now serves both valve kinds and both
> shapes: with a free pump on a circuit through the valve the drop is a choice and the selection rounds
> **down**; with none the boundaries fix it and the selection rounds **up**. Which of the two was used
> is written into the reported basis, because the same catalogue row means different things under each.
>
> **"A circuit through the valve" is the valve's biconnected block, not one fundamental cycle** (`S-55`,
> 2026-09-20). In a block any two branches lie on a common cycle, so a free pump anywhere in the block
> reaches the valve's legs: the pump-free mixing header's source valve has no pump on either of its
> legs and is driven by the consumer pumps that draw from its common port -- it sized to Kv 630, the
> bootstrap, until the sizer read the block. The boundaries do not join the blocks for this question,
> so a bounded primary beside a pumped secondary still reads as bounded and rounds up
> (`HydraulicBlocks.ForFreePumps`); the driver diagnostic's reading, where they do, is `23`'s.

**Authority is reported in both cases and targeted only in the first.** On a bounded circuit it is an
outcome, and `FS4006` still fires when it comes out low.

#### Why this is an `OuterLoop` pass and not an `ISizer`

A three-way valve is a **junction element**. It appears in no branch's `Path`, so
`OuterLoop.Context` — which finds a component's branch by `Path.Contains` — can build it no context at
all: it sits on three branches at once. That is the same structural limit as the parallel set above,
and it has the same answer. A rule that must compare sibling branches belongs to the loop, which can
see them, not to an `ISizer`, which is handed one branch (`C-49`, `C-61`).

#### What a static sizing can and cannot promise

**It can choose the Kv.** The design point is where the controlled path carries its maximum flow, and
that is the flow the coefficient must pass. Nothing dynamic is needed for that.

**It cannot validate the control.** The failure mode of an oversized three-way valve is an oscillation:
the supply temperature falls, the valve opens, far more flow arrives than the correction asked for, the
temperature overshoots, the valve closes too far, and the cycle repeats. Plants are commissioned with
two or three differently sized valves in parallel, opened and closed in sequence, precisely to get fine
control near the bottom of the range that one large valve cannot give.

**Authority is the static shadow of that dynamic failure, and it is the only warning this tool can
give.** Low authority means the branch's own resistance dominates until the valve is nearly shut, so
the installed characteristic bunches almost all of its flow change into the first few percent of
travel — which is what makes a proportional controller overshoot. There is no transient solver, so a
valve at an absurd Kv produces a perfectly respectable steady state and reports nothing wrong unless
authority is computed and surfaced. That raises the importance of this rule rather than lowering it:
it is not a refinement of an answer that would otherwise be roughly right, it is the only place the
tool can notice.

#### The bypass leg needs a balancing resistance, and the valve cannot supply it

**Both legs share one `kv` and one `position`, and their effective coefficients are complementary — so
a drop chosen for the controlled leg fixes the bypass leg's drop too, sensible or not.** Sizing from
one leg alone therefore cannot be right, and `C-63` measured what that costs: on `m2-cooling-loop`,
sizing the controlled leg for the 18.9 kPa the pressure balance leaves gives Kv 1.6, the solver must
then close the valve to `position` 0.41 to satisfy the *other* leg, and there the bypass's effective Kv
is about 0.16 and it drops **316 kPa** passing 0.0766 kg/s. The pump is asked for 33.6 m on a loop
whose exchanger drops 5. Sizing for the bypass instead gives Kv 4, and the controlled leg can no longer
restrict enough.

**The missing element is not in the valve. It is a balancing valve in the bypass leg**, and industry
guidance is unambiguous that it is required: the bypass balancing valve is "essential for proper
operation of the water distribution system", set so that "when the valve is in the bypass position, the
pressure drop will be similar to the path through the coil". Without it "a fluid short-circuit occurs
and the supply-to-return differential pressure in the system will drop, possibly starving other coils
in the system".[^bypass][^balance]

So the rule is in two parts, and only the first belongs to the valve:

1. **The valve's `kv` is sized from the controlled leg**, exactly as above.
2. **The bypass leg carries a balancing resistance matched to the drop of the path it bypasses.** That
   is a separate component with its own setting, not something a `kv` can express — one coefficient
   cannot satisfy two legs whose flows differ.

`m2-cooling-loop`'s bypass leg is **empty** — the branch from the valve back to the mixing node contains
no components at all, so its resistance is exactly zero. That is the fluid short-circuit the guidance
describes, and it is why no choice of `kv` makes that circuit behave. The fixture is missing a real
component rather than the sizer missing a rule.

[^bypass]: HVAC Engineering, *Three-Way Control Valves*. https://hvac-eng.com/three-way-control-valves/

[^balance]: Eng-Tips, *Three way control valve balancing*.
    https://www.eng-tips.com/threads/three-way-control-valve-balancing.320544/

#### What the solve reports when the balancing valve is missing — `FS4011`

**The two legs share one `position`, so the position the mixing ratio implies is reached only when
both legs see the same pressure at their far ends.** Half and half is 0.5 with linear legs; 60/40 to
50 sits there by construction. When one path is easier than the other -- a bare bypass returning to
the mixing node against a primary loop the secondary pump has to drive -- the valve throttles the
easy leg to make the flows come out, and it leaves its mixing position to do it. That is the
balancing valve's job being done by the control valve, and the solve can see it: the pressure at
each switched port and at the common port are unknowns of the field.

**`BypassBalance.ReportSolved` reads every three-way valve after a converged solve** (`C-111`,
2026-09-21). For a valve whose two switched legs both enter (mixing) or both leave (diverting), the
imbalance is the difference between the two legs' drops to the common port -- equivalently the
pressure difference between the two far ports. It raises `FS4011` when that imbalance exceeds the
valve's **own full-open drop at the flow its common port carries**, floored at `three_way.dp_min`
(3 kPa) so a valve stated far too large for its flow does not report a few hundred pascals. The
message names the throttled leg, its drop and the solved position, the imbalance against the line,
and the balancing valve that would level the legs: on the easy leg's connection, dropping the
imbalance at that leg's solved flow, with the Kv the law gives for it.

**The line is this project's reasoning, not a published figure.** The guidance says the bypass
should drop what the path it bypasses drops[^bypass][^balance]; it does not say how far off is too
far. An imbalance the valve absorbs inside the drop it was sized for is the working margin every
mixing valve carries; one beyond it means the valve spends more travel balancing than mixing. The
floor is the band rule's own lower edge. Both are the part of this rule most worth testing against
a commissioning engineer's judgement.

**Measured on the ladder's series header** (`step-08b-header-series`, both valves Kv 6.3): the
radiators' valve sits at 0.784 with 3.05 kPa across `a` and 35.0 kPa across `b`, an imbalance of
32.0 kPa against a 7.6 kPa full-open drop, and the message names a balancing valve between `NM_RAD`
and `TV_RAD.b` dropping 32.0 kPa at 0.239 kg/s, Kv 1.53. The AHU's valve at 0.393 has 11.4 across
`a` and 5.0 across `b`, 6.5 kPa against 7.5, and is silent. Where the 32 kPa comes from matters for
what the warning must not promise: the primary ring has no pump, so `PU_RAD` drives the ring's
0.239 kg/s and pays the ring's 32 kPa on the `a` leg's path -- boiler, pipes, the AHU's `a` leg,
less the AHU pump's help. A balancing valve in the bypass dissipates that same 32 kPa on the `b`
side instead of the three-way valve doing it, and the pump head does not fall: 20 kPa of coil plus
32 of ring plus the valve's 7.6 at mid-travel is 59.5 kPa against the 55 solved, not the 3 m the
row first guessed. What the balancing valve buys is the valve's travel and a secondary flow that
holds as the valve moves, which is what VM-12 calls the design requirement.[^vm12] Those kilopascals
are one point on `S-37`'s valley: the ring has no pump of its own, the two blocks' pumps share its
head, and `D-137`'s move from IAPWS-95 to IF97 slid the same script to 24.7 kPa across `b` at 0.74,
an imbalance of 21.3 and Kv 1.88 -- every flow and every Kv unchanged. The rule and the message are
right on either; which split a real plant runs at is what the script does not say, and since `D-142`
the solve says so: `FS3016` names `PU_RAD.head`, `PU_AHU.head` and `TV_AHU.position` as one answer
among those the script allows. The message says "would level the legs and leave the valve its
travel" and nothing about the pump.

#### A `valve` on a switched leg is set to level the legs

**The language sizes the balancing valve, and it does so as a setting rather than a selection**
(`C-111`, second part, 2026-09-21). A `valve` with no stated `kv` whose branch ends at a three-way
valve's `a` or `b` port is a bypass balancing valve, and `OuterLoop.BypassValves` sets it each pass:
the drop it must take is what it drops now plus the imbalance `BypassBalance.Read` measures at the
three-way valve's ports, signed toward its own leg, and its Kv is `ValveLaw.RequiredKv` of that drop
at the leg's solved flow. Nothing rounds it. A balancing valve is set to a measured drop at
commissioning, read off the maker's Kv-per-turn curve -- IMI TA's STAD is set by turns of its
handwheel, and its sizing example picks the setting from the Kv the flow and drop give[^stad] -- so a
catalogue step would be a fiction. The `ValveSizer` does not touch it, because authority is a
control-valve criterion (`C-49`), and it reports no authority for it.

**The fixed point is the setting at which the legs are level.** Each pass re-reads the imbalance
with the previous setting in place, so the setting converges as the passes do: measured on the
one-branch ring, `BV_AHU` goes 4.99 (the bootstrap) to 0.75 to 0.75, three passes, 7 + 5 + 2
iterations, and `TV_AHU` from 0.79 to **0.674**, which is the primary draw's share of the coil flow
(0.1914 of 0.2871) -- the position a linear pair takes when both legs see the same pressure. Both
legs read 6.50 kPa. On the parallel header both valves settle the same way (Kv 0.75 and 0.89), and
on the cooling loop, whose `3WV` diverts, the valve on the return leg is set to Kv 1.4 dropping
17.8 kPa and the three-way valve sits at 0.311, its recirculation share, with 11.52 kPa across
each leg: the reading is the same arithmetic with the sign turned. `FS4011` is silent on all three.

**The bootstrap does not set it from the seed.** The seed's pressure walk caps each valve at
`seed.valve_excursion` (`S-47`) and so understates a ring's cost -- on the series header it read
8.6 kPa where the solve finds 32 -- and a balancing valve set to the seed's guess is a guess the first
solve is then held to.
Nor can it be left fully open: the provisional Kv is the catalogue's largest, 630, which at
0.239 kg/s drops 0.19 Pa, inside the Kv law's regularised band, and the first solve creeps on that row
to its cap. The bootstrap therefore sets it to **3 kPa at the seed's leg flow**, `balancing.dp_min`:
the least drop a balancing valve is ever set to, because below it the differential cannot be
measured accurately[^stad-min]. It is written provisional (`D-96`), and the first solved pass
replaces it.

**A valve on the harder leg is left at that opening**, with a basis saying which leg the balancing
valve belongs on and `FS4011` still naming the bypass: there is nothing on the harder path to
absorb, and a rule that closed the valve anyway would be raising the pump head to no purpose.

**The series header, which first failed here, was the solver's.** `step-08b` with a valve on the
radiators' bypass crept to its iteration cap from the cold seed at the 3 kPa opening and at the seed's
guess alike, while the same first pass with the Kv *stated* took 5 iterations at Kv 2.95 and 46 at
2.94. `S-74` found the Jacobian's pressure columns perturbed inside the property flash's noise and
closed the same day; with that fixed the rule settles on this ring too, in three passes: `BV_RAD`
Kv 1.53 dropping 32.0 kPa, `TV_RAD` at 0.501 with 7.27 kPa across each leg, `PU_RAD` at 59.2 kPa,
which is the 59.5 the row predicted. Under `D-137` the same ring settles at Kv 1.87 dropping 21.3 kPa
and `PU_RAD` at 49 kPa, the legs still level at 0.501: the balancing valve is set to whatever the
ring's undetermined head split leaves on the bypass (`S-37`), and the rule holds at either point.

[^stad]: IMI Hydronic Engineering, *STAD balancing valve* technical guide: "Kvs = m³/h at a pressure
    drop of 1 bar with fully open valve"; the presetting example takes DN 25 at 1.6 m³/h and 10 kPa
    to Kv 5 and reads 2.35 turns off the curve; four turns is fully open.
    https://digitalassets.reecegroup.com.au/m/f9c60cad5113c3a8/original/Technical-Guide-TA-Balancing-Valve-STAD.pdf

[^stad-min]: The same guide, and TA's *Balancing valves* series 786-789 data: a minimum of 3 kPa
    across the valve, because "differential pressure-type balancing valves become more subject to
    inaccuracies due to limitations of typical manometers when operated at differential pressures
    below 3 kPa". https://www.southernpipe.com/ASSETS/DOCUMENTS/CMS/EN/VTL786D_1.pdf

**On the corpus the warning fires on every bare bypass** -- `m2-cooling-loop`'s `3WV` (18.9 kPa
against 12.0), both header valves on every ladder header, `TV_MAIN` on the mixed header -- because
none of those scripts has a balancing valve. That is the finding, not noise: the scripts are the
minimal forms the ladder needs, and a user who copies one now reads what practice would add.

**Out of scope here.** Sequenced parallel valves are a control-topology question, not a sizing one, and
they need a control element that is a *set* rather than a component — the same shape as the parallel
set above. Recorded so it is not mistaken for an oversight.

#### Worked example — `m2-cooling-loop`'s `3WV`

**This circuit is pump-driven, and reading it as bounded is the mistake worth showing.** `N1` states
300 kPa and `N3` states 280 kPa, which looks like 20 kPa driving the controlled path — but the path
from `N1` to `N3` runs **through `PU1`**, whose head is promoted. A pump on the path means the
driving pressure is not what the boundaries say it is; it is whatever the loop turns out to need.
That is why the classification asks about the path the variable flow takes rather than about the
boundary pair.

```
controlled flow  = 30 kW / (4.18 kJ/(kg·K) × (50 − 6) K)   = 0.1630 kg/s
rest of the leg  P1 at that flow (DN25, 25 m)              = 1.10 kPa
Δp_valve         a·rest/(1−a) at a = 0.5                    = 1.10 kPa
required Kv      = 0.163 × 3600 × √(0.988 × 10⁵) ÷ (988 × √1100)
                                                           = 5.63
selected         round **down** in R5                      = Kv 4
achieved drop    = (5.63 / 4)² × 1.10 kPa                   = 2.18 kPa
authority        = 2.18 / (2.18 + 1.10)                    = 0.66
```

**What the two readings cost, measured.** Every row below is a run of the sample with that `kv`
stated, so the difference is the selection and nothing else:

| Kv | Result | `PU1.head` | Valve drop | `3WV.position` |
|---|---|---|---|---|
| 1.0 | `NonFinite` | 62.9 m | 604 kPa | 0.840 |
| **1.6** — what a bounded reading selects | converges | **33.6 m** | 328 kPa | 0.407 |
| 2.5 | converges | 14.4 m | 140 kPa | 0.412 |
| **4** — what this rule selects | converges | **6.4 m** | 61 kPa | 0.426 |
| 6.3 | converges | 3.4 m | 32 kPa | 0.460 |
| 10 | converges | 2.4 m | 22 kPa | 0.529 |

The bounded reading is not merely a different answer, it is a **33.6 m pump on a loop whose exchanger
drops 5** — the figure `C-63` recorded as absurd. One R5 step either way is a factor of about two in
head, so this selection matters more than a valve selection usually does.

**The design point is not where the circuit runs, and the rule does not claim it is.** At Kv 4 the
rule sizes for a 2.18 kPa drop and the converged solve puts **61 kPa** across the valve. Both numbers
are right. The valve's two legs share one coefficient, so the position that satisfies the controlled
leg also fixes the bypass leg, and the bypass leg is what sets the head `PU1` must develop to drive
the constant-flow secondary. The static rule sizes against the variable leg's **own** resistance,
which is `P1`'s 1.10 kPa and does not depend on the pump at all; the operating drop is set by a loop
the rule never looks at. `C-64` records the gap. What saves the answer is that the direction is right
— a larger `kv` lowers the head monotonically, as the table shows — so a rule that sizes generously
against the leg lands on a plant that works, and one that sizes tightly does not.

That a high authority is nevertheless a legitimate operating point is worth stating, because a rule
that treated it as an error would be wrong: a pressure-independent control valve is a product
category built to deliver **100 % authority** deliberately.[^picv] What a high number reports is that
the available differential greatly exceeds what the circuit needs — the condition a
differential-pressure controller exists to absorb.

[^picv]: Danfoss, *Pressure-independent control valves*.
    https://www.danfoss.com/en/products/dhs/differential-pressure-and-flow-controllers/differential-pressure-flow-and-temperature-controllers/pressure-independent-control-valves/


### Heat exchanger — duty mode: `dp` and `flow`

1. `flow` from the energy balance, if `power` and two temperatures are stated.
2. `dp` from a default of **20 kPa** at design flow, the typical plate-exchanger value.
3. Basis: `"{dp} kPa at {flow} l/s — default"`.

The `dp` default is the weakest number in the whole catalogue: real exchangers range from 5 to 60 kPa.
It must be labelled `default` rather than `sized` in hover, and `/docs` must say so. In an extended
mode it is replaced by a computed side-1 drop from channel geometry; Coupled mode additionally computes
side 2, while Rated mode reports no hydraulic result for its external profile. **Not yet**: `P4.1`
left `dp` and `dp2` as the duty-mode defaults in every mode, because the channel-geometry drop needs
the same plate catalogue `u` does.

**A coil that is off has no design point, and is not sized at all** (`S-56`, 2026-09-20). The
design flow above is the flow the circuit runs at; with `power=0` that is zero by construction, and
a zero design flow makes the quadratic law's resistance infinite — 1.8e-27 kg/s passed the sizer's
`<= 0` test once and became a resistance of 1e44. The rule for a zero duty is *no rule*: the exchanger
keeps no `flow`, resists nothing, and the report says `HE_AHU is off (power=0): its 20 kPa has no
design flow to be measured at`. The off coil never inherits its sibling's flow either — the seed used
to fill it from the active branch, and the rule sized it at the other coil's 30 kW (the finding the
entry was filed on). What an off coil's resistance *would* be at its design flow is not knowable from
a script that states no design duty, and pretending otherwise is the trap above.

**A valve nothing flows through keeps the Kv it has** — the same rule, same entry. The band rule for
a mixing valve reads the common port, and a stopped consumer's common port carries nothing while its
legs still pass the stop's 2 % leakage cross-flow; sized on 1e-27 kg/s it chose the smallest row, and
the report then noted that Kv 0.1 was "still larger than this flow wants". Below `flow.zero_tol` on the
common port (or on the branch, for a two-way valve) the sizer returns no value and a note.

### Heat exchanger — extended modes: `ua`, `area`, `plates`

`D-17` establishes the rated exchanger; `D-19` refines its Rated/Coupled sizing trigger. This is the first rule here that answers a genuinely thermal
question. The same thermal steps apply to both; Rated reads side 2 from its boundary profile, while
Coupled reads it from the second hydraulic flow group.

**The rule reads the design point, not the solve** (`ThermalSizer`, `P4.1`). Every step below follows
from what the script stated about the exchanger — the four terminals, or a terminal with a `dt`, and
the duty — which is why the substation's 12 071 W/K is checkable by hand and why a rule that sees one
branch can size a component on two. The one thing it takes from the circuit is a Rated exchanger's
side-1 flow, when the script states neither that flow nor both of that side's temperatures. The
design point also *pins* each side's flow where nothing else does (`D-97`), so the solved circuit runs
at the point the size was chosen for.

1. **Flows** from each side's energy balance, as in duty mode but twice.
2. **Capacity rates** `C₁ = ṁ₁cp₁`, `C₂ = ṁ₂cp₂`; `Cmin = min`, `Cr = Cmin/Cmax`.
3. **Feasibility.** `Qmax = Cmin·(T_hot,in − T_cold,in)`. If the requested duty exceeds it the design
   is thermodynamically impossible, not merely large: `FS2111`, stating `Qmax`. **Check this before
   inverting anything** — step 4 divides by `1 − ε`, which is where an impossible duty otherwise
   surfaces as an overflow or a negative area.
4. **Required NTU**, by inverting ε for the arrangement. Counterflow is closed-form:

   ```
   ε   = Q̇ / Qmax
   NTU = 1/(1−Cr) · ln((1 − ε·Cr)/(1 − ε))          Cr < 1
   NTU = ε / (1 − ε)                                Cr → 1
   ```

   `parallel` inverts in closed form too; `crossflow` does not, and is inverted by bisection on NTU
   over `[0, 50]` — monotone in ε, so bisection is safe and about twenty evaluations.
5. **`UA` = NTU · Cmin.** This is the thermal size, and it is the number the rest follows from.
6. **`area` = UA / U**, with `U` stated. Geometry-derived `U` waits for the plate catalogue, and there
   is no default (`D-99`): with no `u` the rule stops at `ua` and the basis says so.
7. **Plate count** `plates = ceil(area / plate_area) + 2`, rounded **up** to the catalogue's step.
8. **Approach check.** Compute the achieved approach at the selected size; if it is below `approach`
   (stated) or `hx.approach_min` (default), report `FS4008`.
9. **Overshoot report.** The discrete plate count delivers more `UA` than required; when the surplus
   duty exceeds 2 %, `FS2310` (info) says so with both figures.
10. Basis: `"{plates} plates, {area} m² — UA {ua} kW/K, approach {approach} K"`.

**Rounding up rather than to nearest**, like the pipe rule and unlike the valve rule. More area means a
closer approach and more duty, never a shortfall — and unlike an oversized valve, an oversized
exchanger costs capital rather than controllability. Stating the direction and the reason matters
because the three rules in this document now round three different ways.

**Step 3 before step 4 is not a style preference.** `ε → 1` sends `NTU → ∞`, so an infeasible duty
produces an enormous area, an enormous plate count, and a result that looks like an expensive design
rather than an impossible one. The user's actual problem is that they asked for more heat than the
inlet temperatures can move, and only a check placed before the inversion can say so.

**When geometry is stated, `U` and `plates` are a fixed point.** The plate count sets the channel count,
which sets the velocity, which sets `h`, which sets `U`, which sets the area required
([`22-component-model`](22-component-model.md)). The iteration converges from above and is resolved by
the same outer loop as everything else here — not a nested one, for the reason stated at the top of this
document.

### Heat exchanger — a phase-changing side is zoned

`D-80`. A condenser has three zones — desuperheat, condensation, subcool — and an evaporator two,
boiling and superheat. Each is its own ε-NTU element with its own `U`, its own `LMTD` and its own
`Cr`, and the phase-changing zones have `Cr = 0`, so `ε = 1 − e^(−NTU)`.

**A single-zone selection gets the temperature right and the area badly wrong.** Ammonia at −7/40 °C
with `η_is` = 0.7 discharges near 150 °C; desuperheating to saturation is ≈ 319 kJ/kg against 1099 of
latent heat, so **110 K of superheat carries 22 % of the duty**. Gas-side `U` is roughly
500 W/(m²·K) against 3000 for condensation, and with a desuperheat LMTD about 2.5× the condensing one:

```
A_zoned / A_single-U  =  0.78 + 0.22 × (3000/500) / 2.5  =  1.29
```

**≈ 30 % more area than a single-zone rule predicts**, and the evaporator is starker: 5 K of superheat
is about 4 % of its duty and roughly 20 % of its area. **Duty share and area share are different
numbers, and this rule uses the second.** Zoning is also the only way to express a desuperheater,
which produces water above the condensing temperature and lives entirely inside that 22 %.

The `Cr = 0` branch is shared by three unrelated needs — an emitter against a room (`C-50`), a
condensing zone, a boiling zone — which is the evidence it belongs in the ε-NTU core rather than in
any one rule.

### Compressor — `p_high` on a transcritical cycle only

`D-81`. On a **subcritical** cycle there is nothing to size: `P_high = P_sat(T_c)` and `T_c` follows
from the water outlet plus the condenser approach, so the pressure is an output. On a **transcritical**
one the gas-cooler outlet temperature no longer determines the pressure, the machine carries one more
unknown, and this rule closes it.

1. Default: a correlation from `T_gc,out` and `T_evap`. At −7 °C evaporating and a 35 °C gas-cooler
   outlet, Liao–Zhao–Jakobsen gives **89.1 bar** and Kauf **98.5 bar**.
2. Basis: `"{p} bar — {correlation} at {T} °C gas-cooler outlet"`.
3. On request, a golden-section search on the model's own cycle replaces the correlation, reported with
   its gain over it.

**The correlations are 10 % apart in pressure and 1–2 % apart in COP**, because the optimum is flat.
That split is the rule: a correlation is accurate enough for the COP an optimizer reads and not
accurate enough for the pressure rating a compressor selection reads, which is why the search exists
and why it is opt-in rather than default.

### Node — nothing

Nodes carry state, not size. A node with no stated boundary is not sized; it is solved.

## The default catalogue

Every constant above lives in one table, versioned, citable from `/docs`, and exposed in the UI. This
is not a code-organisation preference — a user asking "why 150 Pa/m?" must be able to get an answer,
and a table with a source column is that answer.

| Key | Value | Source |
|---|---|---|
| `pipe.gradient_target` | 150 Pa/m | Common distribution practice |
| `pipe.velocity_max.small` | 1.0 m/s | Noise limit below DN50 |
| `pipe.velocity_max.medium` | 1.5 m/s | DN50–DN150 |
| `pipe.velocity_max.large` | 2.0 m/s | Above DN150 |
| `pipe.velocity_min` | 0.3 m/s | Sedimentation / air entrainment |
| `pipe.roughness` | 0.045 mm | Commercial steel |
| `valve.authority_target` | 0.5 | Control-quality convention |
| `valve.authority_min` | 0.25 | Below this, `FS4006` |
| `three_way.dp_min` | 3 kPa | ESBE's band for a mixing valve at its common-port flow (`D-122`) |
| `three_way.dp_max` | 15 kPa | The top of the same band; the smallest Kvs under it is chosen |
| `pump.margin` | 1.0 | Deliberately none |
| `pump.efficiency_hydraulic` | 0.7 | Typical small centrifugal. **The energy-balance number** — `(1 − η)` of the shaft work heats the fluid (`D-82`) |
| `pump.efficiency_motor` | 0.6 | Small wet-rotor circulator. Wire-to-water is the product, 0.42 here — **the energy-cost number**, and not the row above |
| `pump.loss_destination` | `fluid` | A wet-rotor circulator dumps its motor into the water; a dry-rotor one dumps it into the room. Per component, never global (`D-82`) |
| `hx.dp_default` | 20 kPa | Typical plate exchanger — duty mode only |
| `hx.u_default` | **withdrawn** (`D-99`) | Was 3000 W/(m²·K), uncited. No `U` is invented: without a stated `u` the rule sizes `ua` and says that no area follows. Returns with `27`'s plate catalogue and a source |
| `hx.fouling_default` | 1e-5 m²·K/W | Combined, clean closed-circuit water |
| `hx.arrangement_default` | `counter` | A plate exchanger is counterflow unless built otherwise |
| `hx.plate_step` | 2 | Catalogue plate counts advance in twos, keeping the channel split even |
| `hx.overshoot_report` | 2 % | Above this surplus duty, `FS2310` reports the discrete round-up |
| `hx.approach_min` | **3 K** | Live from M2b (`D-19`). Below roughly 3 K a water/water plate exchanger's area grows faster than any plate count can follow, and the selection stops being a selection. Applies to Rated and Coupled modes; Duty mode has no approach. Triggers `FS4008`. |

## Contracts

```csharp
/// <summary>Fills every parameter the user left unstated (D-02).</summary>
public interface ISizer
{
    /// <summary>Sizes one component against the current flow and pressure estimates.</summary>
    /// <returns>
    /// The sized values with their bases, or a failure when the estimate is insufficient —
    /// which is normal on the first pass and resolved by iteration.
    /// </returns>
    Result<SizingResult> Size(IComponent component, in SizingContext context);
}

/// <summary>What sizing decided, and why.</summary>
public sealed record SizingResult
{
    public required ImmutableDictionary<string, Quantity> Values { get; init; }

    /// <summary>Human-readable basis per parameter, shown in hover (R-23) and in the model
    /// contract. Never null and never empty — an unexplained sized value is a defect.</summary>
    public required ImmutableDictionary<string, string> Bases { get; init; }

    /// <summary>Whether this result came from a catalogue default rather than a computation.</summary>
    /// <remarks>Rendered differently: a default is a placeholder, a computed size is a decision.</remarks>
    public required ImmutableHashSet<string> FromDefault { get; init; }
}
```

## Invariants

1. Sizing never overrides a stated parameter. `StatedParameters` is read-only to the sizer.
2. Every sized value has a non-empty basis string.
3. Sizing is deterministic: the same graph and the same estimates yield the same sizes.
4. Sizing is idempotent at the fixed point — re-running on a converged solution changes nothing.
5. Every sized diameter is a member of the nominal-diameter table; every sized Kv is a catalogue value.
6. The sizing loop terminates within `MaxSizingIterations` (default 10) or reports `FS2301`.
7. A stated value that the circuit cannot satisfy produces a diagnostic, never a silent override
   (`D-02`).

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS2301` | Sizing loop did not converge | Warning | `Sizes did not settle for {list}. Showing the last values; state them directly to fix.` |
| `FS2302` | Conflicting stated flows on one branch | Error | `'{a}' sets flow {v1} and '{b}' sets {v2} on the same branch.` |
| `FS2303` | Stated value the circuit cannot satisfy | Error | `{name}: head={stated} m, but the loop needs {required} m at this flow.` |
| `FS2304` | Nothing to size against | Error | `Cannot size '{name}': no flow is determined anywhere in its branch. State a duty or a flow.` |
| `FS2305` | Required size outside the catalogue | Warning | `'{name}' needs more than DN{max}, the largest size in {catalog}. Using DN{max}.` |
| `FS2306` | Sized value hit a plausibility bound | Warning | `'{name}' sized to {value}, at the edge of the usual range. Check the duty.` |
| `FS2307` | Velocity check forced a size increase | Info | `'{name}' stepped up to DN{n} for velocity.` |
| `FS2308` | A parallel branch has no valve to balance with | Error | `'{branch}' needs {dp} kPa more resistance to carry its design flow, and has nothing adjustable on it. Add a valve.` |
| `FS2309` | The index branch's drop comes from a stated value | Info | `'{branch}' sets the pressure for {n} parallel branches, from {component}'s stated {param}.` |
| `FS2310` | Discrete plate count overshoots the required duty | Info | `'{name}' sized to {plates} plates ({area} m²); {required} m² was needed, so it delivers {actual} kW against {stated} kW.` |
| `FS2311` | Rated boundary profile cannot determine a second inlet state and capacity rate | Error | `'{name}' needs enough side-2 data to rate: provide an inlet plus flow2, or two temperatures with a duty; alternatively connect both secondary ports.` |
| `FS2312` | Auto-sized pump circuit has no explicit resistance | Info | `'{name}' sized to zero head because its circuit contains no modelled resistance. Add a pipe, valve, exchanger drop, or other loss if resistance is intended.` |
| `FS2313` | Parallel-set index branch has no valve | Info | `'{branch}' is the fixed index at {dp} kPa and has no valve; other branches are balanced to it, but no valve-authority target applies here.` |

**Registered as of 2026-09-19 (`C-74`):** `FS2301`, `FS2304`, `FS2305`, `FS2307`, `FS2310` and
`FS2312` -- the six a rule detects. Each is raised beside the note that carried it before, with the
component's name, so the wire, the badge and the log see what the solve explanation always did.
`FS2301` names what moved between the last two passes. The rest of the table waits on a rule that
detects its case; a code nothing produces stays off the documentation page.

`FS2304` is the one the syntax reference hits because no duty determines a flow. If a flow is known but
all connections are ideal, the pump instead sizes to zero head and emits `FS2312` (`D-25`).

## Worked example

The **simple loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) — one series
circuit, one flow, so every step is checkable by hand:

```fluidscript
HE1  heat_exchanger power=30 in.t=20 out.t=50
LOAD heat_exchanger power=-30
CV1  valve
PU1  pump
P1   pipe length=25

connections
N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
```

**Step 1 — seed.** `HE1` states `power`, `in` and `out`, so the energy balance fixes its flow
directly: **0.2392 kg/s** (from [`22-component-model`](22-component-model.md)'s worked example), which
at the loop's 35 °C mean density of 994 kg/m³ is **0.241 l/s**.

**`LOAD` changes none of the arithmetic below, and the circuit has no steady state without it.** A
closed circuit's duties must sum to zero, so 30 kW entering with no sink is a set of equations with no
solution rather than a warm loop (`FS2203`), and the count is square either way. It states its duty
and nothing else: no unknown, no demand, and no stated `dp`, so it adds no pressure drop to size
against and the head below is unchanged. `HE1 in=20` is then the loop's enthalpy datum (`D-65`) —
every thermal relation in a closed circuit is a difference, so one absolute temperature has to be
stated and this is it.

**Step 2 — propagate.** The circuit is one series loop, so that flow is every component's flow. Nothing
in this circuit is free to move it — which is the point of choosing it for this example.

**Step 3 — size, first pass.**

| Component | Rule | Result |
|---|---|---|
| `P1` (25 m) | 0.241 l/s against [`27-component-catalog`](27-component-catalog.md)'s gradient table: DN15 1299 Pa/m ✗ · DN20 292 Pa/m ✗ · **DN25 94.1 Pa/m ✓** | **DN25**, velocity 0.411 m/s ✓ (limit 1.0) |
| `HE1.dp` | catalogue default | **20 kPa** (`FromDefault`) |
| `CV1.kv` | branch drop excl. valve = 25 m × 94.1 Pa/m + 20 kPa = 2.35 + 20 = **22.35 kPa**; authority 0.5 → valve drop 22.35 kPa = 0.2235 bar; Kv = 0.8664 m³/h ÷ √0.2235 = 1.833 | round **down** to catalogue **Kv 1.6** |
| `PU1.head` | valve drop at Kv 1.6 = (0.8664/1.6)² bar = **29.32 kPa**; loop drop = 2.35 + 20 + 29.32 = **51.67 kPa**; ÷ (998.2 × 9.81) | **5.28 m** at 0.241 l/s |

**The head conversion uses the density at the pump's own inlet state, not the loop mean.** Here that
is 998.2 kg/m³ at 20 °C, while the gradient table two rows above uses the loop's 35 °C mean of
994 kg/m³. The switch is deliberate and must be stated, because it is otherwise read as an error: a
pump develops head against the fluid actually entering it, and the same 51.71 kPa expressed at
994 kg/m³ would read 5.30 m. The gap is 0.4 % here and grows with the loop's temperature spread, so
an implementation that silently picks the loop mean will disagree with this worked example by more
than rounding while looking correct.

**Step 4 — solve.** The flow comes back at 0.2392 kg/s — unchanged, because `HE1`'s three stated
parameters pin it through the energy balance. What the solve determines here is the pressure field, not
the flow.

**Step 5 — re-size.** Every rule is re-applied at the solved flow. Nothing moved: the flow is the same,
so the gradient, the required Kv, and the loop drop are all the same. **Clean. Converged in two
passes** — one to size, one to confirm.

Two things this shows, and the second is the one worth internalising.

**Discreteness stabilises the loop.** Kv rounds from 1.833 down to 1.6 and stays there; DN25 is DN25.
Once the catalogue values settle, the only thing that can still move is the pump head, and it moves
only if the flow does.

**A fully constrained duty makes sizing a one-shot calculation, and that is not the general case.**
Here `power` + `in` + `out` determine the flow, so there is nothing to iterate against. Had `HE1`
stated only `power`, the flow would be free, the pump head would set it, and the head would depend on
the flow through the pipe and valve drops — a genuine fixed point needing the iteration in the pipeline
above. The example is deliberately the easy case; the loop exists for the other one.

The final report reads:

```
PU1  head  5.28 m   sized   "5.28 m at 0.241 l/s — loop drop 51.7 kPa"
CV1  kv    1.6      sized   "Kv 1.6 — authority 0.57 at 0.241 l/s"
P1   dn    DN25     sized   "DN25 (EN 10255) — 94 Pa/m, 0.41 m/s"
HE1  dp    20 kPa   default "20 kPa at 0.241 l/s — default"
```

Note the achieved authority is **0.57**, not the 0.5 target: rounding Kv down raises the valve's share
of the loop drop (29.32 of 51.67 kPa), which is the safe direction and is exactly what step 4 of the
valve rule claims. A report showing an achieved authority *below* the target after rounding down would
mean the rounding went the wrong way.

The last row being marked `default` rather than `sized` is the honesty this document is built around:
three of those numbers are engineering, and one is a guess.

## Acceptance criteria

- [ ] The worked example converges in ≤ 5 passes and reproduces the four values above within 2 %.
- [ ] The pipe gradient used by the sizer equals [`27-component-catalog`](27-component-catalog.md)'s
      table for the same flow, computed rather than transcribed.
- [ ] On a **pump-driven** circuit the achieved authority after rounding Kv down is **greater** than
      the target, never less. On a **pressure-bounded** one the selection rounds up instead, so the
      achieved authority may fall below the target and the criterion is the flow: the chosen Kv
      passes the design flow below full travel (`D-89`).
- [ ] **Every branch of a parallel set carries its design flow after the solve**, within tolerance —
      the check that sizing and solving agree. Asserted on a two-branch circuit whose branches have
      *deliberately different* resistances, since equal ones pass even without the balancing rule.
- [ ] Sizing a parallel set reports each branch's achieved authority, and the non-index branches'
      exceed the target.
- [ ] A parallel branch with no adjustable component produces `FS2308` naming it and the shortfall.
- [ ] A valve-less highest-drop branch becomes the fixed index, emits `FS2313`, and lets adjustable
      lower-drop branches balance to it without inventing a valve or raising pump head.
- [ ] A stated `head=15` on that pump is honoured, and `FS2303` fires if the loop cannot use it.
- [ ] A **three-way valve on a pump-driven circuit** is sized to the authority target and rounds
      **down**, so the achieved authority exceeds the target as the two-way rule requires:
      `m2-cooling-loop`'s `3WV` reaches **Kv 4** at an achieved authority of **0.66**, the circuit
      converges, and `PU1` is asked for **6.4 m** rather than the 33.6 m a bounded reading gives.
- [ ] A **three-way valve on a pressure-bounded circuit** — one with no free pump on the path its
      variable flow takes — is sized to the drop the balance leaves it instead, and rounds **up**.
- [ ] Which of the two applies is decided from the **path the variable flow takes**, not from the graph
      as a whole: a pumped secondary beside a genuinely bounded primary must not read as pump-driven.
- [ ] A three-way valve's design flow is its **controlled path's**, not its common port's: asserted on
      `m2-cooling-loop`, where the two differ by the recirculation (0.163 against 0.239 kg/s).
- [x] A three-way valve whose legs differ by more than its full-open drop raises `FS4011` naming the
      balancing valve's leg, drop and Kv; one inside the line is silent: asserted on the ladder's series
      header, `TV_RAD` at 32.0 kPa against 7.6 (Kv 1.53) and `TV_AHU` at 6.5 against 7.5 (`C-111`);
      21.3 against 7.6 (Kv 1.88) under `D-137`, the same valley (`S-37`).
- [x] A `valve` with no `kv` on a three-way valve's switched leg is set, pass by pass, to the drop that
      levels the legs, unrounded, and the three-way valve settles at its ratio: the one-branch ring's
      `BV_AHU` at Kv 0.75 with `TV_AHU` at 0.674 and both legs at 6.5 kPa; the cooling loop's diverting
      `3WV` at 0.31 with `BV1` at Kv 1.4; a valve on the harder leg kept at its 3 kPa opening and told
      where it belongs; `FS4011` silent once level (`C-111`).
- [ ] No sizable parameter survives the loop still holding its bootstrap provisional without saying so
      — the basis names it as provisional and a note explains that no rule chose it (`C-60`).
- [ ] Every sized value in every sample carries a non-empty basis.
- [ ] `FromDefault` is populated for `hx.dp` and empty for pipe diameters.
- [ ] Sizing twice on a converged model produces identical output (idempotence).
- [ ] Every sized diameter is in the nominal table; a test asserts no continuous value escapes.
- [ ] A circuit with no determinable flow produces `FS2304` naming the component.
- [x] The **substation** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) sizes to
      **UA 12.07 kW/K**, **3.658 m²** required, **39 plates**, and an achieved approach of **4.90 K**,
      each within 1 %. `P4.1`: `ThermalSizerTests` and `RatedExchangerSolveTests`. The sample states
      `u=3300` and no `plate_area`, so the shipped script sizes `ua` and `area`; the plate count and
      the 4.90 K are reached when `plate_area=0.1` is stated.
- [x] The required `UA` computed by ε-NTU inversion equals `Q̇ / LMTD` for that counterflow case to
      within rounding — the two routes are checked against each other, not against a stored number.
- [x] A duty above `Cmin·(T_h,in − T_c,in)` produces `FS2111` **before** any NTU inversion runs,
      asserted by a test that would otherwise see an overflow or a negative area.
- [ ] Plate count rounds **up**: a case needing 36.1 effective plates selects 38 total, never 36.
      **Rounds up, but not to the step**: 36.58 effective selects 37 + 2 = 39, which is `01`'s figure
      and `22`'s criterion; `hx.plate_step = 2` would make it 40, and the two documents disagree.
      Left as written until the plate catalogue decides what a step is (`D-99`).
- [ ] Halving `plate_area` roughly doubles the selected plate count, and the achieved approach moves
      the same direction as the area. Not asserted.
- [ ] A Rated exchanger with an incomplete secondary boundary profile produces `FS2311`; a complete
      external profile sizes without secondary connections, and `ua=` alone stays Duty with `FS2110`.
      **Second and third parts met** (`RatedExchangerSolveTests`, `ExchangerModeTests`). An incomplete
      profile sizes nothing and says so in a note — *the design point does not fix both sides* — and
      the exchanger delivers its stated duty; `FS2311` has no descriptor yet.
- [ ] The default catalogue is rendered into `/docs` from the same table the code reads.
- [ ] Omitted tank volume/layers/elevations bypass the sizing loop and are reported as defaults with
      `D-32`'s basis; no sizing pass changes them, and explicit values remain stated constraints.

## Open questions

None. Pump allowance is the explicit `margin` parameter; physical fittings use explicit
`minor_loss` rather than an invented blanket percentage; and a transient run holds the sizes chosen at
the `design` point for its whole length — frozen into the immutable snapshot by `D-22`, but *chosen*
by `D-58`, which is a design condition and not a clock reading.
