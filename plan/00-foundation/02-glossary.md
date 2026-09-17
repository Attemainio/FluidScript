---
id: 02-glossary
title: Glossary
tier: 00-foundation
status: reviewed
owns: [canonical term spellings, domain vocabulary, identifier casing conventions]
depends_on: [01-vision-and-scope]
traces_to: [R-01, R-09, R-28, R-29, R-37, R-38, R-39, R-41, R-44, R-45, R-46, R-47, R-48, R-50]
open_questions: 0
last_review_pass: 6
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
| **Subcircuit** | `circuit` + `inlet`/`outlet` | `Circuit` | A circuit declared in the same script that attaches to a parent circuit at two explicitly named nodes. It is an ordinary circuit with a parent, not a distinct type. **Not a subsystem** — see below (`D-33`). |
| **Inlet** / **Outlet** | `inlet`, `outlet` | `BoundaryRole.Inlet` / `Outlet` | The boundary nodes where fluid enters and leaves the model (`D-64`, spelled so by `D-115`). Each has exactly one connection. Not *supply* and *return*, which name the two pipes of a hydronic circuit and the layout's route layers. |
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
| **Elevation** | `elevation` | `Elevation` | The absolute height of a component above the project datum, in metres (`D-70`). A property of position: a component has one and every port of it sits there. Only a **pipe** and a bare connection span two, and a pipe's **rise** is `z(out) − z(in)` from what it connects — a pipe states no elevation of its own. Omitted, it is inherited from whatever the component is wired to without a pipe in between, and 0 only where nothing states one (`D-95`); never sized. |
| **Rise** | — | `Rise` | A pipe's outlet height minus its inlet height, derived from the elevations of its two ends. Carries `ρgΔz` in the pipe's momentum row and `−ṁgΔz` in its energy injection. Was `pipe.elevation` before `D-70`; the word moved because a rise is not a position. |
| **Level** | `in1_level`…`out16_level` | `NormalizedLevel` | A tank port's position between the vessel's bottom (0) and top (1), used only to pick the layer the port talks to. Thermal metadata, not metres: no hydrostatic term is formed from it. Was `in1_elevation` before `D-70`; renamed so that `elevation` means one thing. |
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
| **Controller** | `Controller` | A non-flow model element that measures one resolved property and actuates one writable parameter during a transient. Script keyword `controller`; `pi`, `pid` and `p` are aliases, never names (`D-40`). Its declaration carries the algorithm and gains; the `control` binding carries what it measures, actuates and targets. |
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
| **Placement** | `Placement` | Core's decision about where a component sits (`D-103`): an inner box the symbol is drawn in and an outer box grown by the margin, in world units, with every anchor and the label position. On the wire as `layout.placements`. |
| **World units** | — | The canvas coordinate system, independent of zoom. Not pixels, not millimetres; a pump is 1×1 (`D-103`). |
| **Symbol** | `Symbol` (TS) | The drawn glyph for a component kind. |
| **Route** | `Route` | The orthogonal polyline a connection is drawn along, solved by Core and carried as `layout.routes` (`D-103`). |
| **Thermal stage** | `ThermalStage` | A source, conversion/storage, consumer, or neutral component group assigned one left-to-right heat-progression rank. It does not replace fluid-flow direction (`D-31`). |
| **Header layout** | — | *Withdrawn* (`D-107`): the rails-and-U picture of `D-38`. A distribution circuit is now a set of stacked branches (`28` part D, source §26): the main direction stays left to right, each child branch is a rigid group, the children stack perpendicular to the main flow. |
| **Loop rectangle** | — | The arrangement distributing one closed loop's components around the four sides of a rectangle; which side each member takes is the four-side partition search of `28` part D, a candidate until a ladder step proves it (`D-107`). |
| **Spacing** | `spacing` | The margin every component keeps from every other, in world units: each inner box grown by it is the outer box no other inner box may enter (`D-103`). 0.5 unless the script states it. A presentation value — never a layout hint (`D-37`) — but since `D-103` the layout solver reads it, because the solver is Core's. |
| **Flow vector** | `FlowDirection` | The direction the fluid flows at a port, one of the four unit vectors, rotated with the symbol (`D-106`). **Not** the outward normal of the box: a pump pumping right has `(1,0)` at both ports. On the wire as an anchor's `direction` once `28`'s stage 2 lands. |
| **Inner anchor / outer anchor** | `PlacedAnchor.At`, `PlacedAnchor.Along(m)` | Where a port meets the symbol's boundary, and the same point moved one margin along the port's outward direction, on the outer box's edge (`28` A3). The stub between them is straight; a pipe turns only from the outer anchor. |
| **Clearance** | — | The minimum gap between two components' inner boxes, `max` of their margins, hard (`D-106`). Outer boxes may overlap; that is a soft **interference**, counted, not rejected. |
| **Envelope** | — | A route's clearance as the union of axis-aligned rectangles one margin around its segments (`28` A7). A pipe through an unrelated inner box is hard; through a margin, soft. |
| **Sequential group** | — | A chain placed by direction propagation alone: each member's inlet flow equals the previous outlet flow, placed on one axis at the clearance (`28` part D, source §7–10). A candidate until the ladder proves it (`D-107`). |
| **Loop group** | — | A simple loop solved once and then rigid: its parent may translate, rotate and, where allowed, mirror it, never re-lay it (`28` A8, part D). |
| **Group** (layout) | `LayoutGroup` | A set of components laid out together and treated by its parent as one object with bounds, ports and allowed transforms (`28` A8); carried as `layout.groups`. |
| **Layout diagnostic text** | `SceneText` | The text form of a scene — placements with inner/outer boxes and anchors with flow vectors, routes with their envelopes, groups, validation totals, interference — written per ladder step and per sample *before* any assertion, and read instead of the picture (`28` A10). |
| **Layout ladder** | — | The process that builds the layout engine one rule at a time (`D-107`, [`29`](../20-core-domain/29-layout-ladder.md)): each **step** adds one component to the previous step's script, the engine draws it, the user corrects the picture, the correction becomes a numbered rule in `28` part C. |
| **Boundary stub** | — | *Withdrawn* (`D-108`): a boundary node the binder added to terminate an open port (`23`, rule I3) is laid out as a node like any other -- the junction's box, an outer boundary, a place of its own, its name in the picture and the text (`28` A6). |
| **Transform class** | `TransformClass` | Which transforms a kind admits, a fact about the kind and hard (`28` A4, `D-108`): `free` (four quarter turns, mirrored or not), `standing` (no turn; the two mirrors and both -- every exchanger, the heat pump), `upright` (identity and the left-right mirror -- the tank). |
| **Flow-oriented graph** | — | The circuit graph with every connection directed by its ports' nominal flow vectors (`28` A3): roles, then propagation, never the solved flow. `28` B's H9 (every flow loop clockwise) and H10 (heat left to right) are measured on it. |
| **Fallback column** | `group fallback` | Where the layout engine puts a component no rule covers yet: a column below everything placed, its connections drawn as plain L's, never guessed (`28`, `29`). |
| **Equipment list** | `EquipmentList` | The per-circuit table of every device and its design-point values, projected from the model contract and exported for a contractor. **Never "equipment schedule"** — `schedule` is the time-domain block keyword. Post-v1 ([`73-equipment-list`](../70-future/73-equipment-list.md)). |
| **Active document** | — | The one open document that performs presentation work — layout, colour, DOM. Others retain their state, and a running transient in one keeps receiving and reconstructing frames (`D-39`, `D-42`). |

## Banned and confusable terms

| Do not write | Write instead | Why |
|---|---|---|
| "pipe segment", "leg", "run" | **branch** (solver), **pipe** (declared component), **pipe cell** (thermal control volume), or **sub-pipe** (lowered hydraulic element) | The old words hid four different concepts. |
| "flow rate" unqualified | **mass flow** or **volume flow** | They differ by density, which changes with temperature. |
| "temperature drop" for a heat exchanger | **temperature difference** | "Drop" implies a loss; a heat exchanger may raise it. |
| `Cv` | `Kv` | Different unit systems; see above. |
| "pressure loss" and "pressure drop" mixed | **pressure drop** | Pick one; this is it. |
| "equipment schedule" | **equipment list** | `schedule` is the time-domain block keyword ([`12-grammar`](../10-language/12-grammar.md)). Two things called a schedule is one too many. |
| "primary side" / "secondary side" in Core or on the wire | **side 1** / **side 2** (`in`, `out` and `in2`, `out2`) | Which side is which is a solved outcome, not a declaration (`22`). `primary`/`secondary` are display names in the equipment list and nowhere else. |
| "pressure reference" | **pressure datum** or **pressure boundary** | Two different things; the word hid the difference and made every open circuit look over-specified. |
| "simulation" for a steady-state solve | **solve** | Reserve "simulation" for the transient case. |
| "pinch" for one exchanger's minimum ΔT | **approach** | "Pinch analysis" is a plant-wide network method and is out of scope; using the word for a single exchanger guarantees the two get conflated. |
| "plate spacing", "channel gap" | **lamella** | Three words for one dimension. |
| "hot side" / "cold side" as parameter names | **side 1 / side 2** (`in`/`out` vs `in2`/`out2`) | Which side is hot is a solved outcome. A script that says `hot_in=40` and solves to the cold side is worse than one that says nothing. |
| "subsystem" for an inline attached circuit | **subcircuit** | A subsystem is an M6 reusable definition; a subcircuit is declared inline and attaches at named nodes. Two concepts, two words, and they must not swap (`D-33`). |
| "name", "id" or "identifier" for `400PU01` | **tag** | The identifier is what the user wrote; the tag is derived. Conflating them is the mistake `D-34` exists to prevent, and it silently breaks every consumer keyed by id. |
| "circuit id" | **circuit number** | The number is a drawing designation chosen by the engineer, not a system-assigned identity. |
| "elevation" for a tank port's 0…1 position, or for a pipe's rise | **level** for the port, **rise** for the pipe | `elevation` is an absolute height in metres on a single-height component (`D-70`). A pipe has none, a tank port's fraction is not one, and one word for three quantities is how a 32 m riser lost its return. |

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
