---
id: 01-vision-and-scope
title: Vision and scope
tier: 00-foundation
status: draft
owns: [requirement register, non-goals, target users, phase boundaries]
depends_on: []
traces_to: []
open_questions: 0
last_review_pass: 0
---

# Vision and scope

## Purpose

This document is the **traceability root**. Every other document in `plan/` must name at least one
requirement id from the register below in its `traces_to:` frontmatter, and every requirement here
must be owned by at least one document. A requirement with no owner is unbuilt work nobody noticed;
a document tracing to nothing is work nobody asked for. The review loop checks both directions.

## Responsibilities

**Owns.** The requirement register, the non-goals, the target users, and the phase boundaries.

**Explicitly does not own.** How anything is built (tiers 10–70), the milestones' exit criteria
([`05-milestones-and-acceptance`](05-milestones-and-acceptance.md)), or the decisions taken along the
way ([`06-decision-log`](06-decision-log.md)).

## The idea in one paragraph

FluidScript is a plant-modeling tool where you describe a hydronic system by *writing it
down*, in a syntax closer to a markdown list than to Python, and the tool figures out the rest — pipe
diameters, pump head, valve sizing — while drawing the system as a live P&I diagram beside the text.
Change a number in the script and the diagram redraws; drag a valve setting on the diagram and the
script updates. The physics is real (CoolProp-grade fluid properties, a genuine circuit solver), but
the experience should feel closer to a sketchpad than to a simulation suite.

## Who it is for

| User | What they need | Consequence |
|---|---|---|
| **HVAC / process designer** | Fast what-if sizing without opening a heavyweight suite | Auto-sizing must be trustworthy, and overrides must be one word long |
| **Educator / student** | To see how a circuit responds when something changes | Transient mode and the warning log matter more than CAD fidelity |
| **LLM agent** | To generate and validate plant designs from a text brief | `/docs` must be complete and agent-navigable (`R-28`, `R-29`); diagnostics must be machine-parseable (`R-20`) |
| **Reviewer / stakeholder** | To read a design without installing anything | Export (`R-31`) and a legible diagram matter more than editability |

The LLM-agent row is not decorative. It is the reason the documentation requirement is absolute and
the reason diagnostics carry stable codes rather than only prose.

## Requirement register

### Language

| Id | Requirement |
|---|---|
| `R-01` | A plant system is described in a plain-text script whose syntax resembles structured markdown, not a general-purpose programming language. |
| `R-02` | Every component parameter is optional. Omission follows that parameter's declared registry policy: normally "size this for me", or a visible documented default when a binding decision supplies one (`D-32`). Providing a value always constrains the model; explicit values are never merely seeds. |
| `R-03` | The language supports named values (`let`), arithmetic, unit-annotated literals, and references to other components' resolved properties. It has no control flow, no user-defined functions. |
| `R-04` | Dimensioned values may be written bare (`power=30` means kW) or with an explicit unit (`power=30000 W`); both resolve to the same internal SI quantity. |
| `R-05` | The parser recovers from errors: one bad line does not prevent the rest of the script from being analysed and rendered. |
| `R-06` | The language infers what it reasonably can: intermediate nodes between named components, terminating nodes on open ports, and flow direction from connection order. |
| `R-46` | A script may describe several circuits, each with a number that is stated or resolved automatically, and subcircuits that attach to a parent circuit at explicitly named nodes. A circuit's role (`AHU`, `radiator`) resolves through a registry rather than a keyword (`D-33`, `D-35`). |
| `R-49` | A controller is declared once with its algorithm and gains, and bound to its actuator and measurement by a separate statement with named arguments (`D-40`). |

### Physics and core

| Id | Requirement |
|---|---|
| `R-07` | Fluid and thermodynamic properties come from SharpProp (CoolProp), behind an abstraction the rest of Core depends on instead of the package directly. |
| `R-08` | Humid-air psychrometrics is a first-class, validated property capability. v1 does not claim to solve air-side circuits (`D-28`). |
| `R-09` | v1 ships six flow-component families: `Node`, `Pipe`, heat source/consumer (including rated two-sided exchangers), valve, pump, and stratified tank. Controllers are non-flow model elements; air-side components are post-v1. |
| `R-10` | A `Pipe` may be discretized into an arbitrary number of internal nodes to resolve the state along its length. |
| `R-11` | The system can be solved as a **steady-state equilibrium**. |
| `R-12` | The system can be solved as a **time-domain transient**, where the equilibrium is being disturbed — changing demand, controller action, transport delay. |
| `R-13` | PI control acts on the transient system and is part of the model, not the UI. PID derivative mode is optional until a validated use case defines its filtering and tuning requirements. |
| `R-14` | Transport of fluid between components takes time, and that time is modelled explicitly rather than assumed instantaneous. |
| `R-15` | Component and system sizing can be posed as an optimization problem and solved by an evolutionary solver. |
| `R-16` | Core is the backend: it has no UI dependency and is usable as a library without the API host. |
| `R-17` | Core carries an extensive unit-test suite; `dotnet test` runs all of it. |
| `R-35` | A heat exchanger is **two-sided and rated**: it accepts a second fluid side, a flow arrangement, and a thermal size expressed as `ua`, `area`, `u`, or plate geometry (`plates`, `lamella`). Any of those may be omitted and sized, subject to a minimum approach temperature (`D-17`). |
| `R-32` | Standard component dimensions (pipe diameters per EN/SFS series, valve Kv values) are available as a curated, versioned, provenance-carrying catalogue gathered from public manufacturer sources — never scraped from paywalled standards, and never fetched at runtime. |
| `R-36` | Persistent sensors, instrumentation dynamics, and report points are deferred beyond v1 (`D-23`); direct references to node and component properties provide v1 control measurements. |
| `R-37` | Component symbols are declarative Core-owned metadata shared by the canvas and exporters, not duplicated renderer code. |
| `R-45` | A `tank` stores liquid in a parameterized number of equal-volume, perfectly mixed layers; it supports multiple indexed inlet/outlet ports with normalized elevations and conserves mass and enthalpy during a transient (`D-32`). |
| `R-47` | Every device carries a derived equipment tag of the form `<circuit><code><ordinal>` — `400PU01` — computed by Core from the circuit number, a per-kind code, and declaration order. The tag is metadata carried in the model contract; the component's identifier remains the name the user wrote (`D-34`, `D-36`). |

### API

| Id | Requirement |
|---|---|
| `R-18` | Compile, validate, and steady-state solve are request/response over REST. |
| `R-19` | Transient runs stream their frames over a WebSocket so playback can begin before the run completes. |
| `R-20` | Diagnostics cross the wire with a stable code, a severity, and a source span that maps to an editor squiggle. |

### Frontend

| Id | Requirement |
|---|---|
| `R-21` | The script and the rendered diagram sit side by side; editing the script updates the diagram after a ~300 ms idle debounce, not on every keystroke. |
| `R-22` | The canvas is CAD-style: zoomable, pannable, with a visible origin whose X axis is red and Y axis green. |
| `R-23` | Hovering a component reveals its resolved state — temperatures, pressures, flow — without a click. |
| `R-24` | Warnings are portrayed on the offending component *and* listed in a console-style log with human phrasing ("approaching freezing point"). |
| `R-25` | A user may change a component's properties directly on the canvas, and the change is written back into the script text. |
| `R-26` | Light and dark themes ship natively; further themes are configurable. The palette is subtle and HVAC-themed, with contrast reserved for where it carries meaning. |
| `R-27` | The tool should feel playful and inviting rather than like an engineering console, while the heavy computation stays invisible in Core. |
| `R-33` | Script syntax highlighting uses the familiar Visual Studio / VS Code colour palette, with automatic keyword recognition — the editor should look like the editor its users already know. |
| `R-34` | Any fluid state property can be rendered as a colour gradient across nodes and pipe cells, selected from the script (`show temperature`), with a legend naming the property, its unit, and its scale. |
| `R-38` | The static product can create, open, save, Save As, recover, and download named `.fluid` files locally, with visible filename, dirty state, conflicts, and failures. |
| `R-41` | A transient run uses an immutable compiled snapshot. Script editing continues independently; simulation, frame decoding, and render preparation run off the UI thread. Isolation, worker, protocol, or model-integrity failure stops the affected run. |
| `R-42` | All v1 workflows meet WCAG 2.2 AA: keyboard operation, visible focus, non-colour status cues, accessible data readouts, reduced motion, and equivalent alternatives for canvas-only interactions. |
| `R-44` | P&I diagrams render nominal heat progression from left to right: source and cooling-side circuits on the left, conversion/storage centrally, and heating consumers on the right, including parallel source and consumer groups (`D-31`). |
| `R-48` | A distribution circuit renders as a supply header along the top and a return header along the bottom, with its subcircuits stacked between them. Components are spaced sparsely by default, and the spacing is adjustable from the script without Core interpreting it (`D-37`, `D-38`). |
| `R-50` | Several documents are open at once as tabs; only the active document renders and streams frames, and a run whose document is switched away from continues rather than stopping (`D-39`). |
| `R-51` | The solver's state — converging, converged, or failed — is continuously visible, conveyed by text and shape as well as colour, and names which computation it refers to. |

### Documentation and delivery

| Id | Requirement |
|---|---|
| `R-28` | Every user-visible functionality has a page in `/docs`, categorized as Tutorial, Advanced Workflows, or Functions. **No exceptions**: a feature without its page is incomplete. |
| `R-29` | `/docs` is written to be navigable by an LLM agent well enough that the agent can author a valid circuit unaided. |
| `R-30` | The repository is public, with a README that gets a newcomer to a rendered diagram. |
| `R-31` | M3 exports the diagram as SVG and PNG. DXF and a versioned model interchange format are evidence-driven post-v1 work; XML is not promised without a consumer. **STEP and IFC are out of scope** because the model holds no 3D geometry (`D-12`, `D-29`). |
| `R-39` | Every durable script declares its FluidScript language major and can pin a catalogue version. Unsupported versions are rejected without mutation; migrations are explicit and previewable. |
| `R-40` | Quantitative quality budgets define supported scale, responsiveness, transient throughput, numerical accuracy, determinism, resource use, and input limits; milestone tests measure them on a recorded reference environment. |
| `R-43` | Engineering results state their validity domain and basis. Property, hydraulic, thermal, sizing, conservation, and transient reference cases must meet the tolerance matrix before the corresponding capability is presented as supported. |

## Non-goals

Stating these is as load-bearing as the requirements — each one is a direction the project could
plausibly drift, and drifting costs the thing that makes FluidScript worth building.

- **Not a general-purpose programming language.** No loops, conditionals, user functions, or imports
  in the script. If a system needs those to be expressible, that is a signal the component library is
  missing something, not that the language is.
- **Not a CAD editor.** The canvas renders and permits property edits. It is not a drawing tool with
  free-placement geometry; layout is computed, and manual position overrides are post-v1 research.
- **Not a CFD or 1D-detailed transient code.** Pipes are lumped or discretized into finite nodes.
  There is no momentum equation solved in space, no two-phase flow regime map, no acoustics.
- **Not multi-user or cloud-hosted in v1.** One user and one local workspace. One transient run may
  continue from an immutable snapshot while the user edits the next draft (`D-22`).
- **Not a replacement for approval-grade calculation.** Auto-sizing gives defensible starting points,
  and the tool must say so. Anything presented as a final selection needs a stated basis.

## Phase boundaries

Detailed exit criteria live in [`05-milestones-and-acceptance`](05-milestones-and-acceptance.md);
this is the boundary, not the checklist. The risk-separated vertical-slice structure and the
M2a/M2b split are binding under `D-29` (with the M4 tank addition from `D-32`).

| Phase | Contains | Deliberately excluded |
|---|---|---|
| **M1 — Language spine** | Lexer, parser, binder, diagnostics, unit system, round-trip printer | Any physics |
| **M2a — Hydraulic core** | Liquid properties, components, graph, catalogue, hydraulic sizing, Newton solve | Coupled thermal rating, transient |
| **M2b — Coupled thermal rating** | Energy balances and the two-sided rated exchanger (`R-35`, `D-17`) | Transient, controllers |
| **M3 — Usable static product** | REST API, editor, canvas, Core-owned symbols, local file lifecycle, accessibility, SVG/PNG export, hover, log, themes, syntax palette and state gradients | Canvas write-back, transient |
| **M4 — Make it move** | Transient solver, transport delay, stratified tank, PI control, isolated worker execution, WebSocket streaming, non-blocking playback | PID derivative mode, evolutionary sizing |
| **M5 — Close the loop** | Canvas write-back to script, interactive property edits | — |
| **M6 — Evidence-driven extensions** | Evolutionary sizing, DXF/model interchange, subsystem composition | XML unless a consumer changes scope; STEP and IFC never (`D-12`) |

`R-15` (evolutionary sizing) is deliberately last: it optimizes over a solve, so it cannot be built
or validated before the solve it wraps is trustworthy.

## Worked example

The brief's own script, and what each requirement demands of it:

```fluidscript
fluidscript 1
circuit coolingLoop                       # R-01: declarative block
fluid dynamic water                       # R-12: `dynamic` selects the transient model
style blue 2px fillet --                  # R-26: presentation is in the script, not a side file

HE1 heat_exchanger power=30 in=20 out=50  # R-04: 30 kW, 20 °C, 50 °C by parameter kind
3WV three_way_valve                       # R-02: no parameters at all — size it
PU1 pump                                  # R-02 + R-14: head derived from the loop it sits in

connections
N1 - N2                                   # R-06: N1 and N2 are never declared; they are inferred
N2 - HE1
HE1 - 3WV
3WV - N2
3WV - N3                                  # R-06: N3 is an open port; it gets a terminating node
```

Reading it top to bottom: the version directive plus seven model statements produce a topology with
a sized valve and a rendered diagram. The disconnected pump and ideal links cannot create a physical
head or pipe size; diagnostics say so. That density is the product.
Any proposed syntax change should be measured against whether this example gets longer.

### The reference circuits

The script above is the **syntax** reference, and it is what `R-01`–`R-06` are measured against. It is
not a solvable circuit: `PU1` appears in no connection, and `N1`/`N3` are dead-end stubs, so the loop
is not closed and there is no primary-side boundary. The language reports these rather than guessing
(principle P3: inserting a pump into a loop has more than one defensible answer, so the language does
not choose).

**Its diagnostic set, in full**, because three documents previously disagreed about it:

| Code | Count | About |
|---|---|---|
| `FS1507` | 1 | `PU1` is in no connection |
| `FS2107` | 2 | `N1` and `N3` are dead ends with no boundary condition |
| `FS1510` | 6 | one per inferred component — 3 from I1, 1 from I2, 2 from I3 (the two ports of disconnected `PU1`) |

Nine in total, of which six are info-level and hidden by default in the log
([`56-console-log`](../50-frontend/56-console-log.md)). Any document stating a different count for this
script is wrong.

Five **circuit** references follow. Every worked example in this tree states which one it uses, and no
document may introduce an unnamed variant of any of them — that is `D-11`, as amended by `D-16`,
`D-18`, and `D-32`.

**One carve-out, and it is narrow.** A document illustrating a *numerical method* rather than a
circuit may use a minimal abstract system — a two-node loop with an invented pump curve, say — provided
it says so in the same sentence and states no physical result the reference circuits also state.
[`32-steady-state-newton`](../30-solver/32-steady-state-newton.md)'s convergence trace is the case
this exists for: the point being made is about quadratic convergence, and a circuit with three flows
and a catalogue lookup obscures it. The line is whether the example claims to be a *plant*: if it
names components, sizes them, or reports temperatures, it is a circuit and `D-11` applies.

| Circuit | Sample file | Used by |
|---|---|---|
| **Cooling loop** — a three-way mixing circuit | `samples/m2-cooling-loop.fluid` | topology, layout, model contract, canvas, visualization |
| **Simple loop** — one series circuit, one flow | `samples/m2-simple-loop.fluid` | sizing and solver arithmetic |
| **Substation** — a two-sided plate exchanger between two circuits | `samples/m2-substation.fluid` | thermal rating, two-sided sizing, coupled circuits |
| **Demand-step loop** — the cooling loop in time, with a controller | `samples/m4-demand-step.fluid` | transient, controllers, streaming, playback |
| **Storage header** — two source boundaries, a stratified tank, and two consumer boundaries | `samples/m4-storage-header.fluid` | tank physics, indexed ports, thermal ordering, multiple sources/consumers |

Five are needed because they answer different questions. The cooling loop is the interesting
*topology* — a junction, a bypass, a three-port component, mixed temperatures — and it is what the
graph, layout and rendering documents must be exercised against. The simple loop has one flow
everywhere, which is what makes a sizing or solver worked example checkable by hand. The demand-step
loop adds the one thing a steady circuit cannot show: **volume between the disturbance and the
measurement**, which is what every transient and control example actually depends on (`D-16`). The
substation adds the two things a single circuit cannot show: **a component with a fluid on both sides**,
and **two hydraulic circuits solved together** (`D-17`, `D-18`). The storage header adds an intentional
thermal capacitance, several connections on one component, and parallel source/consumer groups
(`D-32`).

The intended whole-plant reading is:

```
ground-source circuit / air-water source → heat conversion → tank → radiator and AHU networks in parallel
```

Every arrow in that line is **thermal progression**, not necessarily a right-pointing fluid arrow.
Hydronic return branches still run right to left. A dedicated heat-pump performance-map component is
post-v1 ([`72-roadmap`](../70-future/72-roadmap.md)); naming heat conversion here fixes the layout
contract without pretending that source/sink COP physics already exists. A coupled exchanger back to
a ground circuit remains grouped on the left of the conversion stage (`D-31`).

#### The cooling loop — topology reference

```fluidscript
fluidscript 1
circuit coolingLoop
fluid water
style blue 2px fillet --
show temperature

HE1 heat_exchanger power=30 in=20 out=50
3WV three_way_valve
PU1 pump
P1  pipe length=25

connections
N1 - N2                      # primary supply into the mixing node
N2 - PU1                     # the secondary pump drives the loop
PU1 - HE1
HE1 - 3WV
3WV - N2                     # recirculation branch — closes the secondary loop
3WV - P1
P1 - N3                      # primary return

N1 node t=6 p=300            # primary-side boundary
N3 node p=280
```

Changes from the syntax reference, each with a reason: **`PU1` is wired into the secondary loop**,
between `N2` and `HE1`; **`P1` gives the primary return a length** — 25 metres under `D-14`, since a
bare `Length` is SI — without which the pipe sizing rule in [`24`](../20-core-domain/24-auto-sizing.md)
has no physical path length;
**`N1` and `N3` carry boundary conditions**, making the primary side a real source and sink rather
than dead ends; and **`fluid water`** rather than `dynamic`, since M2 is the steady-state milestone —
the transient version is the demand-step loop below.

**The boundary lines are ordinary declarations of kind `node`**, written below the connections by
convention rather than by rule ([`12-grammar`](../10-language/12-grammar.md)). They read as
`N1 node t=6 p=300` rather than `N1 t=6 p=300` because the latter is not a statement the grammar has:
its second token is a parameter name where a kind name belongs. Declaring them also means inference
rule I1 does not fire for `N1` and `N3` — which is the honest outcome, since a node the user gave a
boundary condition is a node the user wrote.

**The pump's position is the part that is easy to get wrong**, and it is worth stating why rather than
only stating the answer. Putting `PU1` on the primary branch (`N1 - PU1`, `PU1 - N2`) reads naturally and
does not work: the secondary loop `N2 → PU1? → HE1 → 3WV → N2` then contains no flow-driving
component, every pressure drop around it is positive, and the only solution is **zero recirculation**
— at which point `N2` sits at the primary's 6 °C and `HE1`'s stated `in=20` cannot be met.
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)'s `FS2214` ("nothing drives flow
around this loop") is exactly the check that catches it, and the fact that it does is a point in the
check's favour.

**Solved state.** These are the numbers every other document must reproduce for this circuit. They are
computed from the stated duty and temperatures alone, so they are checkable without a solver:

| Quantity | Value | Derivation |
|---|---|---|
| Secondary flow (through `PU1`, `HE1`) | **0.2394 kg/s** | 30 000 W ÷ (h₅₀ − h₂₀) = 30 000 ÷ 125 333 |
| Mixing fraction at `N2` (primary share) | **0.681** | (h₅₀ − h₂₀) ÷ (h₅₀ − h₆) = 125 333 ÷ 184 140 |
| Primary flow (`N1 → N2`, `3WV.c → P1 → N3`) | **0.1629 kg/s** | 0.681 × 0.2394 |
| Recirculation flow (`3WV.b → N2`) | **0.0764 kg/s** | 0.2394 − 0.1629 |
| Primary-side duty check | **30 000 W** | 0.1629 × (h₅₀ − h₆) = 0.1629 × 184 140 |
| `P1` sized diameter | **DN20** | 0.1649 l/s at 50 °C → 138 Pa/m, 0.45 m/s |

with h₆ = 25 200, h₂₀ = 84 007, h₅₀ = 209 340 J/kg
([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md)).

Node temperatures: `N1` 6 °C · `N2` 20 °C · `PU1__HE1` 20 °C · `HE1__3WV` 50 °C · `3WV__P1` 50 °C ·
`N3` 50 °C. Pressures follow from the solve and are not fixed by hand here; only `N1` = 300 kPa and
`N3` = 280 kPa are stated.

**Inference inventory**, which several documents count: **6 declared** components (`HE1`, `3WV`, `PU1`,
`P1`, and the two boundary nodes `N1` and `N3`), **1 node from I1** (`N2`, the only identifier that
appears solely in `connections`), **3 from I2** (`PU1__HE1`, `HE1__3WV`, `3WV__P1`), and **none from
I3** — every port of every component is connected. **Six nodes, ten components**, four inferred, so
exactly four `FS1510` entries.

Node and component totals are unchanged from earlier drafts of this circuit; only the *origin* of `N1`
and `N3` moved, from `inferred:I1` to `declared`, when the boundary lines became real declarations.
Any document still counting three I1 nodes or six inferred components here is stale.

#### The simple loop — sizing and solver reference

```fluidscript
fluidscript 1
circuit simpleLoop
fluid water

HE1 heat_exchanger power=30 in=20 out=50
CV1 valve
PU1 pump
P1  pipe length=25

connections
N1 - PU1 - N2 - HE1 - N3 - CV1 - N4 - P1 - N1
```

One closed series loop: four nodes, four components, one flow. No node states a pressure, so the graph
picks a datum and says so (`FS2201`). Because `HE1` states `power`, `in` and `out`, the flow is fixed
by the energy balance at **0.2394 kg/s** — the same figure as above — which is what makes every sizing
step checkable by hand ([`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)).

#### The substation — two-sided exchanger reference

```fluidscript
fluidscript 1
circuit substation
fluid water
show temperature

# --- district-heating primary, 85/45 -------------------------------
NPS node t=85 p=600
NPR node p=350
PCV valve
PP  pipe length=12

# --- heating secondary, 40/60 --------------------------------------
SP   pump
SS   pipe length=30
SR   pipe length=30
LOAD heat_exchanger power=-150 dt=20

# --- the exchanger between them ------------------------------------
HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300

connections
NPS - PCV - PP - HX1.in2
HX1.out2 - NPR

HX1.out - SS - NSUP
NSUP - LOAD - NRET
NRET - SR - SP - HX1.in
```

**Two hydraulic circuits, coupled only through `HX1`.** The primary is open — it enters at `NPS` and
leaves at `NPR`, both with stated pressures — and the secondary is a closed loop driven by `SP` with an
auto-picked datum (`FS2201`). They share no node and no flow. `HX1` is the only component in both, and
it couples them **thermally, not hydraulically**: heat crosses, fluid does not. This is what forces
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md) to allow more than one connected
component in a solve; `D-17` therefore closes the earlier isolated-subgraph ambiguity rather than
reporting `FS2213`.

`LOAD` is the same component kind used in **duty mode** — its `in2`/`out2` are unconnected, so it is a
heat sink with a stated duty and temperature difference, exactly as `HE1` is in the cooling loop. One
kind, two modes, selected by whether the second side is wired (`D-17`).

**Thermal design point.** Every figure below follows from the four temperatures and the duty, so it is
checkable without a solver. Water properties at each side's mean temperature
([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md)): primary cp = 4190 J/(kg·K) at 65 °C,
secondary cp = 4181 J/(kg·K) at 50 °C.

| Quantity | Value | Derivation |
|---|---|---|
| Primary flow | **0.8950 kg/s** | 150 000 ÷ (4190 × 40) |
| Secondary flow | **1.7938 kg/s** | 150 000 ÷ (4181 × 20) |
| `C_hot` = ṁ·cp, primary | **3750 W/K** | 0.8950 × 4190 |
| `C_cold` = ṁ·cp, secondary | **7500 W/K** | 1.7938 × 4181 |
| `C_min` / `C_max` ratio `C_r` | **0.500** | 3750 ÷ 7500 |
| Maximum possible duty | **168 750 W** | C_min × (85 − 40) |
| Effectiveness ε | **0.8889** | 150 000 ÷ 168 750 |
| **NTU** (counterflow) | **3.219** | (1/(1−C_r))·ln((1−ε·C_r)/(1−ε)) = 2·ln 5 |
| **UA** | **12.07 kW/K** | NTU × C_min |
| Required area at `u=3300` | **3.658 m²** | 12 071 ÷ 3300 |
| Approach (cold end) | **5.0 K** | 45 − 40 |

**The LMTD cross-check is exact, and it is the test to write.** ΔT at the hot end is 85 − 60 = 25 K, at
the cold end 45 − 40 = 5 K, so LMTD = (25 − 5)/ln(25/5) = 20/1.60944 = **12.427 K**, and
UA = 150 000/12.427 = **12 071 W/K** — the same figure ε-NTU gives, to six significant figures. For
counterflow the two formulations are algebraically identical; an implementation where they disagree by
more than rounding has a sign or a C_min error, and this circuit catches it.

**Selection.** With a catalogue plate of 0.10 m² effective area, 3.658 m² needs 37 effective plates,
so **39 total** (two end plates transfer nothing) and 3.70 m² installed. Rounding **up** is the safe
direction: more area means a closer approach, never a duty shortfall.

| At the selected size | Value |
|---|---|
| Installed UA | 12 210 W/K (3.70 × 3300) |
| NTU | 3.256 |
| ε | 0.8912 |
| Delivered duty | **150.4 kW** — 0.25 % over the stated 150 |
| Primary return | 44.90 °C |
| Secondary supply | 60.05 °C |
| **Achieved approach** | **4.90 K** |

The 0.25 % overshoot is the discrete plate count showing, and it is reported rather than hidden
([`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)'s `FS2310`). A designer reads "39 plates,
4.9 K approach" and recognises a selection; "UA = 12.07 kW/K" is a number they would have to trust.

**Inference inventory:** **9 declared** (`NPS`, `NPR`, `PCV`, `PP`, `SP`, `SS`, `SR`, `LOAD`, `HX1`),
**2 from I1** (`NSUP`, `NRET`), **5 from I2** (`PCV__PP`, `PP__HX1`, `HX1__SS`, `SR__SP`, `SP__HX1`),
and **none from I3** — `LOAD`'s `in2` and `out2` are optional, so leaving them open is duty mode rather
than an open port. **Nine nodes, sixteen components.**

`arrangement` is not written because **`counter` is the default**
([`22-component-model`](../20-core-domain/22-component-model.md)); a plate exchanger is counterflow
unless someone builds it otherwise, and `D-02` says an omitted parameter is a request, not a hole.

#### The demand-step loop — transient and control reference

```fluidscript
fluidscript 1
circuit demandStep
fluid dynamic water
show temperature

HE1 heat_exchanger power=30 out=50
3WV three_way_valve
PU1 pump
P1  pipe length=25
PB  pipe length=8 dn=20 nodes=4   # recirculation branch — the path the controller sees
TC1 controller measure=N2.t actuate=3WV.position setpoint=20

connections
N1 - N2
N2 - PU1
PU1 - HE1
HE1 - 3WV
3WV - PB - N2                # recirculation, now with volume in it
3WV - P1
P1 - N3

N1 node t=6 p=300
N3 node p=280

schedule
at 60 s   HE1.power = 45
```

**Three changes from the cooling loop, and each is load-bearing** (`D-16`):

**`PB` puts pipe volume on the recirculation branch.** This is the change the whole transient story
rests on. In the cooling loop the path from `HE1` to the measured node `N2` is
`HE1 → HE1__3WV → 3WV.a → 3WV.b → N2` with no declared pipe on it, so a disturbance at `HE1` reaches
`N2` within one timestep and there is no dead time to tune against. `P1` cannot supply it: `P1` sits on
the primary *return*, downstream of `N2`, and discharges to `N3` without returning. 8 m at `nodes=4`
gives four 2 m pipe cells inside the loop the controller actually closes around. Lowering also
creates five 1.6 m hydraulic sub-pipes; the two meshes and their ownership are defined in
[`22-component-model`](../20-core-domain/22-component-model.md).

`PB` states `dn=20` rather than letting the sizer choose, and that is deliberate: at the
recirculation flow, DN15 comes to **152 Pa/m** against a 150 Pa/m target and DN20 to 35 Pa/m, so the
rule lands on DN20 by a 1 % margin. A reference circuit whose transport times swing 40 % on the third
significant figure of a viscosity is not a reference. Stating the size makes every number below exact
and checkable, and matching the recirculation branch to the path it serves is what a designer does
anyway.

**`HE1` drops `in=20`.** In the steady circuit that stated value is a constraint that promotes
`3WV.position` into a solver unknown ([`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md))
— the circuit is *solved* into position. `TC1` does the same job dynamically. Leaving both would mean a
constraint and a controller fighting over one actuator, which is over-specification wearing a control
system's clothes.

**`TC1` and the `schedule` section are the two new language features M4 needs.** Both are ordinary
statements: a controller is a component declaration whose `measure` and `actuate` parameters are
references rather than quantities, and a disturbance is one line under a `schedule` header. Neither
adds a statement kind ([`12-grammar`](../10-language/12-grammar.md)).

**Transport figures.** These are the numbers every transient and controller document must reproduce.
`PB` carries the **recirculation** flow of **0.0764 kg/s** at 50 °C — not the secondary 0.2394 kg/s,
which is the mistake to avoid, since `PB` is on the branch that returns to `N2` rather than the one that
leaves through `3WV.c`.

| Quantity | Value | Derivation |
|---|---|---|
| `PB` bore | **21.7 mm** | DN20, EN 10255 — 26.9 mm OD less 2 × 2.6 mm wall |
| Velocity in `PB` | **0.209 m/s** | 0.0764 / 988 = 7.73 × 10⁻⁵ m³/s over 3.699 × 10⁻⁴ m² |
| Thermal-cell volume (2 m equivalent length) | **0.740 l** | π/4 × 0.0217² × 2 |
| Thermal-cell residence time τ | **9.6 s** | 0.740 × 0.988 / 0.0764 |
| Dead time, `HE1` → `N2` | **≈ 38 s** | four pipe cells in series |
| CFL step limit | **8.6 s** | 0.9 × 9.6, set by the smallest control volume |

Every figure uses the **inside** diameter, never the DN number. At 21.7 mm rather than 20 mm the
segment volume is 18 % larger and every transport time in the model moves with it
([`02-glossary`](02-glossary.md)).

The velocity is below the 0.3 m/s sedimentation minimum, so this circuit also produces one `FS4005`
(info) — which is correct and worth keeping: a recirculation branch genuinely carries low flow, and a
reference circuit that emits a real design warning is a better test of the warning than one that
does not.

**Inference inventory:** 7 declared (`HE1`, `3WV`, `PU1`, `P1`, `PB`, `N1`, `N3` — `TC1` has no ports
and is not in the flow graph), 1 from I1 (`N2`), 4 from I2 (`PU1__HE1`, `HE1__3WV`, `3WV__PB`,
`3WV__P1`), none from I3. **Seven nodes, twelve flow components**, plus four internal nodes and five
sub-pipes from `PB`'s discretization.

Keeping the syntax reference alongside the circuit references is deliberate. It shows what the
language should feel like to write; the circuit references show what it takes to be solvable, and the
gap between them is most of what `/docs`'s tutorial has to teach.

#### The storage header — tank and thermal-order reference

```fluidscript
fluidscript 1
circuit storageHeader
fluid dynamic water
show temperature

S1 node t=60 p=300 flow=0.12
S2 node t=45 flow=0.08
T1 tank volume=300 layers=5 t1=25 t2=30 t3=40 t4=50 t5=60 in1_elevation=90% in2_elevation=30% out1_elevation=90% out2_elevation=30%
RAD_NETWORK node flow=0.12
AHU_NETWORK node flow=0.08

connections
S1 - T1.in1
S2 - T1.in2
T1.out1 - RAD_NETWORK
T1.out2 - AHU_NETWORK
```

This reference deliberately terminates the two sources and two heating networks at boundary nodes; it
tests the storage control volume and whole-plant layout without importing the future heat-pump model.
At a terminal, positive `flow` follows the nominal connection direction, so `S1` and `S2` inject
0.20 kg/s in total and the two network boundaries extract the same 0.20 kg/s. `S1` supplies the one
pressure datum. Bare `volume=300` is 300 dm³ (`D-32`); each of the five layers therefore holds 60 dm³.

Layer indices run bottom to top. The normalized elevations map `30%` to layer 2 and `90%` to layer 5.
At t = 0, `S1` replaces 60 °C water in layer 5 at the same temperature, while `S2` replaces 30 °C
water in layer 2 with 45 °C water. With the validation fixture's 1000 kg/m³ incompressible water,
layer 2 contains 60 kg and its initial derivative is:

```
dT₂/dt = (0.08 kg/s ÷ 60 kg) × (45 − 30) K = 0.020 K/s = 1.20 K/min
```

All other layer derivatives are initially zero. The full implementation integrates enthalpy with the
real substance rather than this constant-cp shortcut. If layer 2 later becomes buoyantly unstable
relative to layer 3, the inversion-remixing rule conserves their combined mass and enthalpy.

**Layout.** `S1` and `S2` share the left source stage, `T1` occupies the central storage stage, and
`RAD_NETWORK` and `AHU_NETWORK` share the right consumer stage. The four branches may stack
vertically, but their stage X coordinates may not interleave. The result remains in that order during
the transient; only arrows and temperatures change.

**Inference inventory:** five declared components, no inferred components. The tank materializes
`in1`, `in2`, `out1`, and `out2` from the qualified endpoints; its other indexed ports do not exist in
this model.

## Invariants

1. Every `R-` id is claimed by at least one document's `traces_to:`.
2. Every document's `traces_to:` names only ids defined here.
3. `R-` ids are never renumbered or reused — a withdrawn requirement is struck through, not deleted.
4. No document in `plan/` specifies work that contradicts a stated non-goal.

## Acceptance criteria

- [ ] Every `R-` id above appears in the `traces_to:` frontmatter of at least one document.
- [ ] Every document in `plan/` has a non-empty `traces_to:`.
- [ ] Every non-goal is contradicted by no document in the tree.
- [ ] **Every `fluidscript` block in this document parses under
      [`12-grammar`](../10-language/12-grammar.md) with no changes to its text**, extracted and run in
      CI. Every reference is included, not only the syntax reference: both the syntax reference and
      the cooling loop previously failed to parse while this criterion was recorded as met, because
      nothing mechanically checked it.
- [ ] Every worked example in `plan/` that uses a reference circuit names which one, and reproduces
      the solved-state figures above (`D-11`, `D-16`).
- [ ] The cooling loop solves, and its recirculation flow is non-zero — the check that the pump is in
      the loop that needs it.
- [ ] The demand-step loop's measured node `N2` does not move for at least 30 s after the `t = 60 s`
      step — the check that the transport path between the disturbance and the measurement is real
      (`D-16`). Deleting `PB` from the circuit must make this test fail.
- [ ] No document counts three I1 nodes or six inferred components in the cooling loop; the boundary
      nodes are declared.
- [ ] The storage header begins with two source groups on the left, places `T1` between them and the
      two right-side consumer groups, and keeps those X stages stable while frames update.
- [ ] The storage header's initial layer-2 derivative is 0.020 K/s with the constant-property fixture;
      total tank mass and enthalpy meet the conservation tolerance throughout the run.

## Open questions

None. `D-28` bounds v1 to hydronic circuits, and
[`07-quality-attributes`](07-quality-attributes.md) plus
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md) own the measurable accuracy claim.
