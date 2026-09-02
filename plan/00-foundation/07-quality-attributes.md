---
id: 07-quality-attributes
title: Quality attributes and operating limits
tier: 00-foundation
status: draft
owns: [quality budgets, supported scale, execution isolation, engineering validity claim, accessibility baseline]
depends_on: [01-vision-and-scope, 06-decision-log]
traces_to: [R-05, R-11, R-12, R-17, R-21, R-40, R-41, R-42, R-43, R-45, R-50, R-51]
open_questions: 0
last_review_pass: 0
---

# Quality attributes and operating limits

## Purpose

Turns words such as “fast”, “accurate”, “responsive”, and “robust” into release gates. These are v1
budgets, not implementation suggestions. A result outside the validated domain is rejected or marked
unsupported; it is never presented with false precision.

## Responsibilities

**Owns.** Cross-cutting budgets, their measurement environment, supported-scale policy, transient
isolation and stop policy, and the engineering validity claim.

**Explicitly does not own.** Solver tolerances (`36`), individual validation fixtures (`62`), REST or
WebSocket shapes (`42`–`43`), frontend mechanics (`51`–`58`), or deployment topology (`41`).

## Contracts

### Recorded benchmark environment

Every performance result records application commit, OS, browser or .NET runtime, logical CPU count,
available memory, power mode, sample file, model counts, warm-up count, sample count, and p50/p95/max.
CI keeps a versioned `benchmarks/reference-environment.json`; changing its hardware class requires a
reviewed baseline update, not silent threshold relaxation.

### v1 budgets

| Attribute | Release gate |
|---|---|
| Editor response | Key input, selection, pan, zoom, and hover schedule their visible response within one 16.7 ms frame at p95. No application task on the UI thread exceeds 50 ms. |
| **Keystroke to diagnostic** | **The normative interactive gate** (`D-48`). From the last keystroke of an edit to that edit's diagnostic decoration being visible: **250 ms p95** for the M1 syntax tour, **400 ms p95** for the 200-declaration reference script. The debounce and draft-compile figures below are components of this, not gates in their own right. |
| Draft compile | A component of the gate above. A 200-declaration script returns parse/bind diagnostics within 150 ms p95 on the reference environment, measured from request receipt to response written. A slower solve may finish asynchronously and cannot stall editing. |
| Idle debounce | A component of the gate above, and a **measured** value rather than a constant (`D-49`). Floored by typing cadence — never short enough to fire inside an average typist's inter-key interval, about 200 ms at 40 wpm — and ceilinged by what the gate above leaves after the measured compile. Set once per environment and recorded; it does not adapt at runtime. |
| Model contract payload | The compile response for the 200-component reference model is at most 512 KiB uncompressed, and server serialization plus client parse together stay within 50 ms p95, counted inside the gate above. Measured separately from compute: a loopback request with a trivial body answers in 0.31 ms p95, and that number says nothing about a response carrying the whole model. |
| Static rendering | Pan and zoom sustain 50 fps p95 for the supported 200-component post-collapse reference model; a frame commit gets an 8 ms main-thread budget and no symbols overlap. Geometry work occurs outside the UI thread. Over-limit/degraded scenes are excluded from this gate and must be labelled as specified by `53`. |
| Transient playback | The 200-declaration demand-step benchmark streams and prepares at least 10 simulation frames/s while the UI remains within the input and long-task budgets. Rendering may coalesce old frames; simulation frames are never reordered. |
| Cancellation/stop | User Stop, client disconnect, or a stop condition reaches the solver cancellation boundary within one produced frame or 250 ms, whichever is longer; no worker continues afterwards. |
| Resource use | A 10-minute run at 10 frames/s keeps at most 60 s of decoded frames plus one full checkpoint per 60 frames by default and remains below 250 MiB additional process memory. Older deltas may stay compact or be discarded according to the declared retention mode. |
| Input limits | Default limits are 1 MiB source text, 10,000 declarations, 100,000 tokens, and 800 solver unknowns. Exceeding one returns a stable diagnostic before solve; limits are returned by metadata. |
| Open documents | At most 8 documents open at once and at most **2 concurrent transient runs**, counted across all documents rather than per document. A third run is refused with a diagnostic naming the running two, never queued silently. An inactive document holds its source, compile result and run state but performs no layout, frame decoding or render preparation (`D-39`). |
| Detached run cost | A run whose document is not active stays within the same 250 MiB retention budget as an active one and adds no UI-thread work. The budget is per run, so two concurrent runs are gated at 500 MiB additional process memory and the second is refused above it (`D-39`). |
| Status latency | The solver status reaches its visible state within 250 ms of the underlying transition, and names which computation it describes — draft compile, steady solve, or a specific transient run (`R-51`). |
| Determinism | Identical source bytes, language major, catalogue pin, property-backend version, solver settings, and platform produce the same graph ordering and diagnostic order. Numeric values meet the tolerance matrix; cross-platform bit identity is not promised. |
| Availability | A malformed or temporarily incomplete draft returns diagnostics and retains the last good static render. It does not terminate the editor session. |
| Accessibility | All v1 workflows meet WCAG 2.2 AA and pass automated checks plus keyboard-only and screen-reader acceptance tests. Canvas information has a structured accessible equivalent. |

**The end-to-end gate governs when it and a component disagree.** Components exist to localise a
regression, not to authorise one: a build whose debounce, compile and payload rows are each green
while keystroke-to-diagnostic is 500 ms has failed, and the fix is not to widen the end-to-end row.
The reverse also holds — a component may be missed if the sum still lands inside the gate, because
what a user experiences is the sum and nothing else. This is `D-48`, and it exists because the
component figures this document already carried composed to 450 ms p95 that no document ever added up.

The 200-declaration workload represents the interactive target; 800 unknowns is the hard v1 solver
limit, not a promise of interactive latency. An explicit Solve may take longer above the interactive
fixture, but it remains cancellable and off the UI thread. These initial limits are conservative so
the API can refuse work predictably before memory or dense-factorization behavior becomes accidental.

### Engineering validity matrix

[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md) owns the fixtures and exact
assertions. The release claim is:

| Capability | Validated v1 domain | Maximum error against independent reference |
|---|---|---|
| Water properties | Liquid water, 0–120 °C and 100–1000 kPa absolute, **below the boiling line at that pressure** | temperature 0.02 K; density 0.1%; enthalpy 0.1%; viscosity 0.5% |
| Humid-air properties | 0–50 °C dry bulb, 10–90% RH, 80–110 kPa absolute; no condensation circuit | humidity ratio 0.5%; enthalpy 0.5%; dew point 0.1 K |
| Conservation | Every converged supported circuit | mass residual ≤ max(1e-8 kg/s, 1e-6 of circuit flow); energy residual ≤ max(0.1 W, 1e-6 of circuit duty) |
| Pressure drop | Single-phase water, Darcy–Weisbach correlations and catalogue geometry declared by the model | 1% against the same published correlation evaluated independently |
| Heat exchanger | Single-phase counter/parallel flow inside the property domain | duty and UA 0.5%; hot/cold energy imbalance 0.1% |
| Auto-sizing | Version-pinned catalogue and stated design constraints | selects the smallest row satisfying every constraint; zero missed constraints in boundary fixtures |
| Transient transport | Single-phase plug-flow/lumped segments within the CFL rule | step arrival within one accepted time step; steady endpoint meets the steady-state matrix |
| Stratified tank (`D-32`) | Single-phase liquid water; 1–100 equal-volume mixed layers; normalized port elevations; adiabatic vessel | mass/enthalpy conservation meets the conservation row; analytic mixed-tank temperature within 0.1 K; layer-refinement error decreases monotonically against the plug-displacement reference |

**The water row is not a rectangle, and reading it as one claims states that are steam.** The
temperature and pressure bounds are independent only where the two together stay liquid: measured,
water boils at 99.61 °C at 100 kPa absolute, 120.21 °C at 200 kPa and 179.88 °C at 1000 kPa. So the
corner above the boiling line — 100 kPa absolute and 110 °C, for instance — is inside the stated
rectangle and is vapour, and the property backend returns it without complaint at 0.573 kg/m³ against
liquid's ~950. The domain is the rectangle **intersected with** the liquid region, and the
implementation enforces it by rejecting any state whose phase is not liquid (`F-13`).

**Both endpoints of the temperature range are phase boundaries, not states.** 0 °C is the melting
line just as 99.61 °C at atmospheric is the boiling line, and on either one a pressure and a
temperature are the same constraint rather than two — so the backend refuses the pair instead of
choosing a side, and the implementation reports `FS2002` (`F-14`). The row's lower bound is therefore
attainable only as a limit: 0.01 °C is a state, 0 °C exactly is not. This is a property of the domain
and not a defect to fix; it is written down so a reader does not take an endpoint for a test case.

These are software-verification bounds against stated equations and independent reference data, not
approval-grade equipment-selection guarantees. Unsupported fluids, two-phase states, cavitation,
compressible networks, condensation, and extrapolation outside the table produce diagnostics.

### Transient isolation and stop policy

Under `D-22`, a run is created from an immutable `RunSnapshot` containing compiled model, source revision hash,
language/catalog/property versions, solver settings, and limits. Later edits create draft revisions;
they neither mutate nor cancel that snapshot. The UI labels playback with its source revision and may
offer an explicit “restart from current draft”.

Backend integration runs on a bounded worker, never the WebSocket request loop. The browser performs
frame decode, delta application, history compaction, colour mapping, and render-geometry
preparation in a Web Worker. Because workers cannot mutate the DOM, the UI thread performs only the
bounded `requestAnimationFrame` SVG commit and accessible-readout update.

The run enters terminal state `failed` and its worker is cancelled when any of these occurs:

- snapshot checksum, topology, units, or array dimensions change during a run;
- a solver value or frame contains a non-finite number, violates monotonic time/sequence, or fails
  conservation beyond the configured failure threshold;
- the backend worker exits, deadlocks past its watchdog, exceeds a hard resource limit, or cannot
  honour cancellation;
- WebSocket/frame schema, snapshot id, base-frame id, or checksum is invalid;
- the frontend worker crashes or cannot apply a delta. The client stops playback and requests no
  further frames; it does not continue with a visually plausible corrupt state.

A syntax error in the editable draft is **not** a run failure. A slow renderer may drop intermediate
display frames but does not alter or stop the computation. Network interruption stops the server run
after the disconnect grace period defined in `43`; reconnect/resume is not a v1 promise.

## Invariants

1. Editing and transient execution share no mutable model object.
2. Computation and render preparation never execute on the browser UI thread.
3. The UI thread's SVG/DOM commit is bounded and may coalesce frames.
4. Unsupported engineering states are diagnostic outcomes, not extrapolated answers.
5. Budget changes include evidence and a reviewed specification change.

## Error cases

| Condition | Required result |
|---|---|
| Input exceeds a published limit | Stable diagnostic naming actual and allowed values; no solve starts |
| Performance gate misses | Milestone cannot exit; do not weaken the test without a reviewed budget change |
| Validation result exceeds tolerance | Capability is unsupported for that domain until corrected |
| Worker/isolation/protocol invariant fails | Stop the affected run, retain its last verified frame as stale, and expose a machine-readable reason |
| Draft becomes invalid during a run | Keep the run and last-good static render; show draft diagnostics independently |

## Worked example

Run revision `sha256:a1…` is at 72 s when the user changes `HE1.power`. The editor compiles revision
`sha256:b2…`; the running worker continues with `a1`, and playback says “Run from revision a1”. If a
delta for frame 721 names base frame 718 while the worker holds 720, playback stops with
`frame-base-mismatch`; it does not guess or apply the delta to the wrong state.

## Acceptance criteria

- [ ] Every milestone that claims a budget has an automated benchmark or a named manual test.
- [ ] The reference environment and raw measurements are retained as CI artifacts.
- [ ] Editing an invalid draft during a transient leaves the run's snapshot and sequence unchanged.
- [ ] Thread instrumentation proves no solve, decode, delta, colour-map, or geometry task runs on the
      browser UI thread; only the bounded DOM commit does.
- [ ] Every stop condition above has a fault-injection test and leaves no live worker.
- [ ] Every supported engineering capability passes its validity row using independent reference data.
- [ ] Keyboard-only, screen-reader, contrast, zoom, reduced-motion, and non-colour-cue tests pass.

## Open questions

None. Thresholds may be tightened from measurements, but their ownership and change process are fixed.
