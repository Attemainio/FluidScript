---
id: 72-roadmap
title: Roadmap beyond v1
tier: 70-future
status: draft
owns: [post-M5 scope, deferred features, the criteria for promoting one]
depends_on: [01-vision-and-scope, 05-milestones-and-acceptance]
traces_to: [R-09, R-15, R-31, R-36, R-45]
open_questions: 0
last_review_pass: 0
---

# Roadmap beyond v1

## Purpose

Everything deliberately deferred, with the reason and what would make it worth doing. The value of
writing this down is not the plan — a roadmap past M5 is speculation — it is that each entry is a
question already answered, so "should we add X?" is met with reasoning rather than rediscovery.

## Responsibilities

**Owns.** Post-M5 scope, the deferred-feature register, and the criteria for promoting one.

**Explicitly does not own.** Anything in M0–M5
([`05-milestones-and-acceptance`](../00-foundation/05-milestones-and-acceptance.md)), export
([`71-export-formats`](71-export-formats.md)).

## Promotion criteria

A deferred feature is promoted when **all four** hold. Fewer than four is a reason to wait, not a
reason to argue.

1. **A user has asked for it**, or its absence has blocked real work. Not "it would be nice".
2. **Its foundation is stable.** Anything wrapping the solver waits for a solver worth wrapping.
3. **It does not require reopening a `D-` decision.** If it does, that is a decision-log entry first.
4. **Its `/docs` cost is affordable** (`R-28`). A feature with twenty new pages is a milestone.

Criterion 3 is the one that does real work: it turns "add loops to the language" from a feature request
into a proposal to supersede `D-01`, which is a conversation with a much higher bar.

## The register

### Language

| Feature | Why deferred | What would promote it |
|---|---|---|
| **Subsystems / composition** | The single most-requested thing a modelling language grows. Needs scoping, naming, port exposure, and a way to draw a collapsed subsystem. | A user modelling a plant with three identical AHUs. Likely M6. |
| **Imports** | Only meaningful with subsystems. | Subsystems shipping. |
| **Loops / conditionals** | `D-01`. Four heat exchangers are four lines; if that becomes painful the answer is subsystems, not iteration. | Would need `D-01` superseded — a high bar, deliberately. |
| **Scenario blocks** | Comparing two operating cases in one file. Attractive, and it means two models and two solutions in one document, which reshapes the model contract. | Real demand for comparison, plus a UI for it. |
| ~~**Range literals `a..b`**~~ | **Moved into v1 by `D-30`.** The grammar owns the shared range form used by schedules and fixed visualization domains. | — |

### Physics and components

| Feature | Why deferred | What would promote it |
|---|---|---|
| ~~**Two-sided heat exchangers**~~ | **Moved into M2 by `D-17`.** No longer future work: the rated ε-NTU model, plate geometry, and coupled circuits ship with the steady-state core. | — |
| **Pinch analysis** | Composite curves, a plant-wide ΔT_min, the grand composite curve, and heat-recovery targets across many streams. A *network* method, distinct from the single-exchanger minimum approach that `D-17` already enforces — that one constrains an exchanger the user drew, this one tells them which exchangers to draw. Needs several streams, a stream-matching step, and a way to present a result that is a *target* rather than a design. | A process engineer asking "how much heat could this plant recover". M7+, and arguably a separate tool. |
| **Fans and air handling** | v1 deliberately validates humid-air properties but does not claim air-side network physics (`D-28`). A real air circuit needs fan curves, duct loss, coil air/water coupling, condensation, and pressure conventions; aliases would only disguise missing equations. | A named air-side reference circuit with independent validation data and user demand. |
| **Fitting catalogue** | v1 accepts explicit per-pipe `minor_loss`; it does not infer fittings from the diagram or ship fitting data. Inference would confuse schematic bends with physical elbows. | A workflow that provides actual fitting inventory or imports it from a physical routing model. |
| **Persistent sensors and instrumentation** | v1 controllers reference state properties directly. A `sensor` component would imply physical location, lag, calibration, failure, and report-point semantics that are not yet modelled (`D-23`, `R-36`). | A real instrumentation or controls workflow that supplies those semantics and validation cases. |
| ~~**Thermal storage / buffer vessels**~~ | **Moved into M4 by `D-32`.** The finite-volume `tank` has mixed layers, indexed elevated ports, and transient capacitance. | — |
| **Heat pumps and chillers** | A performance map (COP vs source and sink temperature), not a first-principles model. | Demand, plus a decision about where performance data comes from. |
| **Steam and two-phase flow** | A different flow regime and a different solver formulation. Genuinely large. | Would be a separate product decision. |
| **Pipe heat loss** | Insulation, ambient temperature, and a UA per pipe cell. It interacts with discretization and is not part of M4. | A named validation case with measured or independently calculated loss, plus a decision on ambient-boundary syntax. |

### Solver

| Feature | Why deferred | What would promote it |
|---|---|---|
| **Sparse linear solve** | v1 deliberately uses dense LU up to the published 800-unknown limit (`D-30`). | An M0/M2 or real-model benchmark that cannot meet `07` without refusing required scale. |
| **Analytic Jacobians** | Numerical differencing is adequate and much safer. | A measured profile showing Jacobian assembly dominating. |
| **Implicit time integration** | v1 fixes an explicit integrator so its stability and checkpoint behaviour are testable. | A measured real model whose stiffness cannot meet the v1 time-step budget. |
| **Steady-state controllers** | “What position holds 20 °C?” is a useful design question, but coupling controller equilibrium into Newton changes equation ownership. | A reference case and well-posedness rules for promoted actuator unknowns. |
| **PID and cascade control** | PI is the required v1 controller. Derivative filtering and controller ordering add tuning and instability contracts. | A validated case where PI is insufficient; cascade additionally needs an ordering/sampling decision. |
| **Bayesian optimization** | Evolutionary is the fixed first M6 algorithm for mixed catalogue variables (`D-30`). | Measurement showing it is evaluation-bound on a continuous-heavy real problem. |

### Product

| Feature | Why deferred | What would promote it |
|---|---|---|
| **A library of example plants** | High value for adoption and for agents (`R-29`), and it needs the features it demonstrates to exist. | M4 — one example per advanced-workflow page. |
| **Hosted demo** | v1 uses screenshots/video and has no hosting/security/cost contract (`D-30`). | Public interest plus an explicit operating budget and abuse/security design. |
| **Collaboration** | Multi-user editing needs authoritative server state, which [`41`](../40-api/41-api-architecture.md) deliberately avoids. Would reopen that design. | A team asking for it. Very large. |
| **Cost estimation** | The project does not maintain regional/time-varying prices; M6 cost objectives require supplied data. | A user-supplied versioned cost table and a real selection workflow. |
| **Report generation** | A PDF with the diagram, the schedule, and the warnings. Mostly assembly once SVG export exists. | Demand from anyone producing design documentation. |

## What is deliberately never planned

Not deferred — declined, with a reason, so the question is closed:

- **STEP and IFC export (`D-12`).** The model has no 3D geometry; inventing it would mislead
  ([`71-export-formats`](71-export-formats.md)).
- **CFD or 1D-detailed transients.** Explicitly a non-goal
  ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)).
- **A general-purpose scripting language.** `D-01`. The moment FluidScript needs a debugger, it has
  failed at what it set out to be.
- **Free-placement CAD drawing.** Layout is computed; manual position overrides are separate post-v1
  research, not a reduced CAD editor (`D-29`).
- **Approval-grade calculation.** The tool produces defensible starting points and says so.

## Sequencing after M5

Not a commitment — an ordering argument, so the first post-M5 conversation starts somewhere.

```
M6  ── subsystems ──► example library
     ├── evolutionary sizing, only with a user-supplied objective/cost source
     └── DXF or versioned model interchange, only after `71`'s consumer gate
M7  ── air-side components ──► heat pumps
M8  ── report generation ──► hosted demo
```

M6 is deliberately a set of evidence-gated tracks rather than a promise that every item ships.
Subsystems have the clearest dependency argument; optimizer and interchange work do not start until
their objective data or external consumer exists (`D-29`). SVG/PNG, durable files, two-sided heat
exchangers, and stratified tanks are already v1 capabilities and therefore do not appear in the
future sequence.

## Invariants

1. Every entry states why it is deferred and what would promote it.
2. Nothing is promoted without all four criteria.
3. A feature requiring a `D-` decision to be reopened gets a decision-log entry first.
4. Declined items stay listed with their reason — a closed question stays closed.

## Worked example

Applying the criteria to "add `for` loops so I can declare eight identical heat exchangers":

| Criterion | Verdict |
|---|---|
| 1 · A user asked | ✓ Assume so |
| 2 · Foundation stable | ✓ The language works |
| 3 · No `D-` reopened | ✗ **`D-01` explicitly excludes control flow** |
| 4 · `/docs` cost affordable | ✗ Loops need scoping, iteration variables, error recovery inside a loop body — several pages, and every existing page has to consider whether it can appear in a loop |

**Not promoted.** The reasoning to give back: `D-01` says the answer to repetition is a subsystem
component, which lets a user define an AHU once and place it eight times — expressing the intent
better than a loop does, since eight identical heat exchangers in a plant are eight *instances of a
thing*, not eight iterations. That promotes **subsystems** instead, which passes all four criteria and
is now first in the M6 sequence.

**This register has already produced one promotion the other way.** Two-sided heat exchangers sat here
as "high priority for M6" until `D-17` moved them into M2 — not because the criteria changed, but
because the cost of *not* having them turned out to be paid in M2 anyway: `FS4008` and
`hx.approach_min` were allocated and permanently dead, and the milestone whose purpose is to prove
auto-sizing could not size the component users most want sized. A deferred item whose absence breaks
the milestone it was deferred out of was never really deferred.

That is what this document is for: a request arrives, the criteria answer it in a minute, and the
answer is a better feature rather than a refusal.

## Acceptance criteria

- [ ] Every register entry states why it is deferred and what would promote it.
- [ ] Every declined item states the reason it is declined, not merely that it is.
- [ ] No entry is promoted without all four criteria being demonstrably met.
- [ ] A promotion that reopens a `D-` decision has a decision-log entry before any work starts.

## Open questions

None. The roadmap is public with promotion criteria and no dates. Items are evidence-gated possibilities,
not delivery commitments; declined items retain their reasoning (`D-30`).
