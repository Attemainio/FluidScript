---
id: 34-controllers
title: Controllers
tier: 30-solver
status: reviewed
owns: [controller model, v1 PI algorithm, optional PID extension, anti-windup, setpoints, actuator coupling, default tunings]
depends_on: [22-component-model, 33-transient-time-domain]
traces_to: [R-13]
open_questions: 0
last_review_pass: 2
---

# Controllers

## Purpose

`R-13`: controllers are part of the model, not the UI. A three-way valve that holds a supply
temperature is doing the thing plant designers actually care about, and modelling it is what separates
"here is your circuit at design conditions" from "here is how your circuit behaves". Controllers are
also the most common source of a transient that oscillates forever, so the anti-windup and limit
handling below is not polish — it is the difference between a usable feature and a confusing one.

## Responsibilities

**Owns.** The v1 PI algorithm, the compatible optional PID extension, anti-windup, setpoint handling, actuator
coupling, and default tunings.

**Explicitly does not own.** Time integration ([`33-transient-time-domain`](33-transient-time-domain.md)),
component equations ([`22-component-model`](../20-core-domain/22-component-model.md)), controller syntax
([`12-grammar`](../10-language/12-grammar.md), which now defines it as an ordinary component
declaration with reference-valued parameters).

## The model

A controller reads a measurement, compares it to a setpoint, and drives an actuator. In FluidScript
it is a **component with no ports**: it participates in the model but not in the flow network.

```csharp
/// <summary>Drives an actuator to hold a measured value at a setpoint.</summary>
/// <remarks>
/// A controller has no ports and contributes no residuals — it is not part of the algebraic
/// system. It is integrated alongside the energy states each timestep and writes its output
/// into the actuator's parameter before the next algebraic solve.
/// </remarks>
public interface IController
{
    string Name { get; }

    /// <summary>What is measured directly under D-23: a component property reference such as <c>N2.t</c>.</summary>
    PropertyReference Measurement { get; }

    /// <summary>What is driven: a settable parameter such as <c>3WV.position</c>.</summary>
    ParameterReference Actuator { get; }

    /// <summary>Target value for the measurement, in the measurement's dimension.</summary>
    Quantity Setpoint { get; }

    /// <summary>Advances the controller by one timestep and returns the new actuator value.</summary>
    /// <param name="measured">Current measured value.</param>
    /// <param name="dt">Timestep in seconds.</param>
    /// <returns>
    /// The actuator command in the actuator parameter's own units, already clamped to its limits.
    /// Dimensionless for a valve position or a relative pump speed, which is every v1
    /// actuator — hence <see langword="double"/> rather than <c>Quantity</c>. A dimensioned
    /// actuator would need the wider type, and adding one is a breaking change to this signature.
    /// </returns>
    /// <remarks>
    /// Stateful: holds the integral term and the previous error. Call exactly once per accepted
    /// timestep — calling it on a rejected step corrupts the integral, which is the classic
    /// adaptive-stepping bug in controller code.
    /// </remarks>
    double Step(Quantity measured, double dt);

    /// <summary>Resets integral and derivative state to a bumpless start at the given output.</summary>
    void Initialize(double actuatorValue);
}
```

**"Call exactly once per accepted timestep" is the invariant that adaptive stepping breaks.**
[`33-transient-time-domain`](33-transient-time-domain.md)'s Heun method evaluates derivatives twice per
step and may reject a step entirely. A controller stepped inside the derivative evaluation would
integrate twice per step and again on every rejection. Controllers are therefore stepped **only in the
outer accepted-step loop**, never inside a derivative evaluation.

That is also the reason a controller is not part of the algebraic system: it would then be evaluated
once per Newton iteration, which is meaningless for a stateful discrete-time element.

## The algorithm

Velocity (incremental) form:

```
e_k    = setpoint − measured_k
Δu     = Kp·(e_k − e_{k−1})  +  Ki·e_k·dt
         − Kd·(measured_k − 2measured_{k−1} + measured_{k−2})/dt
u_k    = clamp(u_{k−1} + Δu, u_min, u_max)
```

**The velocity form gives anti-windup for free.** Because the output is accumulated and clamped rather
than computed from an accumulated integral, a saturated actuator simply stops accumulating — there is
no separate integral term to wind up. The positional form needs explicit back-calculation or clamping
of the integral, which is one more thing to get subtly wrong.

It also gives **bumpless transfer**: changing a gain mid-run changes only the increment, not the
absolute output, so the actuator does not jump.

**Derivative on measurement, not on error.** `Kd` acts on `−measured` rather than on `e`, so a setpoint
step does not produce a derivative kick — an impulse in the actuator that in a real plant slams a valve.
This is a one-sign change in the code and a large difference in behaviour.

**Direct vs reverse acting.** A cooling controller must *close* the valve when the temperature falls;
a heating one must open it. Encoded as a sign on `Kp` — a negative gain is reverse acting — rather than
as a separate flag, because a flag and a sign can disagree and one of them must win.

## Actuator limits and rate

| Property | Default | Reason |
|---|---|---|
| `u_min`, `u_max` | The actuator parameter's declared range (0–1 for a valve position) | From the component registry |
| Slew rate | 1/60 per second (60 s full stroke) | A real valve actuator takes ~30–120 s. Unlimited slew makes the controller look far better than it will be. |
| Deadband | 0 | Off by default; a nonzero deadband stops hunting on a noisy measurement but introduces steady-state error |

The slew-rate limit is the one most likely to be omitted and most likely to matter. A simulation with
an instantaneous actuator settles smoothly; the same loop with a 60-second actuator can oscillate. If
FluidScript is to say anything useful about control behaviour, the actuator has to be as slow as the
real one.

## Default tunings

A user writing a controller with no gains must get something that works.

**Default: PI only, Kd = 0.** Derivative action on a noisy measurement amplifies noise, and thermal
processes rarely need it. A user who wants D asks for it.

Gains from the process's own time constants, which the model knows.

**Every gain carries units, and stating them is what keeps the rules dimensionally honest.** The error
`e` is in the measurement's dimension (K, for a temperature loop); the output `u` is in the actuator's
(dimensionless, for a valve position). So `Kp` is *actuator units per measurement unit*, and `Ki` is
*actuator units per measurement unit per second* — which means **`Ki` must carry `1/K_process`, not
just `1/τ`**:

| Term | Rule | Units (temperature loop on a valve) | Reasoning |
|---|---|---|---|
| `Kp` | `0.5 / K_process` | position / K | Half the estimated ultimate gain |
| `Ki` | `Kp / (2·τ)` where τ is the loop residence time | position / (K·s) | Integral time twice the dominant lag — conservative, settles without overshoot |
| `Kd` | 0 | position·s / K | |

Writing `Ki = 1/(2τ)` instead is the error to avoid: it has units of s⁻¹, so `Ki·e·dt` comes out in
kelvin rather than in valve position, and the resulting gain is wrong by a factor of `K_process` —
about 18 on the worked example below, in the direction that makes the loop unstable.

`K_process` is estimated by a **single perturbation at t = 0**: nudge the actuator by 1 %, re-solve
steadily, measure the change in the controlled variable. Two extra steady solves, once per run, and it
turns "default gains" from a guess into a measurement.

This estimate is crude — it is a steady gain used to tune a dynamic loop — and it must be reported
(`FS3201`, info) with the values used, so a user comparing against a real plant knows what they were
given. The alternative, fixed gains, is worse in a different way: it works for one plant size and
oscillates for others.

## Coupling into the transient loop

Per accepted timestep, in this order:

```
1. Read the measurement from the current state.
2. controller.Step(measured, dt) → new actuator value.
3. Apply slew-rate and range limits.
4. Write the value into the actuator's parameter.
5. Integrate the energy states.
6. Solve the hydraulic subsystem algebraically at the new state.
```

**The controller acts on the previous step's measurement**, which is a one-step delay and is physically
correct: a real controller cannot respond to a temperature before it is measured. Making it
simultaneous would require the controller inside the algebraic solve, which invalidates its
discrete-time nature.

## Invariants

1. `Step` is called exactly once per accepted timestep, never inside a derivative evaluation, never on
   a rejected step.
2. The output is always within the actuator's range.
3. The output never changes faster than the slew limit.
4. With zero error the output is constant — no drift from the integral term.
5. Changing gains mid-run does not step the output (bumpless).
6. A setpoint step produces no derivative kick.
7. Controllers contribute no rows to the algebraic system.
8. A run with a controller whose measurement is already at setpoint reproduces the uncontrolled run.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS3201` | Default gains were computed | Info | `{name}: using Kp={kp} per K, Ki={ki} per K per second, from a measured process gain of {k} K per unit.` |
| `FS3202` | Actuator saturated for the whole run | Warning | `{name} held its valve fully {open/closed} throughout. The setpoint may be unreachable.` |
| `FS3203` | Sustained oscillation detected | Warning | `{name} is oscillating with a period of about {t} s. Try a smaller Kp.` |
| `FS3204` | Measurement references a nonexistent property | Error | `{name} cannot measure '{ref}': {reason}.` |
| `FS3205` | Actuator parameter is not settable | Error | `'{param}' of '{component}' cannot be controlled.` |
| `FS3206` | Process-gain estimation failed | Warning | `{name}: could not measure a process gain; using conservative defaults.` |
| `FS3207` | Setpoint outside the measurement's plausible range | Warning | `{name}: a setpoint of {v} is outside the usual range for {dimension}.` |

`FS3203` requires oscillation detection — zero crossings of the error over a sliding window, with a
period and an amplitude — which is a small amount of work and turns the most common user problem from
"why does my chart wiggle" into a message with a fix in it.

## Worked example

M4's demo is the **demand-step loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)):
`TC1` holds `N2.t` — the mixing-node temperature — at **20 °C** by modulating `3WV.position`, and the
load steps 30 → 45 kW at t = 60 s.

```fluidscript
TC1 controller measure=N2.t actuate=3WV.position setpoint=20
```

An ordinary component declaration. `measure` and `actuate` are reference-valued parameters
([`15-semantic-model`](../10-language/15-semantic-model.md)'s `ParameterValueKind.Reference`), and
`kp`, `ki`, `kd` are optional per `D-02` — omitted here, so the defaults below apply.

**`HE1` states `power` and `out` but not `in`, and that is the point rather than an incidental
simplification.** In the steady circuit that stated value is a constraint, and it promotes the valve
position into a solver unknown
([`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)) — the circuit is *solved* into
position. A controller does the same job dynamically. Leaving both in place would mean the constraint
and the controller fighting for the same actuator, which is over-specification wearing a control
system's clothes.

**`PB`, the discretized recirculation pipe, is what makes any of this a control problem** (`D-16`).
The measured node `N2` is fed by `3WV.b`; without volume on that leg a disturbance at `HE1` reaches the
measurement within one timestep, there is no dead time, and every gain below is tuned against a process
that does not exist. `P1` cannot supply that volume — it sits on the primary return, downstream of
`N2`, and never returns to it.

**Setup.**

| Quantity | Value | Source |
|---|---|---|
| Setpoint | 20 °C | The circuit's design mixing temperature |
| Dead time, `HE1` → `N2` | 38.3 s | Four `PB` pipe cells at 9.6 s ([`33`](33-transient-time-domain.md)) |
| Loop residence time τ | 56.9 s | Dead time plus the 18.6 s secondary circulation |
| Process gain `K_process` | −18.2 K per unit position | measured: +1 % position → −0.182 K |
| `Kp` | 0.5 / 18.2 = **0.0275 position/K** (reverse acting: −0.0275) | rule |
| `Ki` | −0.0275 / (2 × 56.9) = **−2.42 × 10⁻⁴ position/(K·s)** | rule; reverse action applies the same sign to P and I |
| Slew limit | 0.0167 s⁻¹ | 60 s full stroke |

**Response shape.** The trajectory is a test output rather than a hand calculation — it depends on the
four-node transport lag, the valve characteristic, and the flow redistribution together, and no
closed form covers all three. What is asserted, and what the acceptance criteria below pin:

| Phase | Behaviour |
|---|---|
| 60 s → ~90 s | **Dead time.** The measurement barely moves — 0.3 K at t = 70 s, 2.4 K of front at `PB`'s outlet by t = 80 s and less than that after mixing at `N2`. The disturbance is still travelling through `PB`'s four pipe cells at 9.6 s each. The controller must not react, because there is nothing yet to react to. |
| ~90 s → peak | Error grows as the front arrives; the controller opens the valve toward the primary. Peak error **under 4 K**. |
| peak → ~500 s | Recovery, **without sustained oscillation and without crossing below setpoint** — Ki is deliberately slow. |

**Why the dead time dominates the tuning.** Roughly 38 seconds pass between the step and any usable
movement in the measurement. A controller tuned as if the process were instantaneous keeps pushing the
valve throughout that window and then overshoots hard when the response finally arrives — the classic
dead-time instability, and the reason `Ki` is set from the *residence* time rather than from a
step-response fit that would not see the delay.

That dead time is also the argument for `nodes=`. With `nodes=0` on `PB` the delay becomes a single
first-order lag, the controller sees a response almost immediately, and the same gains produce a much
better-looking — and wrong — result. **With no `PB` at all it is worse than wrong**: the measurement
moves in the same timestep as the disturbance, the loop looks trivially controllable, and the tool
would be telling a designer that a plant with 38 seconds of transport behaves like one with none.
That is the failure `D-16` exists to prevent, and it is why the acceptance criterion below is written
as a test that must *fail* when `PB` is deleted.

## Acceptance criteria

- [ ] The worked example settles within 500 s with peak error under 4 K, and never crosses below
      setpoint.
- [ ] The measurement does not move measurably for at least 30 s after the step — the dead time is
      real and the controller does not react before it. **Deleting `PB` from the circuit must make this
      test fail**; a dead-time test that passes on a circuit with no transport path is testing nothing.
- [ ] `Ki` has units of actuator-per-measurement-per-second: a test doubles `K_process` and asserts the
      derived `Ki` halves. A `Ki` derived as `1/(2τ)` alone does not change, which is how the
      dimensional error is caught.
- [ ] A saturating disturbance triggers `FS3202` and produces no integral windup — verified by
      returning the disturbance and checking the controller recovers immediately, not after a delay.
- [ ] Changing a gain mid-run produces no step in the output.
- [ ] If derivative mode is implemented after the v1 PI gate, a setpoint step produces no derivative
      kick and measurement filtering has its own validation fixtures; v1 does not require this mode.
- [ ] The slew-rate limit is never exceeded.
- [ ] A controller at setpoint with no disturbance holds its output constant for 600 s.
- [ ] `Step` call count equals the accepted-step count exactly, asserted with a counting fake.
- [ ] Default gains are derived from a measured process gain, and `FS3201` reports them.

## Open questions

None. The component-like syntax is settled. v1 controllers run only in transients and require PI;
steady-state controller equations, derivative mode, cascade, and sequencing are post-v1 capabilities.
`Setpoint` remains an extensible value type so adding a reference later is non-breaking.
