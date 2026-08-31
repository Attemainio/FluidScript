---
id: 33-transient-time-domain
title: Transient time-domain solver
tier: 30-solver
status: draft
owns: [time integration, transport delay, thermal capacitance, step-size control, disturbance schedule, frame production]
depends_on: [31-solver-architecture, 32-steady-state-newton, 36-numerics-and-convergence]
traces_to: [R-12, R-14, R-19, R-40, R-41, R-43, R-45]
open_questions: 0
last_review_pass: 0
---

# Transient time-domain solver

## Purpose

`R-12`'s "the system is not in equilibrium" case. The brief is specific about why this exists: to show
how a plant *behaves* when something changes — a demand step, a controller acting, a temperature front
travelling down a pipe. That last one is the interesting requirement, because it is what makes the
transient physical rather than a sequence of steady states.

## Responsibilities

**Owns.** Time integration, transport delay, thermal capacitance, step-size control, the disturbance
schedule, and frame production.

**Explicitly does not own.** Controllers ([`34-controllers`](34-controllers.md)), the algebraic solve
each step reduces to ([`32-steady-state-newton`](32-steady-state-newton.md)), streaming
([`43-realtime-contract`](../40-api/43-realtime-contract.md)).

## What is actually dynamic

The brief's phrasing — "usually explicit with no need of the solver" — is right, and the reason is
worth stating: **hydraulics are fast, thermals are slow.** A pressure disturbance propagates at the
speed of sound in water, ~1500 m/s, crossing a plant in milliseconds. A temperature front travels at
the *flow* velocity, ~1 m/s, taking minutes.

So the model is **quasi-static in pressure, dynamic in energy**:

| Quantity | Treatment | Why |
|---|---|---|
| Branch flows, node pressures | Solved algebraically each step, as a steady problem | Equilibrate far faster than the timestep |
| Node enthalpies | Integrated in time | The physics of interest |
| Pipe internal-node enthalpies | Integrated — this is transport delay (`R-14`) | |
| Tank-layer enthalpies | Integrated — intentional thermal storage and stratification (`R-45`) | One state per layer, bottom to top |
| Component metal temperatures | Integrated, M4+ | Thermal inertia of the hardware |
| Controller states | Integrated ([`34`](34-controllers.md)) | |

This makes each step a **differential-algebraic system solved by splitting**: integrate the energy
states explicitly, then solve the hydraulic subsystem algebraically at the new state. It is the brief's
"explicit with no need of the solver" made precise — explicit for the part that is genuinely dynamic,
Newton for the part that is instantaneous.

**The alternative, rejected:** integrate everything, including pressures, with an implicit method. It is
more general and handles water hammer and fast valve slams. It is also stiff — the pressure dynamics'
time constant is microseconds — forcing either microsecond steps or a stiff implicit integrator, for
phenomena outside this tool's scope ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)'s
non-goals: no acoustics).

## Transport delay

The requirement that shapes the design. A node's enthalpy changes because fluid arrives carrying
different energy:

```
V ρ dh/dt = ṁ (h_upstream − h) + Q̇
```

The time constant is `τ = Vρ/ṁ` — the residence time. A DN20 pipe cell representing 2 m holds **0.740
litres** — the 21.7 mm bore, not the 20 mm designation ([`02-glossary`](../00-foundation/02-glossary.md))
— so at the demand-step loop's recirculation flow of 0.0764 kg/s that is **9.6 seconds**. A front
entering the segment appears at its outlet nine seconds later, which is precisely the observable
behaviour M4's exit criteria demand. Sizing the volume from the DN number instead gives 0.628 l and
8.1 s: a **16 % error in every transport time in the model**, from a number that is not a diameter.

**Resolution is set by discretization.** A single-node pipe smears the front into a first-order lag: at
t = τ the outlet has reached 63 % of the step, not 0 %. `nodes=n` turns it into n first-order lags in
series, and the front sharpens as n grows. Ten nodes gives a recognisable front; a hundred gives a
sharp one at ten times the cost.

**The user chooses via `nodes=`**, and `/docs` must explain the trade — it is the single most important
modelling decision in a transient run, and it looks like a cosmetic parameter.

## Stratified tank

`D-32` defines a tank as N equal-volume, perfectly mixed layers. For total volume V, layer `k` has
`V_k = V/N` and a fixed reference mass `m_k = ρ(T_k(0), p_datum) V_k`. Holding this mass over a run is
the single-phase incompressible approximation; actual property density is still evaluated for state
reporting and buoyancy ordering. It avoids inventing vessel expansion/free-surface geometry while
making mass and enthalpy conservation exact and testable.

For each layer, sum external signed port flows `s_k`, positive into the tank. The hydraulic junction
balance guarantees `Σ s_k = 0`. The internal interface flow above layer k, positive upward, is the
cumulative imbalance below it:

```
u_k = Σ(j=1…k) s_j              k = 1…N−1
```

This is plug displacement between adjacent layers, not numerical diffusion. It satisfies every layer's
fixed-mass balance exactly. Enthalpy changes only through **incoming** streams; an outgoing stream
leaves with its source layer's current enthalpy:

```
m_k dh_k/dt = Σ external inflows ṁ_p (h_p − h_k)
              + max(u_(k−1), 0) (h_(k−1) − h_k)
              + max(−u_k, 0)    (h_(k+1) − h_k)
```

Missing bottom/top terms are zero. This upwind form handles any number of simultaneous sources and
loads and actual flow reversal without changing equation shape. Summing it over all layers cancels
every internal flux, leaving exactly external enthalpy in minus out.

**Density-inversion remixing.** After each accepted RK2 step, scan bottom to top. Adjacent layers are
stable when `ρ_lower ≥ ρ_upper`. Any violating adjacent block is pooled to its mass-weighted mean
enthalpy, then rescanned using the pool-adjacent-violators procedure until the density sequence is
stable. The operation changes neither block mass nor `Σ m_k h_k`; it models rapid natural convection,
not wall conduction. It also remains correct near water's density maximum because it compares the
property backend's density rather than assuming hotter always means lighter.

No ambient loss, wall conduction, or inlet-jet entrainment term exists in v1. Adding a small hidden
diffusivity would make a stored temperature decay for a reason absent from the script.

## Integration

**Explicit, adaptive.** Heun's method (RK2): one predictor, one corrector, two derivative evaluations
per step, with the local error estimated as their difference.

Rejected alternatives, and their costs:

| Method | Why not |
|---|---|
| Forward Euler | First-order; needs ~10× smaller steps for the same accuracy. Cheaper per step, more expensive per second of simulated time. |
| RK4 | Four evaluations per step; the accuracy is beyond what the property correlations justify. |
| Implicit (BDF) | Unconditionally stable, so no CFL limit — genuinely better for a stiff model, and needs a nonlinear solve per step. Revisit only after a measured model cannot meet the explicit-step budget. |

### The step-size limit

Explicit integration of transport is conditionally stable. The limit is the residence time of the
smallest control volume:

```
Δt < CFL · min_i (V_i ρ_i / ṁ_i)         CFL = 0.9
```

For a tank layer, the same limit is `m_k / Σ incoming mass flow to k`, including an incoming internal
interface flow. A stagnant layer contributes no limit. `FS3101` names `T1.layer2` when it is the
limiting control volume.

A DN20 pipe with 21.7 mm bore, `nodes=20`, length 5 m, water density 988 kg/m³, and flow
0.0764 kg/s has 0.25 m pipe cells: each holds 0.0925 l and has residence time 1.20 s, forcing
Δt ≤ 1.08 s. These are the demand-step reference pipe's diameter, density, and recirculation flow.
This is the mechanism by which a user's choice of `nodes` sets the cost of the whole run, and it must be
surfaced: `FS3101` reports the limiting component when the step is constrained, so the user can see
*which* pipe is making their simulation slow.

### Adaptive control

```
Δt_next = Δt · clamp(0.9 · (tol/err)^(1/2), 0.5, 2.0)
```

capped by the CFL limit and by the frame interval. A rejected step (err > tol) halves and retries. The
0.5/2.0 clamps prevent the oscillation that unclamped controllers produce on a discontinuous
disturbance.

## Disturbances

A transient needs something to disturb it. v1's schedule:

| Form | Meaning |
|---|---|
| `at 60s HE1.power = 45` | Step change at a time |
| `over 60s..120s HE1.power = 30..45` | Linear ramp |
| `at 60s 3WV.position = 0.3` | Any settable parameter |

**[`12-grammar`](../10-language/12-grammar.md) now defines this**, as a `schedule` section whose
statements are `at`/`over` disturbances. `at` and `over` are not reserved words — section position
classifies them, exactly as it does connections — and the target is the `component.parameter` shape the
expression grammar already parses. The alternative, disturbances configured in the UI outside the
script, violates principle P5 and was rejected there.

## Frame production

```csharp
/// <summary>One solved instant.</summary>
public sealed record TransientFrame
{
    public required SnapshotId SnapshotId { get; init; }
    public required long Sequence { get; init; }
    /// <summary>Simulation time from t = 0.</summary><value>Seconds.</value>
    public required double Time { get; init; }

    /// <summary>Every state at this instant.</summary>
    public required StateVector State { get; init; }

    /// <summary>Diagnostics raised during this step — a freezing warning that appears at t = 90 s
    /// and clears at t = 140 s is information a steady solve cannot produce.</summary>
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }

    /// <summary>The integration step actually taken, for diagnosing a slow run.</summary>
    public required double StepTaken { get; init; }
}
```

**Frames are emitted on a fixed wall-clock-independent schedule** — every `frameInterval` of simulated
time, default 1 s — not once per integration step. Steps are adaptive and can be milliseconds; emitting
every one would flood the WebSocket with data no one can see. The frame is interpolated from the
bracketing steps.

**Frames stream as they are produced** (`R-19`), so playback begins immediately. The solver is an
`IAsyncEnumerable<TransientFrame>`, which gives streaming, backpressure, and cancellation from the
language rather than from a hand-rolled protocol.

## Contracts

```csharp
public interface ITransientSolver
{
    /// <summary>Runs a transient, yielding frames as they are computed.</summary>
    /// <param name="snapshot">The immutable model and initial state. Dynamic storage states may be
    /// explicitly non-equilibrium; hydraulics and algebraic states are balanced at t = 0. It includes
    /// the fixed sizes and disturbance schedule.</param>
    /// <param name="cancellationToken">Stops the run at the next integration boundary.</param>
    /// <returns>
    /// Frames in increasing time order, at the configured interval. The enumeration ends at the
    /// horizon, on cancellation, or on a step failure — the last frame's diagnostics say which.
    /// </returns>
    IAsyncEnumerable<TransientFrame> RunAsync(RunSnapshot snapshot,
                                              CancellationToken cancellationToken);
}

public sealed record TransientSettings
{
    /// <summary>Simulated duration.</summary><value>Seconds. Default 600.</value>
    public double Horizon { get; init; } = 600;

    /// <summary>Simulated time between emitted frames.</summary><value>Seconds. Default 1.</value>
    public double FrameInterval { get; init; } = 1.0;

    public double MaxStep { get; init; } = 10.0;
    public double MinStep { get; init; } = 1e-4;
    public double CflSafety { get; init; } = 0.9;
    public double LocalErrorTolerance { get; init; } = 1e-4;
}
```

## Invariants

1. Energy is conserved to within the integration tolerance over the run: the integral of net heat
   added equals the change in stored energy plus what left through boundaries.
2. Mass is conserved exactly at every step — it is enforced algebraically, not integrated.
3. A run initialized from a steady solution with no scheduled disturbance and no explicit
   non-equilibrium storage profile stays at that state, drifting by no more than the tolerance over
   the horizon. **This is the strongest correctness test available** and it fails loudly whenever the
   integration or the algebraic solve is inconsistent. An explicit tank `t1`…`tN` profile is an
   initial disturbance and may evolve at t = 0 without a schedule.
4. Sizes are fixed at t = 0 and never re-evaluated
   ([`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)'s fixed-snapshot rule).
5. Frames are emitted in strictly increasing time order at the configured interval.
6. The step never exceeds the CFL limit.
7. Cancellation stops within one step and disposes cleanly.
8. Running to steady state reproduces the Newton solution within tolerance
   ([`31`](31-solver-architecture.md)'s invariant 7).
9. `RunSnapshot` is immutable and includes equation system, initial state, fixed sizes, schedule,
   language/catalog/property/contract versions, settings, limits, and source hash. No draft object is
   reachable from it (`D-22`).
10. A non-finite state, shape/version change, conservation failure threshold, worker fault, or failed
    cancellation terminates the run; a partial corrupt state is never emitted.
11. A tank's layer count, reference masses, port-to-layer map, and total volume are immutable within a
    run snapshot. Every accepted tank step conserves total reference mass exactly and changes stored
    enthalpy only by integrated external enthalpy flow.
12. After inversion remixing, tank density is non-increasing from bottom to top within property
    tolerance; remixing conserves mass and enthalpy to the conservation matrix.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS3101` | Step limited by CFL | Info | `Step limited to {dt} s by '{component}'. Fewer internal nodes would run faster.` |
| `FS3102` | Step fell below `MinStep` | Error | `The simulation cannot advance past {t} s. Something is changing faster than the model can follow.` |
| `FS3103` | Algebraic solve failed within a step | Error | `Could not balance the circuit at t = {t} s: {inner}.` |
| `FS3104` | Horizon reached before settling | Info | `Still changing at {horizon} s. Extend the run to see it settle.` |
| `FS3105` | Disturbance names an unknown component or parameter | Error | `Cannot change '{target}' — {reason}.` |
| `FS3106` | Energy drift beyond tolerance | Warning | `Energy balance drifted by {pct} % over the run. Results may be unreliable.` |
| `FS3107` | Non-finite/shape/snapshot/conservation invariant failure | Error | `Simulation stopped at {t} s because {invariant} failed. The last verified frame is {sequence}.` |
| `FS3108` | A tank layer/profile cannot initialize inside the supported property domain | Error | `Cannot initialize '{tank}' layer {layer} at {state}.` |

`FS3106` is a self-check, and it is the one that catches an integration bug in production rather than
in a test. It costs one accumulator. Drift above `transient.energy_drift_tol` (1 %) warns; drift at or
above `transient.energy_drift_fail` (5 %) violates the run invariant, produces `FS3107`, and stops
before the unverified frame is emitted (`36`).

## Worked example

M4's demo: the **demand-step loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)),
load stepping 30 → 45 kW at t = 60 s via its `schedule` section, with `nodes=4` on `PB`'s 8 m of DN20
recirculation pipe.

**The circuit is the demand-step loop and not the cooling loop, and the difference is the whole
point** (`D-16`). The cooling loop has one pipe, `P1`, on the *primary return* — downstream of the
measured node `N2`, discharging to `N3`, never returning. Computing this document's transport figures
from `P1` puts the delay on a leg the disturbance never travels, and doing it at the secondary flow
rather than `P1`'s own primary flow compounds the error. `PB` is on the recirculation branch, which is
the path a front from `HE1` actually takes to reach `N2`.

**Time constants.** Every one uses `PB`'s **inside** diameter (21.7 mm) and the **recirculation** flow
(0.0764 kg/s at 50 °C, ρ = 988), both from `01`'s solved state:

| Quantity | Value | Reason |
|---|---|---|
| Secondary-loop residence time | 4.5 l × 0.988 / 0.2394 = **18.6 s** | One circulation of the pump loop |
| Pipe cell (2 m of DN20, 21.7 mm bore) | 0.740 l × 0.988 / 0.0764 = **9.6 s** | Per internal node |
| Dead time `HE1` → `N2` | 4 × 9.6 = **38.3 s** | The four pipe cells in series |
| CFL step limit | 0.9 × 9.6 = **8.6 s** | Set by the smallest volume |
| Frame interval | 1.0 s | Interpolated between steps |

**Response.** At t = 60 s the duty rises 50 %. The heat exchanger has no capacitance in v1, so its
outlet enthalpy jumps within one step — **to 65.0 °C**. At the instant of the step the flow has not
changed, so the rise is 45 000 W ÷ (0.2394 kg/s × 4178 J/(kg·K)) = 45.0 K above the 20 °C inlet.

That jump reaches `3WV.a` immediately: there is no volume between `HE1` and the valve. It reaches the
**measured** node `N2` only after crossing `PB`, and that is the transport this document exists to
model. Tracking `PB`'s outlet with the valve **held at its design position** — the uncontrolled case,
which is the one with a closed form — the four pipe cells give a four-stage series lag from 50.0 °C to
65.0 °C:

| Time | s = (t−60)/9.6 | Fraction of the 15.0 K rise | `PB` outlet |
|---|---|---|---|
| 60 s | 0 | 0 % | 50.0 °C — step applied at `HE1` |
| 70 s | 1.04 | 2 % | 50.3 °C |
| 80 s | 2.08 | 16 % | 52.4 °C |
| 90 s | 3.13 | 38 % | 55.7 °C |
| 100 s | 4.17 | 60 % | 59.0 °C |
| 110 s | 5.21 | 76 % | 61.4 °C |
| 130 s | 7.29 | 93 % | 64.0 °C |
| 160 s | 10.42 | 99 % | 64.9 °C |

The fractions are the analytic four-stage series lag, `1 − e⁻ˢ(1 + s + s²/2 + s³/6)`, which is what
four lumped nodes in series produce and is worth asserting directly in a test.

**The first ten seconds are the row that matters.** At t = 70 s the outlet has moved 0.3 K — within
measurement noise on a real plant — which is the dead time
[`34-controllers`](34-controllers.md) tunes against and the reason its acceptance criterion says the
measurement must not move for at least 30 s. Delete `PB` from the circuit and every row above collapses
to the step itself.

Four nodes smear a step into a recognisable but soft front — the 10-to-160 s rise is the
discretization showing, not physics. With `nodes=20` the same front would arrive over roughly a third
of that span and look much sharper. **Both are "correct" for their discretization**, which is exactly
why `/docs` must explain `nodes=`, and why the M4 exit criterion is about *ordering* (a front crosses
8 m later than 2 m) rather than about a specific arrival time.

**Cross-check, and the trap in it.** The settled state must equal a steady solve of the **post-step**
system — invariant 8 — including the valve's final position and the flow redistribution that follows
from it. It is **not** the 65.0 °C computed above: that figure holds the flow at its pre-step value,
which is true for exactly one timestep. A test asserting the first-step temperature as the settled one,
or a hand-computed ΔT as either, fails for a correct implementation, and that trap is worth writing
into the test's comment.

### Storage-header cross-check

The **storage header** reference starts with 300 dm³ in five 60 dm³ layers. With the validation
fixture's `ρ = 1000 kg/m³`, each layer has 60 kg. Source `S2` supplies 0.08 kg/s at 45 °C to layer 2
while `AHU_NETWORK` extracts the same flow from that layer, initially at 30 °C. Constant cp cancels:

```
dT₂/dt = (0.08/60) × (45−30) = 0.020 K/s
```

The other source/load pair is at 60 °C on layer 5 and initially contributes zero derivative. There is
no net layer imbalance, so every `u_k` starts at zero. Over the first 10 s, before inversion mixing,
layer 2 rises by 0.20 K within RK2 tolerance and total stored-energy gain is
`0.08 × cp × 15 × 10`. Once layer 2 becomes less dense than the layer above, only the smallest
unstable adjacent block remixes and its total enthalpy remains unchanged.

## Acceptance criteria

- [ ] A run with no disturbance drifts less than 0.1 % over 600 s (invariant 3).
- [ ] The settled state after a step equals a steady solve of the post-step system within tolerance.
- [ ] A front reaches a node 8 m downstream later than one 2 m downstream, by roughly length ÷ velocity.
- [ ] `PB`'s outlet has moved less than 0.5 K ten seconds after the step, and the transport figures use
      `PB`'s **inside** diameter and the **recirculation** flow — not the DN number and not the
      secondary flow. Both substitutions look plausible and both are wrong by 16 % or more.
- [ ] Doubling `nodes` sharpens the front, measured as the 10–90 % rise time.
- [ ] `FS3101` names the limiting component.
- [ ] Cancelling mid-run stops within one step, with no background work left.
- [ ] Frames arrive at the configured interval regardless of the internal step size.
- [ ] Energy drift over the M4 demo is below `FS3106`'s threshold; crossing the failure threshold
      produces `FS3107`, stops the worker, and emits no unverified frame.
- [ ] Editing, saving, or invalidating the draft during a run does not change snapshot identity,
      equation count, sizes, schedule, settings, or frame sequence.
- [ ] The storage header's layer-2 initial derivative is 0.020 K/s and its 10 s temperature increase
      is 0.20 K within integration tolerance; all other initial layer derivatives are zero.
- [ ] A multi-height flow fixture reproduces the cumulative interface-flow formula for upward and
      downward displacement, including a reversed nominal tank port.
- [ ] An intentionally inverted profile remixes only the minimal violating block, leaves density
      stable bottom-to-top, and conserves its mass and enthalpy to the matrix in `07`.
- [ ] `layers=1` matches the analytic fully mixed control-volume response; increasing layer count
      converges monotonically toward the committed plug-displacement reference.

## Open questions

None. Schedule values are statically evaluable; v1 uses the explicit integrator and stops with
`FS3102` when stiffness drives the step below its supported minimum; browser-worker checkpoints every
60 frames provide bounded backward scrubbing (`43`).
