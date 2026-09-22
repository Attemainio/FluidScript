# Plan review — P6 (M4, the transient phase) readiness, with a P7 (M5) check

Reviewed scope: the documents P6's seven packages implement against — `33`, `34`, `31` (the seam),
`43`, `07` (isolation and budgets), `05` (M4 exit criteria), `36` (transient tolerances), `62`
(V8/V9/V11/V15–V17/V23), `12` (schedule grammar), `15` (control binding), `22` (tank), `01`/`D-16`
(the demand-step reference) — read against the **built** Core: `SystemLayout`, `EquationSystem`,
`OuterLoop`, `Tank`, `GraphNode.ThermalVolume`, `IController : IObserver`, `ControlBindingSymbol`,
`DisturbanceSymbol`, `Lowering`'s mode switch, the controller registry row. P7's two documents (`17`
mutation API, `54` write-back, `42` `edit`) were checked more lightly. This is a targeted direct
review requested by the user before P6 starts, not a convergence sweep; `cleanSweeps` does not move.

**Verdict.** P7 is implementable as written (nits only). **P6 is not**: the transient design is
physically sound in its choices (quasi-static pressure, dynamic energy, Heun, CFL, controllers outside
the derivative), but the *assembly* that connects it to the built equation system is unspecified in
seven places where a fresh session would either stop or invent a semantics that contradicts a worked
example. All seven are fixable in the plan without new physics; most need one new `D-` each.

Numbering note: the user asked about "P7 … transient solvers". In `08`, **P6 = M4 "make it move"**
is the transient phase; **P7 = M5 "close the loop"** is the mutation API and write-back.

## Mechanical result

`python3 .claude/plan-review/check.py` — at the session baseline (63 lines / 60 known problems, all
pre-existing). No new structural issue.

## Blocking findings

### F-1 · `33`'s state partition contradicts itself and the built graph

**Evidence.** `33:70-76` table: "Node enthalpies — Integrated in time". `33:62`: "nothing but a
pipe-internal node has a volume today (`GraphNode.ThermalVolume` is zero everywhere else)". Built:
`Lowering.Build.Expand` sets `ThermalVolume` on pipe cells only; every other node's is 0.

**Why it blocks.** A node with `V = 0` has `τ = Vρ/ṁ = 0`; its "ODE" `Vρ dh/dt = ṁ(h_up − h)` is
`0 = ṁ(h_up − h)` — the algebraic mixing balance the steady system already holds. It cannot be
integrated, and a session that tries gets an infinitely stiff state and `FS3102` on the first step.
The same holds for the exchanger's injection and every mixing node in the cooling-loop family.
Physically: v1 has *no* capacitance anywhere but pipes and tanks, so the transient's differential
set is small and the algebraic set is the whole steady system with a few unknowns pinned.

**Correction.** Rewrite the table: differential = pipe cells, tank layers (and M4+ metal masses);
algebraic = branch flows, node pressures, **every zero-volume node enthalpy, exchanger injections and
promotions**. State the per-step operation exactly: *the per-step algebraic solve is the steady
system with the differential enthalpy unknowns removed and their values substituted* — a
`SystemLayout` partition (Differential / Algebraic) and an `EquationSystem` view that pins the
differential unknowns, solved by the existing scaled Newton with warm start from the previous step.
This is the whole of P6.0 (F-15) and it is not written anywhere.

### F-2 · Which stated constraints survive t = 0 is undefined, and the worked example silently chooses

**Evidence.** The demand-step script (`01:477-505`) states `HE1 heat_exchanger power=30 out.t=50`.
In static mode a stated `out.t` is a constraint (`D-02`, `D-32`), classified by `WellPosedness` as a
FixedFlow-style promotion that sets the secondary flow. `33:365-368`: at t = 60 s "the heat exchanger
… outlet enthalpy jumps within one step — to 65.0 °C". So during the run `out.t=50` is **not** held.
No document says which stated values are run-long constraints and which are t = 0 conditions.

**Why it blocks.** Two readings, both implementable, with opposite results: (a) `out.t=50` holds for
the run, so the flow through `HE1` re-promotes every step and the transient shows no temperature
front — the M4 exit criterion is then unsatisfiable on the reference circuit; (b) `out.t=50` is the
design point, released at t > 0. `33`'s example is (b); the binder and `WellPosedness` are (a).
Physically (b) is right: `out.t` on an exchanger names the flow the designer sized for, it is not a
thermostat; a thermostat is the `control` line.

**Correction.** A `D-`: *at t = 0 the steady solve runs exactly as static mode, with every stated
constraint and promotion. The promoted quantities (pump head or speed, valve position, every sized
value) are then frozen for the run — `33` invariant 4 extended from sizes to promotions. Stated
thermal constraints on non-boundary components (`out.t`, `in.t`, a `dt`) are released and become
initial conditions. Boundary states (`inlet t=`, `p=`, `flow=`) and `power=` remain constraints for
the whole run unless a schedule or controller moves them.* Write it in `33` under a new "What a
stated value means in a run" section; point `15` §omission policy and `22` §per-kind residuals at
it; each kind's residual table in `22` gains a "transient" column where it differs.

### F-3 · The t = 0 state and the setpoint disagree on the demand-step loop

**Evidence.** The cooling loop states `in.t=20` and the valve is sized to give it. The demand-step
loop (`01:481-488`) drops `in.t=20`, adds `control … measure=N2.t … setpoint=20`, and states no
`3WV.position`. `34:78-79` gives `Initialize(actuatorValue)` for a bumpless start.

**Why it blocks.** With `out.t=50`, `power=30`, no `in.t` and no valve position, the t = 0 system is
under-determined by one degree (the valve): either `WellPosedness` refuses the run (`FS22xx`), or the
valve takes a default and the run starts with `N2.t ≠ 20`, a non-zero error and a transient that is
the controller hunting for its design point rather than the demand step. Neither is what the
reference circuit means. Physically the design point of a controlled loop *is* its setpoint.

**Correction.** A `D-`: *a control binding whose actuator is unstated contributes its setpoint as a
t = 0 constraint on the measurement, promoting the actuator (`N2.t = 20` promotes `3WV.position`),
so the run starts at the controlled equilibrium and `Initialize` is bumpless by construction. When
the actuator is stated, the setpoint is not a t = 0 constraint; the run starts with an offset and
`FS32xx` (info) says by how much.* `WellPosedness` gains the control binding as a constraint source.

### F-4 · The reference circuit for every P6 example does not exist as a sample

**Evidence.** `D-16`, `01:230`, `05:334`, `62` all cite `samples/m4-demand-step.fluid`; `samples/`
holds `m4-storage-header.fluid` only. `01:477-505` holds the script text.

**Why it blocks.** Every P6 worked example, test and golden is derived from it, and nobody has
established that it compiles under the P5.13 spelling (`control` line, `pi` kind, `schedule`
section, `3WV - PB - N2` chain). Effort is tiny; the risk is that it does not bind today.

**Correction.** Create it from `01`'s block as P6.0's first act, run it through `compile` in static
mode (drop `dynamic`) and record its solved t = 0 state in `01` the way the other five have one.

### F-5 · Two different `IController` contracts carry one name

**Evidence.** `34:46-79`: `IController { Measurement, Actuator, Setpoint, Step(measured, dt),
Initialize(actuatorValue) }`. Built `Components/Observers.cs`: `IController : IObserver { AttachedNode,
ObservedProperties, Read(in NodeObservation), PropertyReference Actuator }`. The registry row for
`controller` (aliases `pi`/`pid`/`p`/`thermostat`) has `kp`/`ki`/`kd` as *Sized* and no `slew` or
`deadband`, which `34:140-141` defines with defaults.

**Why it blocks.** P6.3 cannot implement `34`'s interface without breaking the built observer, and
cannot implement the built one without losing `Step`/`Initialize`. "Sized" `kp`/`ki` is also a
category error: `34:173` computes them from a perturbation *at run start*, not from a sizing pass
that `24`'s fixed snapshot governs.

**Correction.** Keep the built observer as the read side. Add a tier-30 `ControlLaw` (PI/PID state:
`Step`, `Initialize`, range, slew, deadband, anti-windup) owned by the controller component; rewrite
`34`'s C# block to that shape. Add `slew` and `deadband` rows to the registry with `34`'s defaults.
Re-state `kp`/`ki`/`kd` basis as *estimated at run start* (`FS3201`), not *sized*.

### F-6 · The tank's transient residual set is never stated

**Evidence.** `22:763`: steady collapses every layer to one `h_tank`; built `Tank` has one
`EnthalpyIndex`. `33` integrates K layer states and defines inversion remix, but nowhere says that in
transient mode the single `h_tank` unknown is *not* allocated and the K−1 pressure equalities plus
mass balance stay algebraic while each port reads `LayerForPort`'s state.

**Why it blocks.** `SystemLayout` allocates `h_tank` unconditionally; a session either integrates a
state that has no equation or solves an equation whose unknown is now a state. Both fail V16.

**Correction.** In `33` §tank: algebraic = K−1 pressure equalities + Σṁ = 0 (unchanged);
differential = `h_1 … h_K`; port outflow enthalpy = the port's layer; inflow lands in the port's layer;
remix after each accepted step, smallest unstable adjacent block, enthalpy-preserving; `layers=1`
reduces to the mixed tank (V15). `22` §6 points here for transient.

### F-7 · `31`'s seam and `33`'s seam are two shapes under one plan

**Evidence.** `31:54-64` `ISolver.CanSolve(EquationSystem)` / `SolveAsync(system, guess, …)`;
`31:250` assigns "Time-domain, with Newton establishing t = 0" to `fluid dynamic`. `33:264-276`
`ITransientSolver.RunAsync(RunSnapshot) → IAsyncEnumerable<TransientFrame>`, not an `ISolver`.
`31`'s `CanSolve` doc says an explicit transient solver "refuses one whose stiffness exceeds its
step limit", which `CanSolve(EquationSystem)` cannot see (settings live in `RunSnapshot`).

**Correction.** State in both: `ITransientSolver` is not an `ISolver`; it *owns* one (Newton) and
calls `OuterLoop` once for t = 0 and `Newton` per step through the existing `Prepare`/`WarmStart`
seams. Drop the stiffness clause from `CanSolve`; it is `FS3102` at run time. Write the step
algorithm once, as pseudo-code, in `33` (F-17) and have `34` point at it.

## Should-fix findings

### F-8 · Frame delta schema and `stateChecksum` are undefined

`43:72-75` keys deltas as `HE1.tOut`, `3WV.position`, `N2.t/p` — none is a `ComponentStateWire`
field name, and no `frame.json` exists under `Contracts/Schemas` although `D-46` commits one per wire
type. `stateChecksum` says `sha256:…` with no statement of what bytes, in what order, in what number
format. Fix: deltas carry the model contract's own state field names and canonical units; the
checksum is SHA-256 over the canonical serialization of the **full reconstructed state** (fixed key
order, round-trip `R` doubles), so the worker can recompute it from `base` + deltas. Commit the schema.

### F-9 · The integrator must land on event times

`33:252-255` interpolates frames from bracketing steps. A step straddling `at 60 s` under Heun
applies the disturbance in one derivative evaluation and not the other; the 60 s frame then shows a
smeared jump and the "outlet jumps within one step" claim is false. Fix: clip every step to the next
scheduled time and the next frame time; state it as an invariant. (Controller sample time ≤
`frameInterval` already follows from `33:200`'s cap — say so in `34`.)

### F-10 · `FS3104` "settling" has no criterion

`33:332` and invariant 8 need a definition: settled when every differential state changes by less
than `transient.settle_tol` (relative, `36`) per `frameInterval` for `settle_frames` consecutive
frames. Put the two constants in `36`'s table.

### F-11 · The energy-drift accumulator has no numerator

`FS3106`/`FS3107` compare a "drift" to 1 % / 5 % with no definition. Fix: `E_stored = Σ_differential
Vρh`; `drift = |ΔE_stored − ∫(Σ Q̇_injected + Σ_boundary ṁh_in − Σ_boundary ṁh_out) dt| /
max(∫|Q̇|, E_stored(0))`. Say which flows are boundary (inlet/outlet attachments) and that
zero-volume nodes contribute nothing.

### F-12 · A file mixing `fluid water` and `fluid dynamic water` circuits is undefined

`Lowering.cs:107` marks the graph Transient when *any* circuit is dynamic. A static circuit coupled
to a dynamic one through a substation exchanger has no defined treatment. F-1's partition gives the
answer for free (a static circuit has no differential states and is solved algebraically each step);
write it, and give a `FS31xx` info naming the static circuits carried along.

### F-13 · A schedule and a controller on one actuator, and a schedule on a sized parameter

`at 60 s 3WV.position = 0.3` with `control actuate=3WV.position` is undefined; so is `at 60 s
PU1.head = …` when `head` is sized. Fix: bind-time error (`FS3105` family) for the first; for the
second, scheduling a parameter freezes it at its t = 0 solved value and then moves it (consistent
with F-2's freeze rule).

### F-14 · `RunSnapshot` has no owning document

`33` invariant 9 lists its contents; P6.4 builds it; `07`/`D-22` cite it; no document declares the
record, `SnapshotId`'s derivation, or who constructs it. Fix: declare it in `33` (Core type, next to
`TransientSettings`), `SnapshotId = sha256(sourceHash ‖ language/catalog/property/contract versions
‖ settings)`, constructed by the Api on `start` from the session's compiled model.

### F-15 · P6 needs a P6.0 before P6.1

P6.1 "transport delay and time integration on a fixed graph" assumes the mode-aware assembly (F-1),
the pinned solve, `RunSnapshot` (F-14), the t = 0 semantics (F-2, F-3) and a compiling reference
circuit (F-4). None exists. Add **P6.0 — Transient assembly on the existing system**: partitioned
`SystemLayout`, pinned `EquationSystem` view, `RunSnapshot`, `m4-demand-step.fluid` compiling, the
t = 0 solve under the new `D-`s, and V9 (rest) as its exit test. `08` is future tense; add the row
with the reason.

### F-16 · `05`'s transport criterion cites lengths the reference circuit does not have

`05:340`: "reaches a node 20 m downstream later than one 5 m downstream". The demand-step loop's
discretized pipe is 8 m in four 2 m cells (`33:346`). Fix: "`PB`'s outlet (8 m) later than its first
cell (2 m), by roughly length ÷ velocity".

### F-17 · `34`'s step order and `33`'s Heun disagree on how many algebraic solves a step takes

`34:184-191` lists "5. integrate energy states, 6. solve hydraulics" once. Heun evaluates
derivatives twice, each needing the algebraic state at its point, then the frame needs the algebraic
state at `x_{n+1}`: `k1 = f(x_n, alg(x_n))`, `x* = x_n + h·k1`, `k2 = f(x*, alg(x*))`,
`x_{n+1} = x_n + h/2·(k1 + k2)`, `alg(x_{n+1})` — two or three Newton solves per accepted step, warm
started. `33:118` already says "two Newton solves each". Write the algorithm once in `33`; `34` keeps
only "controller stepped before k1, once per accepted step".

## Nits

- `33` invariants: two items numbered 10.
- `43`: `frameContractVersion "1.0"` and the model contract's `2.2` are independent versions; say so.
- `43:47`: horizon and interval come from `start`, not the script — a stated exception to P5; record
  it as such (a `run` statement is a later language addition, not M4's).
- `07`'s 10 frames/s is a floor; `43`'s 600 frames in 4.2 s is an example. Not a contradiction.
- `42`'s `edit` operation list omits `applyTags`, which `17` defines (P7).

## P7 (M5) check

`17` is implementable: interface, formatting rules, the chained-connection case, `FS1601`–`FS1607`,
invariants. `54`'s loop and `42`'s `edit` endpoint agree. Nothing is built (no `IScriptEditor`, no
frontend `documentRevision`). Three small things to settle before P7.1, none blocking:

1. `42` operations gain `applyTags`.
2. `Rename` and `SetParameter` must accept post-`D-120` spellings (`in[2].t`) as the `parameter`
   string; state that the editor is AST-driven (works on a malformed script), and that `Rename` covers
   `control` lines, schedule targets, `show`, curve references and attachment endpoints.
3. `52` names the CodeMirror annotation `54` says the debounce listener skips.

## Coverage

| Document | Read | Findings |
|---|---|---|
| `33` | whole | F-1, F-2, F-6, F-7, F-9, F-10, F-11, F-12, F-14, F-17, nits |
| `34` | whole | F-3, F-5, F-17 |
| `31` | seam, table, invariants | F-7 |
| `43` | whole | F-8, nits |
| `07` | isolation, budgets | — |
| `05` M4 | whole | F-16 |
| `36` | transient rows | F-10 |
| `62` | V8–V23 | — |
| `12` schedule, `15` control, `22` tank | sections | F-6, F-13 |
| `01`/`D-16` demand-step | whole | F-3, F-4 |
| `08` P6/P7 | whole | F-15 |
| `17`, `54`, `42` edit | sections | P7 check |

**Missing operational contracts:** the pinned-system view (F-1); the run-time meaning of a stated
value (F-2); setpoint-as-design-point (F-3); the tank's transient residual set (F-6); the step
algorithm as one pseudo-code block (F-17); `RunSnapshot` and `SnapshotId` (F-14); the frame schema and
checksum bytes (F-8); settling and drift definitions (F-10, F-11).

**Open questions, grouped.** *Semantics needing a `D-`:* F-2, F-3, F-13. *Assembly:* F-1, F-6, F-7,
F-15. *Numerics:* F-9, F-10, F-11, F-17. *Wire:* F-8, F-14. *Housekeeping:* F-4, F-5, F-16, nits.

**Recommended phasing.** P6.0 (F-15) → P6.1 with F-9/F-10/F-11/F-17 written → P6.2 with F-6 → P6.3
with F-3/F-5/F-13 → P6.4–P6.7 unchanged, F-8/F-14 written before P6.5.

| Keep | Simplify | Defer | Delete |
|---|---|---|---|
| Quasi-static pressure, dynamic energy; Heun + CFL; controller outside derivative; deltas + checksum; detached runs | `34`'s step list → a pointer to `33`'s one algorithm; `31`'s `CanSolve` stiffness clause | Metal thermal mass (already M4+); ambient (`S-18`) | Nothing |
