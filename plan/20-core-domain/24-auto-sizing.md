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

**The first lowering is a bootstrap and is never solved against.** A pipe with no diameter has no bore
and is not built, so a graph has to exist before flows can be estimated on it; each rule offers a
provisional value for that and nothing else. Flow estimates come from stated duties and stated flows
rather than from resistances, so nothing that survives the bootstrap depends on what it was built with.

**This is the same outer fixed-point loop as deferred expressions**
([`14-expressions-and-references`](../10-language/14-expressions-and-references.md)). They must be one
loop, not two nested ones: a deferred expression can feed a sizing input, and a sized value can feed a
deferred expression. Two loops would interleave unpredictably and could oscillate against each other.
[`31-solver-architecture`](../30-solver/31-solver-architecture.md) owns where that single loop lives.

**Step 1 is what makes convergence fast.** Seeding from stated duties rather than from zero puts the
first iterate close to the answer, and where the duty fully determines the flow it *is* the answer —
the worked example needs two passes for that reason, one to size and one to confirm.

**Every pass of this pipeline runs at the `design` point, and only there** (`D-58`). Sizing is
inherently a single-condition question, so `design` supplies each driver a value, every curve
short-circuits to a constant against it, and the sizes that come out hold for the whole run. In a
static solve the design point is also the operating point and the distinction is invisible. In a
dynamic one it is not: sizing is **not** re-run per time step and it is not run at t = 0 either, which
would size the plant for whatever the weather is at midnight on the first of January. `P3.8` is the
package where sizing first reads `ProjectSettings.Design`; `C-51` records that no rule here yet reads
any driver but `tout`, and that the fraction-of-peak rule a heat pump needs has nowhere to live.

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

A three-way valve is sized on its **controlled path**, and the criterion is the same authority the
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
> **It also applies to the two-way rule above, which does not make the distinction.** That rule rounds
> down unconditionally, on the grounds that more authority is the safe direction. That is true on a
> pump-driven circuit, where the pump absorbs the extra drop, and false on a pressure-bounded one,
> where it silently makes the design flow unreachable. Recorded here rather than edited into the
> two-way rule, because changing a settled rule needs its own decision (`C-62`).

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

**Out of scope here.** Sequenced parallel valves are a control-topology question, not a sizing one, and
they need a control element that is a *set* rather than a component — the same shape as the parallel
set above. Recorded so it is not mistaken for an oversight.

#### Worked example — `m2-cooling-loop`'s `3WV`

The primary is bounded: `N1` states 300 kPa and `N3` states 280 kPa, so 20 kPa drives the controlled
path, and the duty fixes its flow.

```
controlled flow  = 30 kW / (4.18 kJ/(kg·K) × (50 − 6) K)   = 0.1631 kg/s
P1 at that flow  (DN25, 25 m)                              = 1.12 kPa
Δp_valve         = 20 kPa − 1.12 kPa                       = 18.88 kPa
required Kv      = 0.163 × 3600 × √(0.988 × 10⁵) ÷ (988 × √18 880)
                                                           = 1.36
selected         round **up** in R5                        = Kv 1.6
achieved drop    = (1.36 / 1.6)² × 18.88 kPa               = 13.6 kPa
authority        = 13.6 / (13.6 + 1.12)                    = 0.92
```

**0.92 is a finding, not a success, and the rule must report it as one.** It is well above the 0.5 both
sources aim for, and Spirax says explicitly not to exceed it. But it is not a choice the rule made: the
circuit offers 20 kPa of differential against 1.12 kPa of pipe, so *any* valve that limits the flow to
design must take almost all of it. Authority near 1 cannot be avoided by selecting differently — only
by changing the circuit, which is a design decision and not a sizing one.

That it is nevertheless a legitimate operating point is worth stating, because a rule that treated it
as an error would be wrong: a pressure-independent control valve is a product category built to deliver
**100 % authority** deliberately.[^picv] What a high number here actually reports is that the available
differential greatly exceeds what the circuit needs — the condition a differential-pressure controller
exists to absorb.

[^picv]: Danfoss, *Pressure-independent control valves*.
    https://www.danfoss.com/en/products/dhs/differential-pressure-and-flow-controllers/differential-pressure-flow-and-temperature-controllers/pressure-independent-control-valves/

**What the absence of this rule costs, measured.** Until it lands `3WV` keeps the bootstrap
provisional — the largest row in the series, **Kv 630** — because `Bootstrap` hands provisionals out by
kind while `CanSize` selects by type (`C-60`). At Kv 630 the valve drops nothing, the 20 kPa boundary
pair over-drives the loop by about 14 kPa, and the pump is asked for negative head to absorb it. The
sample cannot converge, and every number in it looks plausible.


### Heat exchanger — duty mode: `dp` and `flow`

1. `flow` from the energy balance, if `power` and two temperatures are stated.
2. `dp` from a default of **20 kPa** at design flow, the typical plate-exchanger value.
3. Basis: `"{dp} kPa at {flow} l/s — default"`.

The `dp` default is the weakest number in the whole catalogue: real exchangers range from 5 to 60 kPa.
It must be labelled `default` rather than `sized` in hover, and `/docs` must say so. In an extended
mode it is replaced by a computed side-1 drop from channel geometry; Coupled mode additionally computes
side 2, while Rated mode reports no hydraulic result for its external profile.

### Heat exchanger — extended modes: `ua`, `area`, `plates`

`D-17` establishes the rated exchanger; `D-19` refines its Rated/Coupled sizing trigger. This is the first rule here that answers a genuinely thermal
question. The same thermal steps apply to both; Rated reads side 2 from its boundary profile, while
Coupled reads it from the second hydraulic flow group.

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
6. **`area` = UA / U**, with `U` stated, derived from geometry, or from the catalogue default.
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
| `pump.margin` | 1.0 | Deliberately none |
| `pump.efficiency_hydraulic` | 0.7 | Typical small centrifugal. **The energy-balance number** — `(1 − η)` of the shaft work heats the fluid (`D-82`) |
| `pump.efficiency_motor` | 0.6 | Small wet-rotor circulator. Wire-to-water is the product, 0.42 here — **the energy-cost number**, and not the row above |
| `pump.loss_destination` | `fluid` | A wet-rotor circulator dumps its motor into the water; a dry-rotor one dumps it into the room. Per component, never global (`D-82`) |
| `hx.dp_default` | 20 kPa | Typical plate exchanger — duty mode only |
| `hx.u_default` | 3000 W/(m²·K) | Water/water brazed plate, clean. Used when neither `u` nor geometry is given |
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
| `FS2304` | Nothing to size against | Error | `Cannot size '{name}' — no flow is determined anywhere in its branch. State a duty or a flow.` |
| `FS2305` | Required size outside the catalogue | Warning | `'{name}' needs {value}, larger than the biggest catalogue size ({max}). Using {max}.` |
| `FS2306` | Sized value hit a plausibility bound | Warning | `'{name}' sized to {value}, at the edge of the usual range. Check the duty.` |
| `FS2307` | Velocity check forced a size increase | Info | `'{name}' stepped up to DN{n} for velocity.` |
| `FS2308` | A parallel branch has no valve to balance with | Error | `'{branch}' needs {dp} kPa more resistance to carry its design flow, and has nothing adjustable on it. Add a valve.` |
| `FS2309` | The index branch's drop comes from a stated value | Info | `'{branch}' sets the pressure for {n} parallel branches, from {component}'s stated {param}.` |
| `FS2310` | Discrete plate count overshoots the required duty | Info | `'{name}' sized to {plates} plates ({area} m²); {required} m² was needed, so it delivers {actual} kW against {stated} kW.` |
| `FS2311` | Rated boundary profile cannot determine a second inlet state and capacity rate | Error | `'{name}' needs enough side-2 data to rate: provide an inlet plus flow2, or two temperatures with a duty; alternatively connect both secondary ports.` |
| `FS2312` | Auto-sized pump circuit has no explicit resistance | Info | `'{name}' sized to zero head because its circuit contains no modelled resistance. Add a pipe, valve, exchanger drop, or other loss if resistance is intended.` |
| `FS2313` | Parallel-set index branch has no valve | Info | `'{branch}' is the fixed index at {dp} kPa and has no valve; other branches are balanced to it, but no valve-authority target applies here.` |

`FS2304` is the one the syntax reference hits because no duty determines a flow. If a flow is known but
all connections are ideal, the pump instead sizes to zero head and emits `FS2312` (`D-25`).

## Worked example

The **simple loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) — one series
circuit, one flow, so every step is checkable by hand:

```fluidscript
HE1  heat_exchanger power=30 in=20 out=50
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
- [ ] The achieved authority after rounding Kv down is **greater** than the target, never less.
- [ ] **Every branch of a parallel set carries its design flow after the solve**, within tolerance —
      the check that sizing and solving agree. Asserted on a two-branch circuit whose branches have
      *deliberately different* resistances, since equal ones pass even without the balancing rule.
- [ ] Sizing a parallel set reports each branch's achieved authority, and the non-index branches'
      exceed the target.
- [ ] A parallel branch with no adjustable component produces `FS2308` naming it and the shortfall.
- [ ] A valve-less highest-drop branch becomes the fixed index, emits `FS2313`, and lets adjustable
      lower-drop branches balance to it without inventing a valve or raising pump head.
- [ ] A stated `head=15` on that pump is honoured, and `FS2303` fires if the loop cannot use it.
- [ ] A **three-way valve on a pressure-bounded circuit** is sized to the drop the balance leaves it,
      not to the authority target: `m2-cooling-loop`'s `3WV` reaches **Kv 1.6** with an achieved
      authority near **0.92**, and its position sits below 1 at the design point.
- [ ] A **three-way valve on a pump-driven circuit** is sized to the authority target instead, and
      rounds the other way — down — so the achieved authority exceeds the target as the two-way rule
      requires.
- [ ] A three-way valve's design flow is its **controlled path's**, not its common port's: asserted on
      `m2-cooling-loop`, where the two differ by the recirculation (0.163 against 0.239 kg/s).
- [ ] No sizable parameter survives the loop still holding its bootstrap provisional without saying so
      — the basis names it as provisional and a note explains that no rule chose it (`C-60`).
- [ ] Every sized value in every sample carries a non-empty basis.
- [ ] `FromDefault` is populated for `hx.dp` and empty for pipe diameters.
- [ ] Sizing twice on a converged model produces identical output (idempotence).
- [ ] Every sized diameter is in the nominal table; a test asserts no continuous value escapes.
- [ ] A circuit with no determinable flow produces `FS2304` naming the component.
- [ ] The **substation** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) sizes to
      **UA 12.07 kW/K**, **3.658 m²** required, **39 plates**, and an achieved approach of **4.90 K**,
      each within 1 %.
- [ ] The required `UA` computed by ε-NTU inversion equals `Q̇ / LMTD` for that counterflow case to
      within rounding — the two routes are checked against each other, not against a stored number.
- [ ] A duty above `Cmin·(T_h,in − T_c,in)` produces `FS2111` **before** any NTU inversion runs,
      asserted by a test that would otherwise see an overflow or a negative area.
- [ ] Plate count rounds **up**: a case needing 36.1 effective plates selects 38 total, never 36.
- [ ] Halving `plate_area` roughly doubles the selected plate count, and the achieved approach moves
      the same direction as the area.
- [ ] A Rated exchanger with an incomplete secondary boundary profile produces `FS2311`; a complete
      external profile sizes without secondary connections, and `ua=` alone stays Duty with `FS2110`.
- [ ] The default catalogue is rendered into `/docs` from the same table the code reads.
- [ ] Omitted tank volume/layers/elevations bypass the sizing loop and are reported as defaults with
      `D-32`'s basis; no sizing pass changes them, and explicit values remain stated constraints.

## Open questions

None. Pump allowance is the explicit `margin` parameter; physical fittings use explicit
`minor_loss` rather than an invented blanket percentage; and a transient run holds the sizes chosen at
the `design` point for its whole length — frozen into the immutable snapshot by `D-22`, but *chosen*
by `D-58`, which is a design condition and not a clock reading.
