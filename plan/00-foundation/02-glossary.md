---
id: 02-glossary
title: Glossary
tier: 00-foundation
status: draft
owns: [canonical term spellings, domain vocabulary, identifier casing conventions]
depends_on: [01-vision-and-scope]
traces_to: [R-01, R-09, R-28, R-29, R-37, R-38, R-39, R-41, R-44, R-45, R-46, R-47, R-48, R-50]
open_questions: 0
last_review_pass: 0
---

# Glossary

## Purpose

One name per concept, spelled one way, everywhere: in C# type names, in script keywords, in the REST
contract, in `/docs`, and in UI copy. This document is the authority for that mapping. Three documents
calling the same thing a "leg", a "branch", and a "segment" is the single most common way a large
specification becomes unimplementable, and it is invisible until someone tries to build it.

## Responsibilities

**Owns.** The canonical term for every domain concept, its script keyword, its C# type name, and its
`/docs` spelling. The casing conventions that map between those three.

**Explicitly does not own.** The semantics of any term — a glossary entry says what a thing is called
and points at the document that defines it. Component behaviour is
[`22-component-model`](../20-core-domain/22-component-model.md); language keywords are
[`12-grammar`](../10-language/12-grammar.md).

## Casing conventions

| Surface | Convention | Example |
|---|---|---|
| Script keyword | `lower_snake_case` | `three_way_valve`, `heat_exchanger` |
| Script identifier (user-chosen) | any of `[A-Za-z0-9_]`, may start with a digit | `3WV`, `HE1`, `N_supply` |
| C# type | `PascalCase`, no abbreviations | `ThreeWayValve`, `HeatExchanger` |
| C# property for a dimensioned value | `PascalCase`, unit-free name | `Power`, `MassFlow`, `PressureDrop` |
| REST/WebSocket JSON field | `camelCase` | `pressureDrop`, `massFlow` |
| Diagnostic code | `FS` + four digits | `FS1004` |
| `/docs` page filename | `kebab-case` matching the script keyword | `three-way-valve.md` |

The script keyword is the source of truth for the other two: `three_way_valve` → `ThreeWayValve` →
`three-way-valve.md`. A component whose three names cannot be derived from each other mechanically is
a naming bug.

### Aliases are spellings, not names

`D-15` lets a user write `3_way_valve`, `mixing_valve`, `3WayValve` or `3wv` and reach
`three_way_valve`. **None of those is a name.** The canonical script keyword is the only spelling that
appears in a C# type, a `/docs` filename, a JSON `kind` field, a printer output, or anywhere in this
tree outside the alias table itself. An alias exists so a script *compiles*; it never propagates.

This keeps the derivation rule above intact — there is still exactly one keyword per kind to derive
from — and it is why `D-15` qualifies `P6` rather than breaking it.

The alias list per kind lives in [`15-semantic-model`](../10-language/15-semantic-model.md), with the
registry, because it is data the binder reads. This document owns only the rule that aliases are not
names.

## Terms

### Topology

| Term | Script | C# | Meaning |
|---|---|---|---|
| **Circuit** | `circuit` | `Circuit` | A named, numbered, connected set of components sharing one fluid and solved together. A script may declare several (`D-33`). |
| **Circuit number** | `circuit AHU 101` | `CircuitNumber` | The integer designating a circuit on a drawing. Stated in the header, or resolved automatically as the lowest unused multiple of 100 in declaration order (`D-33`). |
| **Subcircuit** | `circuit` + `supply`/`return` | `Circuit` | A circuit declared in the same script that attaches to a parent circuit at two explicitly named nodes. It is an ordinary circuit with a parent, not a distinct type. **Not a subsystem** — see below (`D-33`). |
| **Circuit role** | the header's name | `CircuitRole` | A circuit's classification — `ahu`, `radiator`, `hot_water`, `ground_loop` — resolved from the header name through a registry by `D-15`'s three stages, never a keyword. Feeds `D-31` thermal classification (`D-35`). |
| **Distribution header** | — | — | The supply and return line pair that a set of subcircuits attaches to. **Supply header** carries flow out, **return header** carries it back. |
| **Tag** | — | `Tag` | The derived equipment designation `<circuit><code><ordinal>` — `400PU01`. Core-computed metadata carried in the model contract. **Never an identifier**: the component's name is what the user wrote (`D-34`). |
| **Tag code** | — | `TagCode` | The one-to-three-letter code for a component kind within a tag — `PU`, `HE`, `TV`, `S`. A registry field on the kind, not a hard-coded table (`D-34`). |
| **Component** | — | `IComponent` | Any named model participant with parameters and a `SymbolId`. A **flow component** also has ports/equations; an **observer** reads model state without joining the fluid graph (`D-20`). |
| **Node** | `node` | `Node` | The primitive component. Carries a single fluid **state**; has no length and imposes no pressure drop of its own. |
| **Port** | — | `Port` | A named attachment point on a component. Every connection joins exactly two ports. |
| **Connection** | `A - B` | `Connection` | A directed ideal link between ports: zero length, drop, storage, and heat loss (`D-25`). Direction is nominal flow direction, not a constraint on solved-flow sign. |
| **Branch** | — | `Branch` | A maximal path between junction elements. Every flow component along one flow-group path shares the branch's one flow unknown. |
| **Pipe cell** | — | — | One equal-volume thermal control volume created by `nodes=`. A pipe with `nodes=n` has n pipe cells but n+1 hydraulic sub-pipes; the two counts are deliberately distinct. |
| **Junction element** | — | — | A terminal or a flow component with a flow group containing three or more ports. Port count alone is insufficient: a four-port exchanger has two groups of two and is not a junction (`D-19`). |
| **Flow group** | — | `FlowGroup` | Ports of one component constrained to carry the same mass flow. A Coupled exchanger has two groups; a Rated exchanger has one graph group plus an external profile; a three-way valve has one group of three. |
| **Run snapshot** | — | `RunSnapshot` | Immutable compiled model, initial state, versions, settings, schedule, and limits used by one transient. Edits create a separate draft and cannot mutate it (`D-22`). |
| **Draft revision** | — | `DraftRevision` | Current editable source and its compile result. It may be invalid without affecting an active run snapshot. |
| **Symbol definition** | — | `SymbolDefinition` | Core-owned declarative primitives, port anchors, and label anchor selected by a component's `SymbolId`; placement and SVG rendering stay in the frontend. |
| **Tank** | `tank` | `Tank` | A finite-volume liquid storage component with indexed inlet/outlet ports. `container` is an input alias, never the canonical name (`D-32`). |
| **Tank layer** | — | `TankLayer` | One equal-volume, perfectly mixed and isothermal control volume in a tank. Layers are indexed bottom to top; their stack represents stratification. |
| **Pressure datum** | — | — | The node whose pressure anchors the field. Exactly one per connected component, arbitrary, often auto-picked. **Not** the same as a pressure boundary condition. |
| **Pressure boundary** | `p` on a node | — | A real constraint holding a node at a pressure, admitting an unknown external flux. A circuit may have any number. |
| **Gauge pressure** | bare pressure, `kPa`, `bar`, `kPag`, `barg` | — | Pressure relative to the model's recorded atmosphere; the v1 script/UI default. |
| **Absolute pressure** | `kPaa`, `bara` | — | Pressure relative to vacuum, required by substance properties. Standard atmosphere is 101.325 kPa absolute in v1. |
| **Open port** | — | — | A declared port with no connection. Terminated automatically with a boundary node (`R-06`). |
| **Implicit node** | — | — | A node the binder inserts because two components were connected directly and the graph needs a state between them. |
| **Subsystem** | — | `Subsystem` | A circuit definition *reused* from elsewhere — composition and instantiation, not attachment. **Phase M6.** A subcircuit is declared inline and attaches hydraulically; a subsystem is a reusable definition referenced by name. The words are close and the concepts are not. |

### Thermodynamics

| Term | C# | Meaning |
|---|---|---|
| **Substance** | `ISubstance` | The thing flowing: a pure fluid, a mixture, or humid air. The abstraction over SharpProp. |
| **State** | `FluidState` | A fully determined thermodynamic point — two independent properties plus composition — from which every other property follows. |
| **Property** | — | A scalar derivable from a state: temperature, pressure, enthalpy, density, viscosity, specific heat. |
| **Quantity** | `Quantity` | A number with a dimension. Never a bare `double` across a public boundary. |
| **Dimension** | `Dimension` | What kind of quantity: power, temperature, pressure, mass flow, length. |
| **Psychrometrics** | — | The humid-air property set: dry-bulb, wet-bulb, humidity ratio, relative humidity, dew point, enthalpy. |
| **Head** | `Head` | Pump energy per unit weight of fluid, in metres of the pumped fluid. Distinct from **pressure rise**, which is head × ρ × g. Both exist; the script accepts either and the docs must never use them interchangeably. Its explicit unit symbol is `mH2O`, never a bare `m` — that belongs to `Length`, and one symbol may not mean two dimensions ([`13`](../10-language/13-type-and-unit-system.md)). |
| **DN** | `NominalDiameter` | Nominal diameter **designation**, dimensionless, and its own dimension kind — never a `Length`. DN25 steel pipe has a 33.7 mm outside diameter and a 27.3 mm bore. **DN is not a length**: treating it as one is a 16 % area error and roughly a factor of two in pressure gradient. Hydraulics reads the catalogue's inside diameter; the script writes `dn=25` and reads back `P1.diameter` for anything dimensional. |
| **KV value** | `Kv` | Valve flow coefficient: m³/h of water at 1 bar differential. **Always `Kv`, never `Cv`** — `Cv` is the imperial cousin and mixing them is a factor-of-1.156 error. |
| **Authority** | `Authority` | A control valve's pressure drop at design flow divided by the drop across the controlled branch. The number that decides whether a valve actually controls anything. |
| **UA** | `Ua` | Overall conductance, W/K. The product of area and overall heat transfer coefficient, and the one number that expresses an exchanger's thermal size independently of how it is built. |
| **NTU** | `Ntu` | Number of transfer units, `UA/C_min`. Dimensionless. |
| **Capacity rate** | `CapacityRate` | `ṁ·cp` for one side of an exchanger, W/K. `C_min` and `C_max` are the smaller and larger of the two, and `C_r = C_min/C_max`. **Which side is `C_min` is a solved outcome and can change during a solve.** |
| **Effectiveness** | `Effectiveness` | Actual duty divided by the thermodynamic maximum `C_min·(T_hot,in − T_cold,in)`. Between 0 and 1. What the ε-NTU relation returns. |
| **LMTD** | `Lmtd` | Log-mean temperature difference. A **reported property**, never a solver residual — it is singular when the two end differences are equal, which is a common design point ([`22`](../20-core-domain/22-component-model.md)). |
| **Approach** | `Approach` | The minimum temperature difference between two streams in an exchanger. For counterflow it occurs at one end. **Not the same as pinch analysis** — see below. |
| **Pinch analysis** | — | A plant-wide heat-integration *method*: composite curves, a ΔT_min target, and stream matching across many exchangers. **Out of scope** ([`72-roadmap`](../70-future/72-roadmap.md)). Do not use "pinch" for a single exchanger's minimum approach; write **approach**. |
| **Arrangement** | `FlowArrangement` | How the two streams run relative to each other: `counter`, `parallel`, `crossflow`. Sets which ε-NTU relation applies. |
| **Lamella** | `Lamella` | The channel gap between adjacent plates in a plate exchanger, in metres. The hydraulic diameter is about twice it. Also called plate spacing or channel gap; **`lamella` is the term here**. |
| **Duty / Rated / Coupled exchanger mode** | — | Duty has no second-side evidence and transfers a stated `power`. Rated uses stated secondary profile properties with open secondary ports. Coupled connects both secondary ports to a solved stream. Rated+Coupled are **extended** modes using ε-NTU; mode is inferred, never declared (`D-19`). |

### Solving

| Term | C# | Meaning |
|---|---|---|
| **Steady state** | `SteadyStateSolution` | The equilibrium: all time derivatives zero. |
| **Transient** | `TransientSolution` | Time-domain evolution from an initial state under changing boundary conditions. |
| **Frame** | `TransientFrame` | One solved instant of a transient run: simulation time plus every component's state. |
| **Controller** | `Controller` | A non-flow model element that measures one resolved property and actuates one writable parameter during a transient. |
| **Schedule** | `Schedule` | The ordered set of time-based disturbances declared after the `schedule` section marker. |
| **Residual** | `Residual` | How far an equation is from being satisfied at the current guess. The solver drives these to zero. |
| **Unknown** | `Unknown` | One scalar the solver is free to change. The count of unknowns must equal the count of equations. |
| **Well-posed** | — | Unknowns equal equations, the Jacobian is non-singular, and every branch is reachable from the pressure datum. |
| **Sizing** | `SizingResult` | Choosing a component parameter the user left unspecified, subject to their explicit constraints. |
| **Design point** | — | The operating condition sizing is performed at. Distinct from any solved operating point. |

### Language

| Term | C# | Meaning |
|---|---|---|
| **Script** | — | The source text. |
| **Statement** | `Statement` | One logical line: a header, a declaration, a connection, or a binding. |
| **Declaration** | `ComponentDeclaration` | A statement introducing a named component with optional parameters. |
| **Binding** | `LetBinding` | A `let` statement naming a value. |
| **Reference** | `MemberReference` | `HE1.dp` — reading a resolved property of another component. |
| **Trivia** | `Trivia` | Whitespace, blank lines, and `#` comments (`D-13`). Preserved through the round trip (`R-25`). |
| **Diagnostic** | `Diagnostic` | A coded, spanned message: error, warning, or info. |
| **Span** | `TextSpan` | A start offset and length into the script. What an editor squiggle is drawn from. |
| **Project directive** | `project` | — | The global statement naming the project and setting the default solve mode for every circuit in the file. Follows the version directive (`D-37`). |
| **Control binding** | `control` | `ControlBinding` | The statement joining a controller definition to the parameter it actuates and the property it measures, with named arguments. Distinct from the controller *declaration*, which carries the algorithm and gains (`D-40`). |

### Rendering

| Term | C# / TS | Meaning |
|---|---|---|
| **Layout hint** | `LayoutHint` | Core's advice about placement — ordering, port side, flow direction, grouping. Not coordinates. |
| **Placement** | `Placement` (TS) | The frontend's decision about where a component actually sits, in world units. |
| **World units** | — | The canvas coordinate system, independent of zoom. Not pixels, not millimetres. |
| **Symbol** | `Symbol` (TS) | The drawn glyph for a component kind. |
| **Route** | `Route` (TS) | The polyline a connection is drawn along. |
| **Thermal stage** | `ThermalStage` | A source, conversion/storage, consumer, or neutral component group assigned one left-to-right heat-progression rank. It does not replace fluid-flow direction (`D-31`). |
| **Header layout** | — | The layout mode drawing a distribution circuit as a supply line along the top and a return line along the bottom with its subcircuits stacked between them. The alternative mode is the **loop rectangle** (`D-38`). |
| **Loop rectangle** | — | The layout mode distributing one closed loop's components around the perimeter of a rectangle. The original and still the default for a circuit with no subcircuits (`D-38`). |
| **Spacing** | `spacing` | The minimum gap between adjacent component bounding boxes, in world units. A presentation value carried through Core untouched — never a layout hint (`D-37`). |
| **Active document** | — | The one open document that renders and streams frames. Others retain their state and any running transient (`D-39`). |

## Banned and confusable terms

| Do not write | Write instead | Why |
|---|---|---|
| "pipe segment", "leg", "run" | **branch** (solver), **pipe** (declared component), **pipe cell** (thermal control volume), or **sub-pipe** (lowered hydraulic element) | The old words hid four different concepts. |
| "flow rate" unqualified | **mass flow** or **volume flow** | They differ by density, which changes with temperature. |
| "temperature drop" for a heat exchanger | **temperature difference** | "Drop" implies a loss; a heat exchanger may raise it. |
| `Cv` | `Kv` | Different unit systems; see above. |
| "pressure loss" and "pressure drop" mixed | **pressure drop** | Pick one; this is it. |
| "pressure reference" | **pressure datum** or **pressure boundary** | Two different things; the word hid the difference and made every open circuit look over-specified. |
| "simulation" for a steady-state solve | **solve** | Reserve "simulation" for the transient case. |
| "pinch" for one exchanger's minimum ΔT | **approach** | "Pinch analysis" is a plant-wide network method and is out of scope; using the word for a single exchanger guarantees the two get conflated. |
| "plate spacing", "channel gap" | **lamella** | Three words for one dimension. |
| "hot side" / "cold side" as parameter names | **side 1 / side 2** (`in`/`out` vs `in2`/`out2`) | Which side is hot is a solved outcome. A script that says `hot_in=40` and solves to the cold side is worse than one that says nothing. |
| "subsystem" for an inline attached circuit | **subcircuit** | A subsystem is an M6 reusable definition; a subcircuit is declared inline and attaches at named nodes. Two concepts, two words, and they must not swap (`D-33`). |
| "name", "id" or "identifier" for `400PU01` | **tag** | The identifier is what the user wrote; the tag is derived. Conflating them is the mistake `D-34` exists to prevent, and it silently breaks every consumer keyed by id. |
| "circuit id" | **circuit number** | The number is a drawing designation chosen by the engineer, not a system-assigned identity. |

## Worked example

Applying the mapping to the brief's `3WV three_way_valve` line:

| Surface | Value |
|---|---|
| Script keyword | `three_way_valve` |
| Script identifier | `3WV` (legal: identifiers may start with a digit) |
| C# type | `ThreeWayValve` |
| C# instance name in the graph | `"3WV"` — user identifiers are data, never C# symbols |
| JSON `kind` field | `"three_way_valve"` — the script keyword crosses the wire unchanged |
| `/docs` page | `docs/functions/three-way-valve.md` |
| UI label | "Three-way valve" |

The JSON carries the script keyword rather than the C# type name deliberately: the wire contract is
shared with `/docs` and with anything generating scripts, and those speak script, not C#.

## Invariants

1. Every term used in more than one document appears here.
2. A term's script keyword, C# type, and `/docs` filename derive from each other by the stated rule.
3. No normative contract uses a term from the "Do not write" column; glossary definitions, rejected
   alternatives, diagnostics about forbidden input, and historical decision rationale may quote one.
4. A term means one thing across the whole tree; one thing has one term.

## Acceptance criteria

- [ ] No normative contract in `plan/` uses a term from the "Do not write" column except while
      defining, rejecting, or diagnosing that spelling.
- [ ] Every component kind in [`22-component-model`](../20-core-domain/22-component-model.md) has all
      three names here, and they derive from each other by the stated rule.
- [ ] Every term used in more than one document appears in this glossary.

## Open questions

None. v1 accepts `kv=` only. `cv=` produces an unknown-parameter diagnostic that names Kv and requires
the user to convert explicitly, avoiding a silent imperial/SI coefficient change (`D-30`).
