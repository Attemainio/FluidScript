---
id: 22-component-model
title: Component model
tier: 20-core-domain
status: reviewed
owns: [component interface hierarchy, ports, the six v1 flow-component families, parameters, governing equations, parameter registry, per-kind tag codes]
depends_on: [13-type-and-unit-system, 15-semantic-model, 21-fluid-and-state]
traces_to: [R-02, R-09, R-10, R-16, R-35, R-37, R-43, R-45, R-47]
open_questions: 0
last_review_pass: 6
---

# Component model

## Purpose

The six v1 flow-component families (`R-09`), what parameters each accepts, and what equations each imposes on
the circuit. This is the document that turns "a pump" from a word into a set of residuals the solver
can drive to zero, and it is the registry the binder reads to know that `heat_exchanger` accepts
`power` but not `flow`.

## Responsibilities

**Owns.** `IComponent`, `Port`, the parameter and property registries for each kind, and each
component's governing equations.

**Explicitly does not own.** How components are wired ([`23-topology-and-graph`](23-topology-and-graph.md)),
how unspecified parameters get values ([`24-auto-sizing`](24-auto-sizing.md)), how the equations are
solved (tier 30), fluid properties ([`21-fluid-and-state`](21-fluid-and-state.md)).

## The abstraction

```csharp
/// <summary>A named model participant with declarative presentation metadata.</summary>
public interface IComponent
{
    /// <summary>The user's identifier, or the generated name of an inferred component.</summary>
    string Name { get; }

    /// <summary>Script keyword of this component's kind.</summary>
    string Kind { get; }

    /// <summary>Resolves to the Core-owned declarative symbol definition for this kind (`D-20`).</summary>
    SymbolId SymbolId { get; }

    /// <summary>Canonical component-specific mode, or null for a kind with no modes.</summary>
    /// <remarks>A heat exchanger exposes duty, rated, or coupled as inferred by D-19.</remarks>
    string? Mode { get; }

    /// <summary>Parameters the user stated. Absence means unresolved, not null.</summary>
    /// <remarks>The registry's omission behavior selects sizing or a visible default (`D-02`, `D-32`).</remarks>
    IReadOnlyDictionary<string, Quantity> StatedParameters { get; }

    /// <summary>Parameters resolved by sizing, keyed the same way.</summary>
    IReadOnlyDictionary<string, Quantity> SizedParameters { get; }

    /// <summary>Explicit component defaults used because neither source nor sizing resolved them.</summary>
    /// <remarks>Always reported with a basis; includes the tank defaults fixed by D-32.</remarks>
    IReadOnlyDictionary<string, Quantity> DefaultParameters { get; }
}

/// <summary>A component that participates in a fluid graph and its equation system.</summary>
public interface IFlowComponent : IComponent
{

    /// <summary>Ports, in declaration order. Unqualified connections bind in this order.</summary>
    IReadOnlyList<Port> Ports { get; }

    /// <summary>Which flow group each port belongs to, indexed as <see cref="Ports"/> is (D-63).</summary>
    /// <remarks>
    /// Ports sharing a group must carry the same mass flow. A component is a junction element when
    /// one group holds more than two ports — never when its port count does. A coupled exchanger is
    /// [0,0,1,1], two groups of two; a three-way valve is [0,0,0]; a tank is one group of every
    /// materialized port. See <see href="23-topology-and-graph.md"/>.
    /// </remarks>
    IReadOnlyList<int> FlowGroups { get; }

    /// <summary>Declares the unknowns this component adds to the system.</summary>
    /// <returns>One entry per scalar the solver may vary. Empty for a component that only
    /// constrains state its ports already carry.</returns>
    IReadOnlyList<UnknownDeclaration> DeclareUnknowns();

    /// <summary>Evaluates this component's residuals at the given trial solution.</summary>
    /// <param name="context">Port states, flows, and the substance, at the current iterate.</param>
    /// <param name="residuals">Destination span, exactly <see cref="EquationCount"/> long.</param>
    /// <remarks>
    /// Called inside the solver's iteration — a hot path. Must not allocate, must not call the
    /// property backend more than necessary, and must be deterministic: the same context always
    /// produces the same residuals.
    /// </remarks>
    void EvaluateResiduals(in SolveContext context, Span<double> residuals);

    /// <summary>How many equations this component contributes.</summary>
    int EquationCount { get; }
}

/// <summary>A node's solved state, as an instrument attached to it sees it.</summary>
/// <remarks>Deliberately narrower than `SolveContext`: a sensor has no ports, so port states and
/// branch flows are not its business.</remarks>
public readonly record struct NodeObservation
{
    public required FluidState State { get; init; }
    /// <summary>kg/s, the sum of the flows *entering* the node — see "What a flow sensor reads".</summary>
    public required Quantity MassFlow { get; init; }
}

/// <summary>A non-flow model participant that reads state and contributes no equations.</summary>
public interface IObserver : IComponent
{
    /// <summary>The node the `at` clause placed this instrument on.</summary>
    string AttachedNode { get; }

    /// <summary>What this observer reads, naming the NODE rather than the instrument (`D-61`).</summary>
    ImmutableArray<PropertyReference> ObservedProperties { get; }

    /// <summary>Reads the measurement from a node's solved state.</summary>
    /// <remarks>A projection, not a computation: a sensor is not a filter, a lag, or a source of
    /// error. Declaring what is observed without being able to read it would leave the family a data
    /// bag, and the reading is the half of `D-61` that is physics rather than language.</remarks>
    Quantity Read(in NodeObservation observation);
}

/// <summary>An observer that also drives one actuator during a transient.</summary>
public interface IController : IObserver
{
    PropertyReference Actuator { get; }
}

/// <summary>An attachment point carrying a fluid state and a flow.</summary>
public sealed record Port
{
    public required string Name { get; init; }
    public required PortRole Role { get; init; }          // Inlet | Outlet | Bidirectional
    public required bool IsOptional { get; init; }        // whether inference rule I3 skips it
    /// <summary>Normalized bottom-to-top height for a tank port; null for other components.</summary>
    public double? NormalizedElevation { get; init; }
}
```

**`D-23` deferred persistent sensor components and `D-61` reversed it**: `t_sensor`, `p_sensor` and
`flow_sensor` are real components in v1, and a controller reads one rather than reading a node's
solved state directly (§7). What `D-23` still governs is instrument *dynamics* — a lag, an offset, an
error band — which remain post-v1. The symbol schema becomes a delivery gate with the M3 renderer
under `D-24`, not with M2 physics.

**A four-port exchanger declares two groups whatever its mode.** [`23`](23-topology-and-graph.md)
tabulates duty mode as one group of two, because side 2 does not exist there — but the difference is
in what is *connected*, not in how the ports partition, and a component that had to know its own mode
to answer would need lowering to tell it. `[0,0,1,1]` always, and connectivity decides (`D-63`).

**`IFlowComponent` and `IObserver` do not arrive together.** The observer half is built first
(`P3.0`), before the six flow kinds, because a seventh family added afterwards is six rewrites and one
addition before them is none. `IObserver` is therefore stated against types that already exist, and
`IFlowComponent` waits for tier 30 to fix the shape of `SolveContext` and `UnknownDeclaration`.

**`EvaluateResiduals` writes into a caller-owned span and returns nothing.** The solver assembles one
residual vector for the whole system; per-component allocation inside a Newton iteration is the
performance mistake this signature exists to prevent (`performance.md`; the iteration is a hot path by
definition).

**Analytic Jacobians are deliberately absent from the interface.** v1 uses numerical
differentiation ([`32-steady-state-newton`](../30-solver/32-steady-state-newton.md)). Adding an
optional `EvaluateJacobian` later is a non-breaking addition; requiring it now would triple the work
per component for a speedup nobody has measured a need for.

## Conventions that apply to every component

1. **Sign.** Pressure drop is positive when pressure falls in the nominal flow direction (rule I4).
   A pump reports a negative drop. Summing drops around a closed loop is then zero, and no component
   needs a special case.
2. **Flow.** Mass flow is positive in the nominal direction. Negative solved flow is legal and produces
   `FS4009` (info), not an error — reverse flow in a bypass is a real answer, not a mistake.
3. **State pairing.** Components consume and produce (p, h). Temperature is derived. This is
   [`21-fluid-and-state`](21-fluid-and-state.md)'s reasoning applied consistently: an energy balance
   produces enthalpy, and going via temperature requires inverting cp.
4. **Every parameter is optional** (`D-02`). The registry's `OmissionBehavior` decides what absence
   means: normally sizing, or an explicit visible default backed by a binding decision. The tank's
   `volume=300 dm3`, `layers=5`, and mid-height port elevations are explicit visible defaults because
   the graph cannot infer them (`D-32`).
5. **Every parameter declares a dimension, a plausible range (for `FS1306`), and a display
   precision** (for write-back formatting). The tables below carry the first two; **the display
   precision lives only in the registry**, because it is a formatting decision with no bearing on the
   physics and a column of it here would be a second place to keep in step. `ParameterInfo.DisplayPrecision`
   is the authority. It does **not** declare a unit: the canonical unit
   follows the dimension (`D-14`, [`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md)).
   The "bare number means" column below is copied from there for reading convenience and is never the
   authority — a table here disagreeing with `13` is a review finding, and three of them did.
   **Ranges are written in that column's unit, not in SI**: a temperature range of −50 … 300 is °C, and
   the registry converts it when it is built. Transcribing these as SI numbers by hand is how −50 °C
   becomes −50 K and every plausible temperature falls outside its own range.

---

## 1 · `node` / `supply` / `return`

The primitive. A point in the circuit with a state and no extent.

**Ports:** unlimited, unnamed, bidirectional. A node is the only component that accepts an arbitrary
number of connections — that is what makes it the junction.

| Parameter | Dimension | Bare number means | Range | Meaning |
|---|---|---|---|---|
| `t` | Temperature | °C | −50 … 300 | Fixes the temperature — a boundary condition |
| `p` | Pressure | kPa | 0 … 2500 | A pressure boundary condition; the first one also supplies the circuit's datum |
| `flow` | MassFlow | kg/s | 0 … 1000 | Terminal-flow magnitude. Positive follows the nominal connection: an upstream terminal injects and a downstream terminal extracts. |

**Properties:** `t`, `p`, `h`, `flow`, `rho`.

**Equations.** A node contributes **one energy balance always, and one mass balance only when it is a
junction element or a terminal** — both written over *every* attached port with signed flows,
`ṁᵢ > 0` into the node:

```
Σᵢ ṁᵢ            = 0        junction elements and terminals only
Σᵢ ṁᵢ · h(ṁᵢ)    = 0        every node;  h(ṁᵢ) = h_upstream for an inflow, h_node for an outflow
```

**The energy count is unconditional, and that is the point.** The tempting formulation — an energy
balance only where three or more ports carry flow — makes a node's equation count depend on the
*solved* flow direction, and convention 2 makes reverse flow legal. The system's size would then
change between Newton iterations, breaking `EquationCount` and invariant 4. Writing the balance over
all ports with signed flows handles mixing, straight-through, and reversal with one equation and a
fixed size.

**The mass count is conditional, and that is not an inconsistency — it is what branch-owned flows
mean.** A degree-two node interior to a branch has one flow in and the same flow out, because the
branch owns a single flow unknown for every component along it
([`23-topology-and-graph`](23-topology-and-graph.md)). Its mass balance is then `ṁ − ṁ = 0`,
identically zero for every iterate: a row of zeros in the Jacobian, and a system that is singular by
construction rather than by any user error. Almost every node in a real circuit is interior to a
branch, so this is the normal shape and not an edge case.

The condition is **structural, not numerical** — it depends on how many connections a node has, which
is fixed at lowering and never changes during a solve. `EquationCount` is therefore still constant
across Newton iterations, which is the property invariant 4 actually protects.

[`23-topology-and-graph`](23-topology-and-graph.md) owns the junction-element definition and the
counting scheme; this document owns only the statement that a node's mass balance follows it.

`h(ṁᵢ)` is upwinded, which is a discontinuity at `ṁᵢ = 0`. It is smoothed over the same zero-flow band
the valve law uses, for the same reason
([`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md) owns the band).

**Every state in the circuit lives on a node.** Pipes, valves, and pumps have states at their ports,
but those ports are attached to nodes. This is why inference rule I2 exists: two components connected
directly have no state between them to write an equation about.

### A boundary is a node that states what a boundary needs

`node` says nothing about intent. A terminal one with no parameters is legal and means **zero flow** —
a dead leg — which is the right reading for a stub someone has not finished wiring and the wrong one
for the inlet of an open circuit. Nothing in the script distinguishes them, and the consequence is not
an error message: it is a solved circuit carrying no flow, with plausible temperatures everywhere.

Two kinds carry that intent (`D-64`). Both resolve to nodes and share the table above.

| Kind | Requires | Its external mass flux |
|---|---|---|
| `supply` | `t`, and **exactly one** of `flow` or `p` | Known when `flow` is stated; unknown when `p` is |
| `return` | nothing | **Unknown** — mass leaves here |
| `node` | nothing | Unknown only where `p` is stated; otherwise zero |

**`supply` needs one thermal and one hydraulic condition, and that is what an inlet condition is.** A
pumped feed states `flow` and its pressure is solved; a district-heating connection states `p` and its
flow is solved. Stating both over-specifies the boundary (`FS2101`) and stating neither leaves it
undetermined (`FS2118`) — two errors that were previously one silent wrong answer.

**`return` requires nothing and is still not a `node`.** What it carries is the one thing no parameter
can: *mass leaves here*. That is what gives its mass balance an unknown external flux instead of a
zero-flow closure, and it is what lets a circuit that fills up with no way out be reported rather than
solved ([`23-topology-and-graph`](23-topology-and-graph.md)'s `FS2204`). A `return` may still state
`p`, which makes it a pressure boundary as well.

**A closed circuit needs neither**, which is why `node` keeps every meaning it had. The syntax
reference, the cooling loop's secondary and the simple loop declare no boundary at all and are right
not to.

**Every connected circuit needs a pressure datum**, or only pressure *differences* are determined and
the solver has a singular Jacobian. The first stated `p` supplies it; if the user states none,
[`23-topology-and-graph`](23-topology-and-graph.md) picks a node and says so. Stating *several* `p`
values is normal and not an error — an open primary side with a supply and a return needs exactly that,
and each one admits an unknown external flux. The datum and a boundary condition are different things
([`02-glossary`](../00-foundation/02-glossary.md)); conflating them makes every open circuit look
over-specified.

---

## 2 · `pipe`

A pressure drop between two nodes, optionally discretized (`R-10`).

**Ports:** `in`, `out`. Neither optional.

| Parameter | Dimension | Bare number means | Range | Omission behavior | Meaning |
|---|---|---|---|---|---|
| `length` | Length | m | 0.01 … 10000 m | Size | `length=45` is 45 metres (`D-14`) |
| `dn` | **`NominalDiameter`** | — | 6 … 2000 | Size | Nominal-diameter **designation**, dimensionless. |
| `roughness` | Length | m | 1 µm … 5 mm | Default 0.045 mm | Written `roughness=0.045 mm` — a bare number here is metres, like every other `Length` |
| `nodes` | Dimensionless | — | 0 … 100 | Size | Internal discretization count (`R-10`): omission sizes to 0 because no transport resolution can be inferred from topology; transient storage is opt-in with explicit `nodes>=1`. |
| `insulation` | Dimensionless | — | — | Size (unavailable in v1) | Reserved; heat loss is post-v1. |
| `elevation` | Length | m | −500 … 500 m | Size | Outlet minus inlet height; absent geometry resolves to 0 m with basis “no elevation stated”. |
| `minor_loss` | Dimensionless | — | 0 … 10000 | Default 0 | Sum of explicit fitting/local-loss coefficients K. |

**Properties:** `dp`, `velocity`, `re`, `dn`, `diameter`, `flow`, `volume`.

`dn` reads back the designation; **`diameter` reads back the catalogue inside diameter in metres**,
and it is the one an expression should use for anything dimensional
([`14-expressions-and-references`](../10-language/14-expressions-and-references.md) references
`P1.diameter`). Exposing both, with different kinds, is what stops the two being confused in a script
the same way the parameter table stops it in code.

**Equations.** One momentum equation:

```
Δp = (f · L/D + minor_loss) · (ρ v²/2)  +  ρ g Δz
```

with the Darcy friction factor `f` from Colebrook–White, solved by the Serghide explicit
approximation — accurate to 0.003 % against the implicit form and, being explicit, differentiable and
allocation-free inside the solver's iteration. Below Re = 2300, `f = 64/Re`. Between 2300 and 4000 the
correlation is interpolated rather than switched, because a discontinuity in the residual is a
discontinuity in the Jacobian and Newton does not survive one.

`minor_loss` is the explicit sum of fitting coefficients at the design represented by this pipe. It
does not invent elbows from the diagram: auto-layout geometry is not physical routing. When omitted,
the value is zero and the sizing basis states “no minor losses modelled”. Manufacturer-specific valves
and strainers remain explicit components or later catalogue work; v1 does not hide them in a default.

**`dn` is a designation, not a length**, and it carries its own dimensionless `NominalDiameter` kind
for that reason ([`02-glossary`](../00-foundation/02-glossary.md)). DN25 steel pipe has a 27.3 mm bore,
not a 25 mm one. Typing `dn` as a `Length` in millimetres is the mistake this row exists to prevent:
it makes `dn=25` compute an area from 25 mm, a 16 % area error and roughly a factor of two in pressure
gradient, and nothing in the result looks wrong. **Every hydraulic calculation reads the catalogue's
`InsideDiameter`** ([`27-component-catalog`](27-component-catalog.md)) — never the DN number, and never
a diameter derived from it.

**`nodes=n` defines two related meshes.** It inserts n internal thermodynamic nodes and lowers the
hydraulic path into n+1 equal-length sub-pipes, each carrying `length/(n+1)` and `1/(n+1)` of the total
pressure drop at a common flow. Independently, the n internal nodes are n equal-volume pipe cells:
each owns `pipe volume/n`, equivalent to `length/n`, so their volumes sum to the declared pipe volume.
The endpoint graph nodes own no part of the pipe volume. `nodes=0` is the steady/lumped-no-storage
form: one hydraulic pipe, no internal thermal state, and therefore no transport-delay claim. A
transient requiring pipe storage must use `nodes>=1`.

Thus `node_in → node_1 → node_2 → node_out` is `nodes=2`: two pipe cells and three hydraulic
sub-pipes. Internal nodes are named `{pipe}__1`, `{pipe}__2`, are visible in hover, and make a
temperature front observable (`R-14`). The unequal mesh counts are deliberate: internal nodes own
state; sub-pipes distribute pressure drop. Calling both a "segment" is forbidden because it hides
which count and length apply.

**Where a `pipe` component is not written.** The brief's example has no `pipe` declarations — a
connection `N1 - N2` is a bare edge. [`23-topology-and-graph`](23-topology-and-graph.md) decides
whether such an edge is a zero-drop link or an implicit pipe; `D-25` fixes it as an ideal zero-loss
connection because auto-layout geometry is not physical routing.

---

## 3 · `heat_exchanger`

Heat source, heat consumer, or a real two-sided exchanger (`R-09` item 3, `R-35`). One kind covers all
three; a negative `power` is a consumer. Secondary properties promote it to Rated mode; secondary
connections promote it to Coupled mode (`D-19`, which amends `D-17`).

**Ports:** `in`, `out` (side 1) and optional `in2`, `out2` (side 2). Lowering computes exactly one
mode; there is no script `mode=` parameter:

| Mode | Trigger, in precedence order | Flow groups | Behaviour |
|---|---|---|---|
| **Coupled** | Either secondary port is connected; both must be connected or `FS2112` | `{in,out}` and `{in2,out2}` | Two solved hydraulic streams. ε-NTU couples their energy equations; each side has its own momentum relation. |
| **Rated** | No secondary connections and at least one of `in2`, `out2`, `dt2`, `flow2` is stated | `{in,out}` only | Side 2 is an external stated/sized boundary profile, not a graph branch. ε-NTU and approach/geometry are live; no side-2 pressure equation is assembled. |
| **Duty** | No secondary connection or thermal-profile property | `{in,out}` only | A stated duty crosses the model boundary. Rating parameters are inert with `FS2110`; no area, effectiveness, or approach claim is made. |

Coupled wins over Rated so adding real connections to an external-profile design has one predictable
meaning. `dp2` and rating-only parameters (`ua`, `area`, `u`, `approach`, arrangement, plate geometry)
do not promote Duty mode: none supplies the second inlet temperature or capacity rate that ε-NTU
needs. Inference rule I3 skips optional secondary ports, so Duty and Rated declarations are complete
without fabricated nodes. “Extended exchanger” means Rated or Coupled collectively.

**Side 1 is the side the unsuffixed parameters describe.** `power`, `in`, `out`, `dt`, `flow`, `dp`
belong to side 1; `in2`, `out2`, `dt2`, `flow2`, `dp2` to side 2. `power` is the duty *transferred*,
positive when side 1 gains heat. Numbering rather than naming the sides (`hot`/`cold`, `primary`/
`secondary`) is deliberate: which side is hot is a solved outcome, not a declaration, and a script
that says `hot_in=40` when the solve makes it the cold side is worse than one that says nothing.

### Parameters

| Parameter | Dimension | Bare number means | Range | Meaning |
|---|---|---|---|---|
| `power` | Power | kW | −100000 … 100000 | Duty transferred. Positive adds heat to side 1. |
| `in`, `out` | Temperature | °C | −50 … 300 | Side-1 inlet / outlet temperature |
| `in2`, `out2` | Temperature | °C | −50 … 300 | Side-2 inlet / outlet temperature |
| `dt`, `dt2` | TemperatureDelta | dK | 0.1 … 200 | Temperature change across that side. Always positive; the sign follows `power` |
| `dp`, `dp2` | PressureDelta | kPa | 0 … 1000 | Pressure drop at design flow, per side |
| `flow`, `flow2` | MassFlow | kg/s | 0 … 1000 | Flow constraint, per side |
| `ua` | — (W/K) | W/K | 1 … 1e7 | Overall conductance. The thermal size, independent of how it is achieved |
| `area` | Area | m² | 1e-3 … 1e4 | Heat transfer area |
| `u` | — (W/(m²·K)) | W/(m²·K) | 10 … 20000 | Overall heat transfer coefficient |
| `approach` | TemperatureDelta | dK | 0.1 … 100 | **Minimum** temperature difference the design must respect |
| `arrangement` | *symbol* | — | — | `counter` (default) · `parallel` · `crossflow` |
| `plates` | Dimensionless | — | 3 … 800 | Total plate count. Effective plates are `plates − 2` |
| `lamella` | Length | m | 1 mm … 20 mm | Lamella between adjacent plates. Written `lamella=2.4 mm` |
| `plate_area` | Area | m² | 1e-3 … 5 | Effective heat transfer area of one plate |
| `fouling` | — (m²·K/W) | m²·K/W | 0 … 1e-2 | Combined fouling resistance. Default 1e-5 |

**Properties:** `power`, `ua`, `area`, `u`, `ntu`, `effectiveness`, `lmtd`, `approach`, `plates`,
`dp`, `dp2`, `dt`, `dt2`, `flow`, `flow2`, `t_in`, `t_out`, `t_in2`, `t_out2`.

`ua`, `area` and `u` are related by `UA = U·A`, so **any two fix the third** and stating all three is
`FS2101`, exactly as `power`/`in`/`out`/`flow` already are. Geometry (`plates`, `lamella`,
`plate_area`) is a fourth route to the same pair: it derives both `area` and `u`.

### Equations — ε-NTU, not LMTD

In Duty mode, side 1 contributes its energy relation plus its momentum relation:

```
Q̇   = ṁ (h_out − h_in)
Δp  = dp_design · (ṁ/ṁ_design)²
```

In both extended modes, the duty is no longer free — it is what the exchanger can transfer:

```
C₁   = ṁ₁ cp₁            C₂ = ṁ₂ cp₂
Cmin = min(C₁, C₂)       Cr = Cmin / Cmax
NTU  = UA / Cmin
Q̇    = ε(NTU, Cr, arrangement) · Cmin · (T_hot,in − T_cold,in)
```

with, for the three arrangements:

| Arrangement | ε |
|---|---|
| `counter`, Cr < 1 | `(1 − e^(−NTU(1−Cr))) / (1 − Cr·e^(−NTU(1−Cr)))` |
| `counter`, Cr → 1 | `NTU / (1 + NTU)` — a **removable** singularity, blended |
| `parallel` | `(1 − e^(−NTU(1+Cr))) / (1 + Cr)` |
| `crossflow`, both unmixed | `1 − exp( (NTU^0.22 / Cr)·(e^(−Cr·NTU^0.78) − 1) )` |

**LMTD is a reported property, never a residual, and that is the load-bearing choice.** The obvious
formulation `Q̇ = U·A·F·ΔT_lm` needs all four terminal temperatures and divides by `ln(ΔT₁/ΔT₂)`,
which is singular when the two end differences are equal — precisely the balanced-counterflow case
`Cr = 1`. That is not a pathological input; it is where a great many exchangers are deliberately
designed. A residual that divides by zero at a common design point cannot be handed to Newton.

ε-NTU needs only the two **inlet** temperatures, is smooth in NTU everywhere, and its own `Cr = 1`
singularity is removable and blended over `|1 − Cr| < 1e-3` with the smoothstep
[`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md) owns — the same treatment
the valve law gets, for the same reason. LMTD is then computed *after* the solve, from the terminal
temperatures, and reported; for counterflow the two agree to rounding, and a test asserting that
agreement is the strongest available check on a sign or a `Cmin` error.

In Rated mode the secondary inlet state/capacity rate comes from its boundary profile and its outlet is
derived by the energy balance; missing profile quantities may be sized only when the remaining
quantities and side-1 duty determine them. In Coupled mode both sides come from graph state. In either
case, **`Cmin` must be recomputed each iteration.** Which side is `Cmin` can switch during a solve — a
substation at part load does exactly this. Caching it at assembly silently changes the residual.

### Geometry — where `lamella` enters

Given plate geometry, `area` and `u` are derived rather than stated:

```
area      = (plates − 2) · plate_area
channels  = plates − 1, divided between the sides
Dh        = 2 · lamella                      (a wide, shallow channel)
Re        = ρ v Dh / μ,  v from the per-channel flow and lamella × plate width
Nu        = C · Re^m · Pr^(1/3)              (chevron correlation, C and m per plate model)
h         = Nu · λ / Dh
1/U       = 1/h₁ + t_plate/λ_plate + 1/h₂ + fouling
```

`plates − 2` because the two end plates have fluid on one side only and transfer nothing — a 2-plate
error on a 39-plate unit is 5 % of the area, and it is the mistake every first implementation makes.

**`lamella` earns its place because it is the number on the datasheet.** `ua` is not something a
designer has; a plate model, a plate count, and a lamella are. Halving the lamella doubles the velocity
and raises `h` by roughly `Re^0.663` — about 58 % — while quadrupling the pressure drop, which is the
actual trade a selection makes. The correlation constants `C` and `m` are per plate model and live in
[`27-component-catalog`](27-component-catalog.md) with their provenance; this document owns only the
shape of the calculation.

**Geometry makes `U` and `plates` a fixed point**, since the plate count sets the channel count, which
sets the velocity, which sets `h`, which sets `U`, which sets the area required. It converges from
above — a larger guess gives lower velocities, lower `U`, and therefore a larger area, so the sequence
is monotone — and it is resolved by [`31-solver-architecture`](../30-solver/31-solver-architecture.md)'s
existing outer loop rather than a nested one.

### The approach, and what "pinch" does and does not mean here

The **approach** is the minimum temperature difference between the two streams. For counterflow it
occurs at one end — the end where the `Cmin` stream is — so it is `min(T_h,in − T_c,out,
T_h,out − T_c,in)`. For crossflow it can occur inside the exchanger, and the correlation above does not
resolve where; v1 reports the terminal minimum and says so.

Approach → 0 is `ε` → its arrangement maximum and `NTU` → ∞, so **the approach constraint is what stops
sizing from returning an infinite exchanger**. A stated `approach` is a constraint like any other
(`D-02`): the sizer must meet it or report `FS4008`, which is now live rather than reserved.

**This is not pinch analysis.** Pinch analysis is a *network* method — composite curves, a plant-wide
ΔT_min, the grand composite curve, heat-recovery targets across many streams — and it answers "which
streams should exchange with which", a question that presupposes several exchangers that do not yet
exist. What this component does is enforce a minimum approach on **one** two-stream exchanger. The two
share a word and almost nothing else. Network pinch analysis is on
[`72-roadmap`](../70-future/72-roadmap.md), not in v1.

### The over-determination traps, now three

`power`, `in`, `out`, and `flow` are related by side 1's energy balance: any three fix the fourth, and
stating all four is `FS2101` reporting the value the other three imply. Side 2 has the same trap with
`power`, `in2`, `out2`, `flow2`.

The third is new and is the one that will bite. In an extended mode, **the four terminal temperatures, the
duty, and the thermal size are not independent** — ε-NTU relates them. Stating all four temperatures,
`power`, *and* `ua` (or `area`+`u`, or geometry) is over-determined by one: `FS2109`, naming the size
the temperatures imply. The normal script states the temperatures and the duty and leaves the size to
be sized, which is `D-02` working exactly as intended.

**`dt` is a magnitude; `power` carries the sign.** `RAD1 heat_exchanger power=-70 dt=20` is a
consumer dropping 20 K, and `BLR heat_exchanger power=150 dt=20` is a source raising it by 20 K. The
range excludes zero and negatives deliberately: `dt=-20` on a consumer would mean the same thing twice
and `dt=-20` on a source would contradict `power`, so neither is accepted (`FS1307`).

## 4 · `valve` / `three_way_valve`

Two kinds sharing an equation.

**`valve` ports:** `in`, `out`.
**`three_way_valve` ports:** `a` (common), `b` (controlled), `c` (bypass). All three are
**bidirectional**; `c` is optional — a three-way valve used as a two-way leaves it open, and inference
rule I3 terminates it.

**All three ports are bidirectional because both arrangements are real and the model must carry
both.** The ports were previously typed inlet / outlet / outlet, which describes a **diverting** valve
— one stream in at `a`, split between `b` and `c` — and that is what the cooling loop uses. But the
commonest three-way valve in hydronics is a **mixing** valve: two streams in at `b` and `c`, one out
at `a`, which is how every weather-compensated heating circuit is built. Fixed port roles made that
arrangement expressible only by relying on reverse flow being legal, which left `PortRole` wrong, the
canvas arrows wrong, and `FS4009` firing on a correct design.

The equations do not change: `ṁ_a = ṁ_b + ṁ_c` with signed flows covers both, and convention 2 already
makes a negative solved flow a legal answer. What changes is that **the arrangement is read from the
topology, not declared**: the port whose connections carry flow toward the valve at the design point
is its inlet, and `25-layout-hints` reports which so the renderer draws the arrows. A valve whose
solved flows contradict the nominal direction written in `connections` still produces `FS4009` — that
is a real finding — but a mixing valve wired as a mixing valve does not.

`position` means the same in both: **1 is fully open between `a` and `b`**, whichever way the fluid
moves through them.

| Parameter | Dimension | Bare number means | Range | Meaning |
|---|---|---|---|---|
| `kv` | Kv | m³/h @1 bar | 0.01 … 10000 | Flow coefficient |
| `position` | Dimensionless | — | 0 … 1 | Opening. 1 = fully open to `b`. |
| `characteristic` | — | — | — | `linear` \| `equal_percentage` \| `quick_open` |
| `authority` | Dimensionless | — | 0 … 1 | Target authority for sizing |
| `dp` | PressureDelta | kPa | 0 … 2500 | Design pressure drop, an alternative to `kv` |

**Properties:** `kv`, `dp`, `position`, `authority`, `flow`.

**Equations.** The Kv relation, **in the units Kv is defined in**:

```
Q [m³/h] = Kv_eff · √( Δp [bar] / (ρ/ρ_water) )          Kv_eff = Kv · φ(position)
```

with the characteristic φ: linear `φ = x`; equal-percentage `φ = R^(x−1)` with rangeability R = 50;
quick-open `φ = √x`.

**Stating the unit basis is not pedantry here.** `Kv` is defined as m³/h of water at 1 bar differential
([`02-glossary`](../00-foundation/02-glossary.md)), so the relation above is only true with Δp in bar
and Q in m³/h. Everything else in Core is SI, so the residual must use the converted form

```
ṁ [kg/s] = (ρ / 3600) · Kv_eff · √( Δp[Pa] / (10⁵ · ρ/ρ_water) )
```

An implementer who substitutes pascals into the first form is wrong by √10⁵ ≈ 316 — a plausible-looking
flow that is two and a half orders of magnitude out. Both forms belong in the XML docs of the member
that evaluates them.

A three-way valve splits: port `a`'s flow divides between `b` and `c` per position, with the `c` path's
effective Kv following the complementary characteristic. Mass balance across the valve —
`ṁ_a = ṁ_b + ṁ_c` — is the valve's own equation, not a node's, because the valve is the only element
in the graph where a flow divides without a node
([`23-topology-and-graph`](23-topology-and-graph.md)).

**`√` in the residual is a problem for Newton**: the derivative is infinite at Δp = 0, which is exactly
where a closed valve sits. The equation is therefore regularised below a small Δp threshold, matched in
**both value and slope** at the join.
[`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md) owns the threshold and the
blend; this document owns the requirement that one exist and that it be C¹. Note that a straight line
*through the origin* cannot satisfy both conditions — it matches value and gets the slope wrong by
exactly a factor of two — so the blend is necessarily curved. A valve implemented without any
regularisation will fail to converge on the first circuit that closes one, and the failure will look
like a solver bug.

---

## 5 · `pump`

**Ports:** `in`, `out`.

| Parameter | Dimension | Bare number means | Range | Meaning |
|---|---|---|---|---|
| `head` | Head | m | 0.1 … 500 | Head at duty point |
| `dp` | PressureDelta | kPa | 1 … 5000 | Pressure rise, an alternative to `head` |
| `flow` | MassFlow | kg/s | 0 … 1000 | Duty flow |
| `speed` | Dimensionless | — | 0 … 1.2 | Relative speed, for variable-speed control |
| `efficiency` | Dimensionless | — | 0.1 … 0.95 | Hydraulic efficiency; default 0.7 |
| `margin` | Dimensionless | — | 1 … 2 | Explicit head multiplier used only when auto-sizing; default 1.0 |
| `curve` | — | — | — | Named curve, or absent for the default quadratic |

**Properties:** `head`, `dp`, `flow`, `power`, `speed`, `efficiency`.

**Equations.** The pump curve, as a residual:

```
Δp = −ρ g H(ṁ, n)          H(ṁ, n) = n² H₀ − k ṁ²
```

Negative, per the sign convention. `H₀` and `k` describe the curve **at n = 1**, and come from the
curve or from the sized duty point when no curve is given.

**The `n²` distributes over both terms, and that is the whole content of the affinity laws here.**
A point (Q, H) on the base curve maps to (nQ, n²H) at relative speed n, so the head at flow ṁ and
speed n is `n²·[H₀ − k(ṁ/n)²]`, which simplifies to `n² H₀ − k ṁ²`. Writing it unsimplified as
`n² H₀ − k (ṁ/n)²` is the error to avoid: it leaves an extra `1/n²` on the loss term — a factor of
four at half speed — and it is **silent at n = 1**, which is where every test gets written. The
simplified form is also finite at n = 0, where the unsimplified one divides by zero; a stopped pump
must evaluate to a pure resistance `−ρ g (−k ṁ²)`, not to a non-finite residual (invariant 7).

**Shaft power** `P = ṁ |Δp| / (ρ η)` is a property, not an equation — nothing else depends on it, so
it is computed after the solve rather than carried as an unknown. The absolute value is deliberate:
`Δp` is negative for a pump by convention 1, and shaft power is positive.

**The default curve is a modelling decision with real consequences.** A pump given only a duty point
gets a quadratic through it with a shut-off head of 1.2 × duty head, which is typical for a centrifugal
pump and wrong for anything else. It must be stated in `/docs` and reported in hover, because a user
comparing against a datasheet curve needs to know what they are being shown.

---

## 6 · `tank`

A finite-volume liquid store. `container` resolves to this canonical kind, and parameter alias `v`
resolves to `volume`; neither alias is emitted (`D-32`). The tank is a mixed junction in steady state
and a stack of equal-volume, perfectly mixed layers in a transient.

**Ports:** indexed `in1`…`in16` and `out1`…`out16`, all bidirectional at solve time. `in1` and `out1`
always exist. Higher ports materialize only when named by a qualified connection or an elevation
parameter. With several ports, qualified endpoints are the canonical authoring form. Actual solved
flow sign decides whether fluid enters or leaves; an `in` port with reverse flow draws from its layer.

| Parameter pattern | Dimension | Bare number means | Range/default | Meaning |
|---|---|---|---|---|
| `volume` (`v` alias) | Volume | dm³ | 1 … 1e7; **default 300** | Total liquid volume |
| `layers` | Dimensionless integer | — | 1 … 100; **default 5** | Equal-volume layers, indexed bottom to top |
| `t` | Temperature | °C | −50 … 300 | Uniform initial temperature for every layer |
| `t1`…`tN` | Temperature | °C | −50 … 300 | Complete bottom-to-top initial profile; N equals `layers` |
| `in1_elevation`…`in16_elevation` | Dimensionless | — | 0 … 1; **default 0.5** | Normalized inlet height, bottom 0 and top 1 |
| `out1_elevation`…`out16_elevation` | Dimensionless | — | 0 … 1; **default 0.5** | Normalized outlet height |

`t` and indexed `tN` are mutually exclusive. If indexed temperatures are used, **every** layer must
be stated. With neither, the mixed steady solution initializes all layers. Elevation maps by
`min(floor(elevation × layers) + 1, layers)`: 0 is layer 1, 30% of a five-layer tank is layer 2, and
1 is the top layer. The boundary rule is explicit so a port exactly on a layer boundary is not placed
differently by two implementations.

**Properties:** `volume`, `layers`, `stored_energy`, `t1`…`tN`, and `inN_t`/`outN_t` for every
materialized port. Volume is exposed in dm³ on the model contract and held in m³ internally.

### Hydraulic and steady thermal equations

For K materialized ports the tank contributes K−1 pressure equalities against the first port. It also
contributes one mass balance when K ≥ 3 (a junction) or K = 1 (a terminal); at K = 2 the branch-owned
flow already makes that row an identity, exactly as for an interior node:

```
Σᵢ ṁᵢ = 0                         junction/terminal only; ṁᵢ positive into the tank
pᵢ − p₁ = 0                       i = 2…K
```

No hydrostatic term is applied: normalized port height is thermal metadata, not physical metres, and
the script has no vessel height from which to compute `ρgΔz`. No internal pressure drop is invented.

For a steady solve, all layers collapse to one perfectly mixed enthalpy `h_tank`; every outflow carries
it and the incoming-stream energy balance is zero. `volume`, `layers`, and elevations have no steady
effect and remain visible design data. This supplies a unique equilibrium and makes `layers=1`
identical to the steady behavior of every larger count.

In a transient, each layer owns one enthalpy state and each materialized port is attached to the layer
selected above. [`33-transient-time-domain`](../30-solver/33-transient-time-domain.md) owns the
finite-volume derivative, internal displacement flow, density-inversion remixing, and step-size
limits; this document owns the state/parameter/port contract they operate on.

The v1 tank is **adiabatic except for connected streams**. It does not infer ambient loss, wall
conduction, jet entrainment, coil heat transfer, vessel geometry, or hydrostatic pressure. Those need
parameters and validation data rather than defaults disguised as physics.

---

## 7 · `t_sensor` / `p_sensor` / `flow_sensor` — placed observers

An instrument is a component, and a controller reads one (`D-61`). This is the distinction the model
lacked: `in=50` on a heat exchanger is a **specification** — what the design asks for — and `TE1` is a
**measurement** — what the model produced. `measure=NB2.t` blurred the two, and a plant drawing shows
where its instruments are.

```fluidscript
TE1 t_sensor at N2
```

| Kind | `TagCode` | Measures | Property |
|---|---|---|---|
| `t_sensor` (aliases `temperature_sensor`, `te`) | `TE` | temperature | `t` |
| `p_sensor` (aliases `pressure_sensor`, `pe`) | `PE` | pressure | `p` |
| `flow_sensor` (aliases `flow_meter`, `fe`) | `FE` | mass flow | `flow` |

**A sensor attaches to a node; it never sits in the flow path.** `TE1 t_sensor at N2`, never
`HX1 - TE1 - TV1`. A pass-through instrument would carry two ports, gain an inserted node from rule
I2, and contribute equations that are all identities — a hundred sensors would double the size of the
solve to compute nothing. Attachment keeps them out of the hydraulic graph entirely, which is also why
they have no ports, no `DrivesFlow`, and no residuals: `EvaluateResiduals` on a sensor writes nothing
and is never called.

**A sensor reads the node it is attached to and holds no state of its own.** `TE1.t` is `N2.t`. It
exists so that the *script* can name a measurement point, and so that a diagram can draw one; it is
not a filter, a lag, or a source of error. Instrument dynamics are post-v1 and would be parameters on
this kind, not a different one.

**What a flow sensor reads is the sum of the flows entering its node.** On a node with one inlet and
one outlet that is the through-flow and the definition is invisible, which is why it went unstated
until an implementation had to pick one (`C-14`). At a tee it is the only reading that is well
defined: "the flow at this node" otherwise names two or three different numbers, and the plausible
alternatives differ from each other by a factor of two at a mixing junction. A temperature or pressure
sensor has no such ambiguity — a node carries one of each.

**Its measured property is registry data (`MeasuredProperty`)**, which is what lets `control TV1 with
TE1 by PID1` resolve without a `.t`. A kind naming exactly one measured property makes the bare form
unambiguous by construction; a kind naming none makes it `FS1531`.

## Parameter registry

Every table above is data, read by the binder ([`15-semantic-model`](../10-language/15-semantic-model.md)) and by
write-back formatting ([`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md)).
It lives in one place per component — `ComponentKindInfo` built by each component's static
registration — not duplicated into the binder.

A test asserts the registry's parameter set matches this document's tables. Without it, the two
diverge on the first component change and the divergence is invisible until a user writes a parameter
that the docs promise and the binder rejects.

### The actuated parameter

Each kind names at most one `ActuatedParameter` — the single parameter a controller may move at
runtime (`D-61`):

| Kind | `ActuatedParameter` |
|---|---|
| `valve`, `three_way_valve` | `position` |
| `pump` | `speed` |
| everything else | *none* |

This is what makes `control TV1 with TE1 by PID1` unambiguous without writing `.position`. `D-43`
refused a bare actuator on the grounds that "a valve has more than one thing that could move", and it
was right about parameters and wrong about actuators: of `position`, `kvs` and `authority`, only
`position` moves at runtime — `kvs` is a sizing parameter and `authority` a design property. Where the
registry names exactly one, the bare form is safe **by construction**; where it names none, the bare
form is `FS1531` and the qualified form is required. `control TV1.position …` stays legal everywhere,
and is the only form for a kind that ever gains a second actuator.

### Tag codes

Each kind also registers a `TagCode` — the letters in its equipment tag (`D-34`). The v1 set:

| Kind | `TagCode` | Tag at circuit 400 |
|---|---|---|
| `pump` | `PU` | `400PU01` |
| `heat_exchanger` | `HE` | `400HE01` |
| `valve` | `V` | `400V01` |
| `three_way_valve` | `TV` | `400TV01` |
| `tank` | `S` | `400S01` |
| `controller` (aliases `pi`, `pid`, `p`) | `PID` | `400PID01` |
| `t_sensor` | `TE` | `400TE01` |
| `p_sensor` | `PE` | `400PE01` |
| `flow_sensor` | `FE` | `400FE01` |
| `node`, `pipe` | *none* | untagged |

The three instrument codes are the ones an instrument index already uses — TE, PE and FE are
temperature, pressure and flow *element* — which is the argument for a kind per instrument rather than
one `sensor` kind with a `measures=` parameter: the tag falls out of the kind for free.

`node` and `pipe` are deliberately untagged. Both are mostly inferred, both outnumber every other kind
in a lowered graph, and no plant schedule tags them — a diagram labelling forty `400PI` nodes would
bury the six pieces of equipment a reader is looking for.

**A code is a house convention, not a standard**, and registering it as data rather than hard-coding
it is what makes that survivable. The reference drawings this scheme is modelled on write `LP` for a
pump where the table above writes `PU`; both are defensible, neither is a published standard for a
tool to claim, and a site that wants `LP` should get it by changing a registry row rather than by
patching the tagger.

Two constraints hold on any code: it is unique across kinds, and no tag it produces may lex as a
quantity literal — `400PU01` is safe because `PU01` is not a unit symbol, but a code sharing a symbol
with the unit table would produce tags the language reads as numbers. Both are asserted when the
registry is built, alongside the parameter-set test above.

## Invariants

1. `StatedParameters`, `SizedParameters`, and `DefaultParameters` are pairwise disjoint, and their union
   is the component's complete resolved parameter set.
2. `EvaluateResiduals` allocates nothing on the managed heap.
3. `EvaluateResiduals` is deterministic and side-effect free.
4. `residuals.Length == EquationCount` on every call.
5. Every residual is scaled so its magnitude is comparable across component kinds — a pressure residual
   in Pa and a mass residual in kg/s differ by six orders of magnitude, and an unscaled convergence
   test is then meaningless ([`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md)).
6. Pressure drop is positive in the nominal flow direction for every component, pumps included
   (negative for them).
7. Every residual function is continuous and differentiable over the solver's search domain,
   regularised where the physics is not.

Invariants 5 and 7 are the two that get skipped and then cost a week of "the solver does not converge".

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS2101` | Over-determined component | Error | `'{name}': {parameters} cannot all be set. Any {count} of them fix the rest.` |
| `FS2102` | Under-determined after sizing | Error | `{name} needs one of: {list}.` |
| `FS2103` | Both `kv` and `dp` stated | Warning | `{name}: using kv={kv}; dp is implied by it.` |
| `FS2104` | Both `head` and `dp` stated and inconsistent | Error | `{name}: head={head} m is {implied} kPa, not {dp} kPa.` |
| `FS2105` | Valve position outside 0–1 | Error | `'{name}': position must be between 0 and 1.` |
| `FS2106` | Pipe discretization above the cap | Warning | `Using {cap} internal nodes instead of {n}.` |
| `FS2107` | Node with a single connection and no boundary parameter | Warning | `'{name}' is a dead end. Set t, p or flow to make it a boundary.` |
| `FS2108` | Efficiency outside 0–1 | Error | `'{name}': efficiency must be between 0 and 1.` |
| `FS2109` | Rated exchanger over-determined: four temperatures, duty **and** a thermal size | Error | `{name}: those four temperatures and {power} kW already fix UA at {implied} kW/K. Remove {param}, or let a temperature be solved.` |
| `FS2110` | A rating parameter stated in Duty mode | Warning | `{name}: '{param}' has no second-side profile to rate. State in2/out2/dt2/flow2, connect both secondary ports, or remove it.` |
| `FS2111` | Requested duty exceeds what the inlet temperatures allow | Error | `{name} cannot transfer {power} kW: with {t_hot} and {t_cold} in, the most any exchanger could move is {qmax} kW.` |
| `FS2112` | Exactly one secondary port is connected | Error | `{name}: Coupled mode requires both in2 and out2 connections; {port} is open.` |
| `FS2113` | Tank uses `t` with any indexed temperature, or states only part of `t1`…`tN` | Error | `{name}: state either t for every layer, or all of t1…t{layers}; do not mix them.` |
| `FS2114` | `layers` is non-integral or outside 1…100 | Error | `{name}: layers must be a whole number from 1 to 100.` |
| `FS2115` | A tank port elevation is outside 0…1 | Error | `{name}: {parameter} is normalized height and must be between 0 (bottom) and 1 (top).` |
| `FS2116` | Tank substance is not a supported single-phase liquid | Error | `{name}: stratified tank supports a single-phase liquid; {substance} is outside that model.` |
| `FS2117` | A required parameter is absent | Error | `'{name}': a {kind} must state {parameter}.` |
| `FS2118` | A parameter group has too few of its members stated | Error | `'{name}': a {kind} must state {count} of {parameters}.` |

`FS2101` covers both of this kind's relations, which is why its message names the group rather than
spelling one of them out. **It does not name the implied value.** For `ua`/`area`/`u` it could —
`u = ua/area` is arithmetic — but for `power`/`in`/`out`/`flow` the implied flow is `Q / (cp · dT)`,
and a c_p needs a substance. The binder holds a fluid's *name*; the substance behind it is resolved
at lowering, which is where a message quoting the fourth value belongs (`C-21`).

`FS2105` and `FS2108` name their component, because a script has more than one valve in it.

**`FS2117` and `FS2118` are the two halves of `D-64`'s requirement, and they are not the same
check.** `FS2117` is a policy on one parameter: a `supply` with no `t` has no state to give the
fluid entering there. `FS2118` is a property of a *set*: neither `flow` nor `p` is individually
required — a rule that made either so would reject every valid boundary there is — and what is
required is that exactly one of them appears. `FS2101` is its upper bound and this is its lower
one, which is why the group carries two codes.

## Worked example

`HE1 heat_exchanger power=30 in=20 out=50`, water:

| Step | Value |
|---|---|
| Stated | `power` = 30 000 W, `in` = 293.15 K, `out` = 323.15 K |
| Sized | `dp` (no value given), `flow` (implied, not sized) |
| cp at mean 35 °C | 4178 J/(kg·K) |
| h_in at (p, 293.15 K) | 84 007 J/kg |
| h_out at (p, 323.15 K) | 209 418 J/kg |
| Δh | 125 411 J/kg |
| Energy balance ṁ = Q̇/Δh | 30 000 / 125 411 = **0.2392 kg/s** |
| Volume flow | 0.2392 / 994 = 0.241 l/s |

Cross-check against the cp shortcut ṁ = Q̇/(cp·ΔT) = 30 000/(4178 × 30) = 0.2394 kg/s against the
enthalpy form's 0.2392 — agreement to three figures, as expected for liquid water where cp is nearly constant. The enthalpy form is used
because it stays correct where the shortcut does not: near saturation, across a phase change, and for
humid air.

**The component contributes one equation and zero unknowns.** ṁ is the branch flow, owned by the
branch; the heat exchanger constrains it. That distinction — components constrain, branches and nodes
own unknowns — is the whole shape of the system assembly in
[`31-solver-architecture`](../30-solver/31-solver-architecture.md).

**"One equation" means one *term*, in its outlet node's energy balance** — not an extra row in the
system. There are exactly N energy equations for N node enthalpies; a duty-bearing component defines
its outlet port enthalpy as `h_in + Q̇/ṁ`, and that port enthalpy is what the downstream node upwinds
to. Assembling a separate component energy row on top of the per-node balance over-determines the
enthalpy block by the number of duty-bearing components.
[`23-topology-and-graph`](23-topology-and-graph.md)'s counting table is the authority and shows no
such row.

## Acceptance criteria

- [ ] Every component's governing equation has a test with hand-checked numbers, independent of the solver.
- [ ] The worked example yields 0.2392 kg/s ± 1e-4 through the component's own residual.
- [ ] The pump curve is tested at n = 1, n = 0.5 and n = 0. At n = 0.5 the head is `0.25·H₀ − k·ṁ²`,
      **not** `0.25·H₀ − 4k·ṁ²`; at n = 0 the residual is finite.
- [ ] The valve law is tested against a hand-computed case in both the Kv units and the SI form, and
      the two agree.
- [ ] `dn` is not assignable from, or to, a `Length`; a test asserts the kinds do not convert.
- [ ] A node's `EquationCount` is the same before and after a solved flow reverses direction.
- [ ] `FS2101` fires for a heat exchanger with all four of power/in/out/flow, and names the implied value.
- [ ] **ε-NTU and LMTD agree to within rounding on a counterflow case**, computed independently from the
      same solved state — the substation gives UA = 12 071 W/K by both routes
      ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)). This is the strongest check on
      a `Cmin` or sign error, because the two formulations share no code.
- [ ] ε is C¹ across `Cr = 1`, verified by finite differences either side of the blend — the case an
      LMTD residual could not evaluate at all.
- [ ] `Cmin` is recomputed per iteration: a test drives a substation from full to part load across the
      `C₁ = C₂` crossover and asserts the residual stays continuous.
- [ ] An exchanger with no secondary thermal-profile properties or connections behaves identically to
      the original Duty block, even if `ua` or geometry is stated; those fields emit `FS2110`.
- [ ] `in2=85 out2=45 flow2=0.90` with open secondary ports selects Rated mode, assembles no side-2
      hydraulic branch, and sizes/reports the same thermal relation as an equivalent boundary profile.
- [ ] Connecting both secondary ports selects Coupled mode and creates two flow groups; connecting
      exactly one produces `FS2112` and no solve.
- [ ] `plates=39` with `plate_area=0.10` gives **3.70 m²**, not 3.90 — the two end plates transfer
      nothing.
- [ ] Halving `lamella` raises `u` and roughly quadruples `dp`, asserted as a direction and a rough
      magnitude rather than an exact figure.
- [ ] A duty exceeding `Cmin·(T_h,in − T_c,in)` produces `FS2111` naming the thermodynamic maximum,
      never a converged solution.
- [ ] `ua`, `area` and `u` all stated produces `FS2101`; four temperatures plus `power` plus `ua`
      produces `FS2109`.
- [ ] A valve at Δp = 0 evaluates a finite residual and a finite derivative.
- [ ] Friction factor matches Colebrook–White within 0.01 % over Re = 4×10³…10⁸ and ε/D = 0…0.05.
- [ ] The laminar–turbulent transition is continuous in value and first derivative.
- [ ] `EvaluateResiduals` allocates zero bytes, asserted by an allocation-counting test.
- [ ] A registry test asserts the parameter tables match this document, **and that every parameter's
      canonical unit comes from its dimension** rather than from the table (`D-14`) — the check that
      would have caught `length` in m beside `roughness` in mm.
- [ ] A node interior to a branch contributes **no** mass balance, and a circuit built only of
      degree-two nodes plus two terminals assembles to a non-singular Jacobian.
- [ ] A three-way valve wired as a mixer (two inflows at `b` and `c`, outflow at `a`) solves and
      produces **no** `FS4009`; the same valve with a genuinely reversed branch still does.
- [ ] `power=-70 dt=20` gives an outlet 20 K below the inlet; `dt=-20` produces `FS1307`.
- [ ] `nodes=2` on a pipe produces four states with a monotonic profile.
- [ ] `T1 tank` resolves `volume=300 dm3`, `layers=5`, and 0.5 elevations as defaults—not sized or
      stated—and the model contract reports the basis for each.
- [ ] `T1 container v=300` produces the same domain component as canonical `T1 tank volume=300`; the
      semantic model and wire contract use canonical names while the source round-trips byte for byte.
- [ ] A five-layer tank maps elevations 0, 0.30, 0.90, and 1.0 to layers 1, 2, 5, and 5 exactly.
- [ ] A four-port tank contributes one mass balance and three independent pressure equalities, with no
      hydrostatic term; reverse flow uses actual sign to swap inlet/outlet behavior.
- [ ] A steady tank produces one mixed outlet enthalpy for every outflow regardless of `layers`; a
      transient `layers=1` starts from and returns to the same equilibrium.
- [ ] Partial indexed temperatures and `t` plus `t1` produce `FS2113`; non-integral layers and invalid
      elevations produce `FS2114`/`FS2115` without throwing.

## Open questions

None. `D-25` makes bare connections ideal links. [`24-auto-sizing`](24-auto-sizing.md) sizes pipes for
the pressure-drop target first, then checks velocity and steps up if required; explicit `minor_loss`
captures known fittings without inferring physical elbows from diagram bends.
