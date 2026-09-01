---
id: 35-evolutionary-sizing
title: Evolutionary sizing and optimization
tier: 30-solver
status: reviewed
owns: [optimization problem statement, search space, objective and constraints, evolutionary solver adaptation, result reporting]
depends_on: [24-auto-sizing, 31-solver-architecture, 32-steady-state-newton]
traces_to: [R-15]
open_questions: 0
last_review_pass: 2
---

# Evolutionary sizing and optimization

## Purpose

`R-15`. Rule-based auto-sizing ([`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)) answers "what
size is standard practice here". This answers a different question: "what set of sizes minimises cost,
or pumping energy, subject to the constraints I care about". It is **M6**, deliberately last, because
an optimizer wrapping an untrustworthy solve produces confident nonsense faster than anything else in
this project could.

## Responsibilities

**Owns.** The optimization problem statement, the search space, objectives and constraints, adaptation
of the evolutionary solver, and result reporting.

**Explicitly does not own.** Rule-based sizing ([`24`](../20-core-domain/24-auto-sizing.md)), the solve
each evaluation performs ([`32`](32-steady-state-newton.md)), the solver seam
([`31`](31-solver-architecture.md)).

## Reference implementation

A mature evolutionary solver in another of the author's projects (`PandaAI`) is worth studying before
writing one here. It is **not part of this repository and not publicly available**, so nothing below
depends on reading it — each row states the idea rather than pointing at code. If any of it is to be
reused rather than re-implemented, that is a licensing decision needing its own decision-log entry.

**Worth taking:**

| Piece | Why |
|---|---|
| `ISolver` / `SolverBase` split, with partials for Execution, Population, Persistence, Diagnostics | A clean separation that keeps a large solver readable |
| `ISolverParameter` with `ContinuousRange` and `NamedList` | Exactly the two parameter kinds needed here — a continuous pipe length and a discrete DN from a catalogue |
| `IsContinuous` / `IsOrdinal` distinction | Nominal diameters are **ordinal** (DN20 is nearer DN25 than DN100); pump models from a catalogue are **categorical**. Distance metrics differ, and getting it wrong makes the search wander. |
| `SolverDiagnostics` / `BuildSample` publication seam | Progress reporting without coupling the solver to a UI — the same problem this project has |
| `PerformanceTracker`, `EmaMillisecondsPerIteration` | Honest cost reporting for a long run |
| Linkage-tree crossover | Learns which parameters interact — pipe diameters in one branch are strongly coupled, and exploiting that beats uniform crossover |

**Worth leaving:**

| Piece | Why |
|---|---|
| `SolverPool` and multi-solver orchestration | Built for long unattended runs; FluidScript's user waits interactively |
| `BigInteger` iteration counters | PandaAI runs for billions of iterations; here each evaluation is a circuit solve, so thousands is a long run |
| File-based persistence (`Read`/`Write`/`FilePath`) | Session-scoped here; the model is the durable artifact |
| Plotting (`EnablePlot`, `ISolverPlotRenderer`) | Rendering is the frontend's ([`D-03`](../00-foundation/06-decision-log.md)) |
| `Lock`-based concurrency and `IsMaster` | Pool machinery |

**Adapt, do not copy.** That solver is coupled to its own result, serialization and collection types.
The value is in the algorithm and the parameter abstraction, not the code — which is also why this
section is written to stand on its own for a reader who cannot open it.

## Problem statement

```
minimise    f(x)                        the objective
subject to  g_i(x) ≤ 0                  physical and design constraints
            x ∈ X                       the search space
where       each evaluation of f requires a full steady-state solve
```

**The last line dominates every design choice.** A circuit solve is ~50 ms. A population of 50 over 200
generations is 10 000 evaluations, or **8 minutes**. That budget rules out anything needing many
evaluations per candidate, makes caching essential, and makes progress reporting non-optional.

## Search space

Only parameters the user left unstated are variables (`D-02` — stated values are constraints, and the
optimizer must not override them any more than the rule-based sizer may).

| Parameter | Kind | Space |
|---|---|---|
| Pipe `dn` | Discrete, **ordinal** | The nominal-diameter catalogue |
| Valve `kv` | Discrete, ordinal | The Kv catalogue |
| Pump `head` | Continuous | 0.5× to 2× the rule-based size |
| Heat exchanger `dp` | Continuous | 5–60 kPa |

Bounding continuous variables relative to the rule-based answer is what makes the search tractable: it
starts from a known-good design and explores around it, rather than searching the whole feasible space
for a starting point the rule-based sizer already found in one pass.

**The rule-based result seeds the initial population.** One individual is exactly it; the rest are
perturbations. This guarantees the optimizer never returns something worse than the deterministic
answer, which is the minimum bar for the feature to be worth running.

## Objectives

| Objective | Expression | Notes |
|---|---|---|
| `pump_energy` | Σ shaft power × operating hours | Needs an operating profile; defaults to 8760 h at design |
| `capital_cost` | Σ component cost from a user-supplied versioned table | Unavailable without that table (`FS3504`) |
| `total_cost` | supplied capital + energy × years × tariff | Requires the price table plus profile/tariff |
| `pressure_drop` | Total loop drop | A proxy needing no cost data — the honest baseline objective |

**Multi-objective is out of scope for M6.** A Pareto front is the right answer to "cost versus energy"
and it needs a UI to present it, a different result type, and a different algorithm family. Weighted
single-objective first; if it proves inadequate, that is a real finding for a later phase.

## Constraints

| Constraint | Source | Handling |
|---|---|---|
| Velocity within limits | [`24`](../20-core-domain/24-auto-sizing.md)'s catalogue | Penalty |
| Valve authority ≥ 0.25 | `FS4006` | Penalty |
| No freezing, no cavitation | `FS4001`, `FS4003` | **Hard reject** |
| Solve converged | [`32`](32-steady-state-newton.md) | **Hard reject** |
| Stated parameters unchanged | `D-02` | Structural — not in the search space at all |

**Penalty for soft constraints, rejection for hard ones.** A design that fails to solve carries no
usable information, so a penalty value would be arbitrary and would distort the fitness landscape. A
design with a slightly high velocity is a real design that is slightly worse, and a penalty
proportional to the violation gives the search a gradient toward feasibility.

## Contracts

```csharp
/// <summary>Searches for component sizes minimising an objective, subject to constraints.</summary>
public interface ISizingOptimizer
{
    /// <summary>Runs the search, reporting progress and honouring cancellation.</summary>
    /// <param name="graph">The circuit. Stated parameters are fixed and excluded from the space.</param>
    /// <param name="problem">Objective, constraints, and budget.</param>
    /// <param name="progress">Best-so-far per generation. Drives the UI; never null in practice.</param>
    /// <returns>
    /// The best feasible design found, with its objective value and the rule-based design for
    /// comparison. Never worse than rule-based, since that seeds the population.
    /// </returns>
    Task<OptimizationResult> OptimizeAsync(CircuitGraph graph, OptimizationProblem problem,
                                           IProgress<OptimizationProgress>? progress,
                                           CancellationToken cancellationToken);
}

public enum OptimizationObjective
{
    PumpEnergy, CapitalCost, TotalCost, PressureDrop
}

/// <summary>Canonical objective unit: kWh, ISO-4217 currency units, or kPa.</summary>
public sealed record ObjectiveValue(double Value, string Unit);

public sealed record OptimizationBudget(
    int PopulationSize,
    int MaximumGenerations,
    int MaximumEvaluations,
    int StagnationGenerations,
    int Seed);

public sealed record OperatingProfile(double HoursPerYear, int Years, double? TariffPerKwh);

public sealed record VersionedPriceTable(
    string Id,
    string Version,
    string Currency,
    ImmutableDictionary<string, double> ComponentPrices);

public sealed record OptimizationProblem(
    OptimizationObjective Objective,
    OperatingProfile OperatingProfile,
    VersionedPriceTable? PriceTable,
    ImmutableArray<OptimizationConstraint> Constraints,
    OptimizationBudget Budget);

public sealed record OptimizationConstraint(string Id, double Limit, string Unit, bool IsHard);

public sealed record OptimizationProgress(
    int Generation,
    int Evaluations,
    ObjectiveValue BestObjective,
    double FeasibleFraction);

public sealed record ObjectiveSensitivity(
    double ObjectiveChange,
    string ObjectiveUnit,
    double ParameterChange,
    string ParameterUnit);

public sealed record OptimizationResult
{
    public required ImmutableDictionary<string, Quantity> BestValues { get; init; }
    public required ObjectiveValue BestObjective { get; init; }

    /// <summary>The rule-based design's objective, for comparison.</summary>
    /// <remarks>The number that answers "was this worth eight minutes". Reported always.</remarks>
    public required ObjectiveValue BaselineObjective { get; init; }

    public required int Evaluations { get; init; }
    public required bool Converged { get; init; }

    /// <summary>Per-parameter sensitivity — how much the objective moves per unit change.</summary>
    /// <remarks>
    /// Often more valuable than the optimum itself: it tells the designer which three decisions
    /// matter and which twenty do not.
    /// </remarks>
    public required ImmutableDictionary<string, ObjectiveSensitivity> Sensitivity { get; init; }
}
```

## Evaluation caching

The single most valuable optimization. Two mechanisms:

1. **Exact-match cache** keyed by the candidate's discrete values plus rounded continuous ones.
   Populations revisit designs constantly, especially once converging.
2. **Warm-started solves.** A candidate differing in one diameter starts Newton from the nearest cached
   solution rather than from sizing, typically halving the iteration count
   ([`31`](31-solver-architecture.md)).

Together these are worth more than any algorithmic tuning, because the cost is entirely in the solves.

## Invariants

1. A stated parameter is never varied.
2. The rule-based design seeds the population, so the result is never worse than it.
3. Every reported design converged and satisfies every hard constraint.
4. The search is reproducible from a stated seed — same seed, same result.
5. Cancellation returns the best-so-far, not nothing. An eight-minute run cancelled at seven minutes
   must not discard its work.
6. Progress is reported at least once per generation.
7. Every evaluation is a full solve through the same `ISolver` path as a normal solve — no
   simplified physics inside the optimizer.

Invariant 7 rules out the tempting shortcut of an approximate evaluation, which produces an optimum of
the approximation rather than of the model.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS3501` | No feasible design found | Error | `No design satisfies every constraint. The binding one is {constraint}, violated by {amount} in the best attempt.` |
| `FS3502` | Search space empty (everything stated) | Warning | `Nothing to optimize — every parameter is set.` |
| `FS3503` | Budget exhausted before convergence | Warning | `Stopped after {n} evaluations. Best found is {pct} % better than the standard design; it may improve further.` |
| `FS3504` | Objective needs data that is missing | Error | `Cannot optimize for cost without a cost model. Try 'pressure_drop'.` |
| `FS3505` | More than half of evaluations failed to solve | Warning | `{pct} % of candidates could not be solved. The result may be unreliable.` |
| `FS3506` | Cancelled | Info | `Stopped. Best design after {n} evaluations kept.` |

`FS3505` is a real signal, not a nicety: a search space where most candidates fail to solve means the
bounds are wrong, and the result is a search over a fragment of the intended space.

## Worked example

The **simple loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)). First request:
minimise `total_cost` over 15 years at €0.15/kWh and 4000 operating hours, with no price table.

The optimizer does not start. It returns `FS3504`: energy price cannot stand in for pipe, valve,
pump, and exchanger purchase prices. The previous plan claimed an 11.4% total-cost improvement without
providing a single capital-cost input; that number was impossible to reproduce and is deliberately
removed.

The user then chooses the built-in `pump_energy` objective with the 4000 h/year profile.

**The simple loop, not the cooling loop** (`D-11`). Every baseline figure below — DN25, Kv 1.6,
5.28 m of head, 20 kPa — is [`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)'s rule-based
result for the simple loop, and this document previously presented them under the cooling loop's name.
They are not interchangeable: the cooling loop has one pipe sized to DN20 at 0.1649 l/s and three
different flows, which is exactly what makes it useless as an optimizer example a reader can check.

**Search space** (nothing stated except `HE1`'s duty and temperatures):

| Parameter | Kind | Options | Rule-based |
|---|---|---|---|
| Pipe `dn` (`P1`) | ordinal | DN15…DN65, 6 values | DN25 |
| `CV1.kv` | ordinal | catalogue, 12 values | 1.6 |
| `PU1.head` | continuous | 2.6–10.6 m | 5.28 m |
| `HE1.dp` | continuous | 5–60 kPa | 20 kPa |

Space size: 6 × 12 = 72 discrete combinations before the continuous dimensions — small enough that
the optimizer's value here is the sensitivity table rather than the search.

**Baseline (rule-based):** pump 5.28 m at 0.241 l/s, shaft power 17.7 W, using
`ṁ·g·head/η = 0.2392 × 9.81 × 5.28 / 0.7`, and therefore 70.8 kWh/year. This hand calculation is the
objective oracle.

The rule-based design is generation zero. Every candidate must solve and satisfy velocity, valve
authority, exchanger approach, and catalogue constraints. The result may select larger/lower-loss
equipment because capital is intentionally absent from this objective; the UI labels it **minimum
pump energy, purchase cost not considered**. Acceptance is property-based rather than a fabricated
stored optimum: reported energy is independently recomputed, is no greater than 70.8 kWh/year, and no
candidate may improve it by violating a constraint.

The sensitivity table ranks variables from the evaluated neighbourhood and includes units and the
perturbation used. It is explanatory output, not evidence of economic optimality. A future
`total_cost` example must commit its full input price table as a versioned fixture before it may state
an optimum.

## Acceptance criteria

- [ ] The result is never worse than rule-based, over 20 random seeds.
- [ ] The same seed reproduces the same result exactly.
- [ ] Cancelling at 50 % returns a valid design better than or equal to baseline.
- [ ] Every reported design converged and satisfies every hard constraint.
- [ ] Cache hit rate and evaluation count are reported; no minimum hit rate is claimed before measurement.
- [ ] Stated parameters are unchanged in the result — asserted, not assumed.
- [ ] Progress reaches the UI at least once per generation.
- [ ] The worked example's objective is independently recomputed from flow, head, efficiency, and
      operating hours and is no greater than the 70.8 kWh/year baseline.
- [ ] Requesting `total_cost` without a versioned price table produces `FS3504` and performs zero
      candidate solves.

## Open questions

None. This remains an M6 evidence-gated extension. The first implementation is evolutionary for its
mixed discrete/continuous space and ships only engineering objectives such as pressure drop and pump
energy. Capital/total-cost objectives require a user-supplied, versioned price table; FluidScript does
not invent regional cost curves. Accepting an optimized candidate previews and writes every selected
parameter as one undoable script transaction; rejecting it changes nothing (`D-29`, `D-30`).
