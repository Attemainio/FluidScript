# How FluidScript works, from text to picture

This page is for someone who has just opened the repository and wants to know what happens between
typing a script and seeing a solved, coloured diagram. It walks every stage in order, says what goes
in and what comes out, and names the folder and the class that does it, so that a reader can go
straight from a question to the code. It assumes nothing beyond the tutorial.

The example throughout is the cooling loop sample, `samples/m2-cooling-loop.fluid`, and every number
quoted is from its solve report:

```
fluidscript 1
circuit coolingLoop
fluid water
show temperature

HE1 heat_exchanger power=30 in.t=20 out.t=50
3WV three_way_valve
PU1 pump

connections
N1 - N2                    # primary supply into the mixing node
N2 - PU1                   # the secondary pump drives the loop
PU1 - HE1
HE1 - 3WV
3WV - N2                   # recirculation branch, closes the secondary loop
3WV - N3 length=25 dn=25   # primary return, a pipe

N1 inlet t=6 p=300         # fluid enters here
N3 outlet p=280            # and leaves here
```

## The one-minute version

```
 script text
     │  1  lexer            characters → tokens
     │  2  parser           tokens → syntax tree, one statement per line
     │  3  binder           syntax tree → semantic model (names, kinds, typed values)
     │  4  lowering         semantic model → circuit graph (components, nodes, branches)
     │  5  sizing           the graph, with every missing size chosen and re-lowered
     │  6  well-posedness   does this graph have exactly one answer?
     │  7  equations        unknowns and residuals laid out as one vector each
     │  8  seed             a first guess that is mass-consistent
     │  9  Newton           the state where every residual is zero
     │ 10  outer loop       size → solve → evaluate deferred values → size again, until nothing moves
     │ 11  layout           the solved graph placed and routed in world units
     │ 12  model contract   everything above as one JSON document
     ▼
 the editor, the canvas, the hover card, the log, the exporters
```

Stages 1 to 12 are in one .NET library, `src/FluidScript.Core`. Stage 13, the HTTP host that runs
them for a request, is `src/FluidScript.Api`. Stage 14, the React application that sends the text
and draws the answer, is `frontend/`. Nothing in the frontend computes a pressure or a coordinate: the
library solves the physics and the geometry, and the browser only draws.

Two rules hold at every stage, and knowing them explains a lot of the code's shape:

- **No stage throws on what the script says.** A script being edited is malformed most of the
  time. A line that cannot be read, a name that does not resolve, a circuit that has no answer: each is
  a *diagnostic* attached to the result, and the later stages do as much as they still can.
- **Everything is SI inside.** Kilopascals, degrees Celsius and cubic metres per hour exist only at
  two boundaries: where the script is read, and where the answer is written to the wire.

## The stages, one by one

### 0. The version gate

**In:** the raw text. **Out:** which language major to read it as. **Who:** `Compatibility/ScriptCompatibility`.

Before anything parses, the first meaningful line is checked for `fluidscript 1`. This runs on the
text rather than on a syntax tree because the parser's own rules depend on the answer. A file that
names a major this build cannot read comes back as diagnostics alone.

### 1. Lexing: characters into tokens

**In:** text. **Out:** a token stream that still holds every character. **Who:** `Syntax/Lexer`.

`HE1 heat_exchanger power=30 in.t=20 out.t=50` becomes identifier, identifier, identifier, `=`,
number, and so on.
Spaces, comments and line breaks are kept as *trivia* attached to the tokens, not discarded. That is
deliberate: the printer in the next stage has to reproduce the file byte for byte. Codes in the
`FS10xx` range are the lexer's.

### 2. Parsing: tokens into a syntax tree

**In:** tokens. **Out:** a `ParseResult` holding one statement per line. **Who:** `Syntax/FluidScriptParser`
and `Syntax/LineParser`; the tree's node types are in `Syntax/Ast`.

The language is line-oriented, so the parser reads one line into one statement: a component
declaration, a connection, a `let`, a `show`, a section header. A line that cannot be read becomes a
`MalformedStatementSyntax` that still holds its tokens, so the rest of the file parses and the
printer can still reproduce the broken line. `FS11xx` codes are the parser's.

Two more classes live here and matter more than their size suggests:

- `SyntaxPrinter` turns the tree back into text and must satisfy `Print(Parse(x)) == x` for every
  input, including malformed ones. The canvas writes edits back into the script through it, and a
  printer that tidied while printing would turn a one-character change into a forty-line diff.
- `Formatter` is the tidy-up, run only when the user asks. It is a command, never a side effect.

### 3. Binding: names and values get a meaning

**In:** the syntax tree. **Out:** a `SemanticModel`. **Who:** `Binding/Binder`, using `Language/ComponentRegistry`
and `Units/`.

The binder answers three questions for every line:

- **What is this?** `pump` is looked up in the `ComponentRegistry`, the table of every component
  kind: which parameters it accepts, each one's dimension, its default, and its *omission policy*,
  which says what happens when the script leaves it out (usually: size it). `3WV.b` is checked
  against the kind's ports.
- **What does this number mean?** `power=30` is a power in kilowatts, `t=6` a temperature. Values become
  `Quantity` objects with a `Dimension`, so the type system can refuse `20 °C + 30 °C` while allowing
  `20 °C + 30 K`. Everything is converted to SI here and stays SI until the wire.
- **What is stated and what is absent?** A parameter the script wrote is a *constraint* the circuit
  must satisfy. One it did not write is *absent*, never zero and never null-as-a-value, and a later
  stage decides it. This distinction is the single most important idea in the model.

`let` bindings and expressions such as `kv=0.7*CV2.kv` are evaluated by `ExpressionEvaluator` where
they can be; an expression that needs a solved value is recorded as *deferred* and evaluated after a
solve (stage 10). `FS12xx` to `FS15xx` are the binder's codes.

### 4. Lowering: the model becomes a graph

**In:** the semantic model. **Out:** a `CircuitGraph`. **Who:** `Topology/Lowering`, building components
through `Topology/ComponentFactory`; the component classes are in `Components/`.

This is where the physics objects are made. For each declaration the factory builds the class that
carries its equations: `Pump`, `Pipe`, `Valve`, `ThreeWayValve`, `HeatExchanger`, `Tank`, and
`CircuitNode` for a node. Each takes its stated parameters and the registry's defaults, in SI.

Then the connections are walked. Two rules produce a graph that is larger than the script:

- **Components connect to nodes, never to each other.** `PU1 - HE1` gets a node `PU1__HE1` put
  between them, because the temperature and pressure between the pump and the exchanger have to
  live somewhere. The cooling loop names three nodes, `N1`, `N2` and `N3`, and the graph has six:
  `PU1__HE1`, `HE1__3WV` and `3WV__N3__in` are inferred.
- **A connection with pipe parameters is a pipe.** `3WV - N3 length=25 dn=25` becomes a `Pipe`
  named `3WV__N3` with a node in front of it.
- **A run of components between two junctions is a branch.** Branches are what carry a flow. The
  cooling loop has four: the primary supply, the coil run from the mixing node through the pump and
  the exchanger to the valve, the recirculation leg, and the primary return.

After this point nothing knows a script existed. The graph holds names and components only, which
is what lets the solver be tested from a hand-built graph. Instruments, controllers and other
components with no ports are dropped here. `FS15xx` and `FS22xx` codes about topology come from this
stage and the next. [How a script becomes a circuit](how-a-script-becomes-a-circuit.md) draws this
graph for the example.

### 5. Sizing: every missing number is chosen

**In:** the graph and the model. **Out:** a re-lowered graph with sizes, each with a *basis* sentence.
**Who:** `Solvers/OuterLoop.Prepare`, calling the rules in `Sizing/` (`PipeSizer`, `PumpSizer`,
`ValveSizer`, `ExchangerSizer`, `ThermalSizer`) with the constants in `SizingDefaults` and the
catalogue rows in `Catalogs/`.

A pipe cannot be built without a bore, so a first, throw-away graph is lowered with provisional
sizes purely so that flows can be estimated on it. Flow estimates come from the stated duties and
flows, not from the provisional sizes, so nothing depends on the placeholder. The rules then run.
In the cooling loop:

- The return pipe's `dn=25` becomes the catalogue's 27.3 mm bore. A DN is a designation, not a
  diameter, and the catalogue row says where the bore comes from.
- `HE1.flow` is set to 0.239 kg/s, "the flow HE1's 20 kPa is measured at": the exchanger's default
  design drop needs a flow to be measured at, and 30 kW across the stated 20 to 50 °C is that flow.
- `3WV.kv` is set to Kv 2.5, the R5 preferred number that drops 12 kPa at that flow, inside the
  3 to 15 kPa band a mixing valve is selected in.

Every chosen value carries a sentence saying why, which is what the hover card shows as "sized".
The graph is lowered again from the chosen sizes, and that graph is the one solved.

### 6. Well-posedness: is there exactly one answer?

**In:** the sized graph. **Out:** a `CountingTable` of unknowns against equations, and the list of
parameters *promoted* to unknowns. **Who:** `Topology/WellPosedness`, with `HydraulicBlocks` for which
loops a pump actually drives.

A circuit is solvable when it has as many independent equations as unknowns. Every stated value is
an equation, and when the script states more than the physics can absorb, something has to give:
the count finds a parameter no constraint has claimed and *promotes* it to an unknown the solver
finds. The cooling loop promotes two. `PU1 pump` states nothing, so its head is promoted (the solve
finds 2.48 m). `HE1` states its power and both temperatures, which fixes its flow at 0.239 kg/s,
and the only thing that can deliver that flow through the mixing node is the valve's position, so
`3WV.position` is promoted (the solve finds 0.52). The count comes out at 20 unknowns against
20 equations. The result is reported in the `FS22xx` range and, in long form, in the solve report.
[Why a circuit has one answer](why-a-circuit-has-one-answer.md) is the reader's guide to this stage.

### 7. The fluid

**In:** a pressure and a temperature or enthalpy. **Out:** a `FluidState` with density, viscosity,
specific heat. **Who:** `Fluids/`: `ISubstance` is the interface, `Water`, `HumidAirSubstance` and
`Refrigerant` implement it.

Exactly one class, `PropertyBackend`, references the property library (SharpProp); `Water` and
`Refrigerant` ask it for properties, and everything else depends on `ISubstance`, so a different
backend is a one-file change. `Water` also enforces the validated range (liquid, 0 to 120 °C, up to
1000 kPa absolute) rather than trusting a backend that returns a plausible density at 5000 °C.

### 8. The equation system

**In:** the graph and the fluid. **Out:** a residual function: one vector of unknowns in, one vector of
equation residuals out. **Who:** `Solvers/SystemLayout` (which unknown sits where), `Solvers/EquationLayout`
(which equation is which row), `Solvers/EquationSystem` (evaluating them).

Each component declares its unknowns and equations through `IFlowComponent`: a pipe writes
`p_in − p_out − Δp(ṁ) = 0`; a pump `p_out − p_in − ρ g H(ṁ) = 0`; a valve `ṁ − Kv φ(x) √(Δp ρ) = 0`;
a node its mass balance and its energy balance. For the cooling loop the state vector has 20
entries: 4 branch flows, 6 node pressures, 6 node enthalpies, the 2 flows crossing the inlet and the
outlet, and the 2 promoted parameters. The residual vector has 20 rows: 6 pressure relations,
4 mass balances, 6 energy balances, 2 stated pressures and the 2 stated constraints. Rows and
columns are *scaled* (`ResidualScales`, `UnknownScales`) so that a pressure equation in pascals and a
mass balance in kilograms per second are comparable; an unscaled norm would measure only the
pressure rows.

### 9. The seed: a first guess that can be iterated from

**In:** the system. **Out:** a starting state vector. **Who:** `Solvers/SolutionSeed`, with
`Solvers/BranchResistance` for what each component drops at a guessed flow.

Newton's method needs a starting point, and zero flow is a bad one: a pipe's drop is `R ṁ|ṁ|`,
whose derivative at zero is zero, so a zero start gives a singular matrix however well posed the
circuit is. The seed therefore builds flows that satisfy every mass balance from the stated duties
and flows, walks the graph subtracting each component's own drop to lay down pressures, and steps
temperatures along each branch inside a narrow band. It is allowed to be wrong about magnitudes.
It is not allowed to be structurally wrong. In the cooling loop the coil branch is seeded at
0.359 kg/s from the exchanger's duty and solves at 0.239; the mixing node is seeded at 287 kPa and
solves at 300.

### 10. Newton, and the loop around it

**In:** the seed. **Out:** the state where every residual is below tolerance, or a reason why not.
**Who:** `Solvers/NewtonSolver`, with `DenseLu` for the linear solve and `Tolerances` for every number
that decides convergence. Around it, `Solvers/OuterLoop`.

Newton evaluates the residuals, builds the Jacobian by finite differences, solves for a step, and
takes it with a line search. It stops when the scaled residual is under `1e-8`, or reports one of
the `FS30xx` outcomes: iteration cap, singular, diverged, stalled, non-finite. The cooling loop
takes eight Newton iterations from a cold seed, four in each of two sizing passes, and ends at a
scaled residual of 6e-10; a re-solve after an edit starts from the previous solution (`WarmStart`)
and usually takes one or two.

The outer loop is why "solve" is not one Newton run. Sizing needs flows, flows need sizes, and a
deferred expression like `in[2].t=HE1.out[2].t` needs a solved value. So the loop goes: size, solve,
evaluate the deferred expressions (`DeferredEvaluation`), size again from the solved flows, and stop
when no sized or deferred value moves by more than half a percent. One loop rather than three nested
ones, so that no inner loop converges against a stale outer one. After the last pass it reads the
solution once more for things worth telling the user: a valve throttling a bypass that wants a
balancing valve (`FS4011`), a valve written as mixing that runs diverting (`FS4012`).

Everything the loop and the solver know about one circuit can be rendered as text by
`Diagnostics/SolveExplanation`; [Reading the solve report](reading-the-solve-report.md) explains it.

### 11. Layout: the graph gets a shape

**In:** the graph, the model and the solved branch flows. **Out:** a `Scene`: a box and a rotation
for every component, a polyline for every connection, a position for every label, all in world
units. **Who:** `Layout/LayoutHintsDerivation`, then `Layout/LayoutSolver` driving `LayoutEngine`,
with `OrthogonalRouter` for the pipes.

The layout is solved in the library, not in the browser, so that every consumer draws the same
picture and an edit to a value moves nothing. Hints derivation first classifies the graph: which
loop is the main ring, what attaches to it and where, what a distribution header is, which way the
flow runs (solved flows decide, which is why layout comes after the solve). The engine then applies
the rules of the layout specification in order: rails for a ring, hangers for a branch between them,
blocks for an inner loop, instruments beside what they read. `SceneText` prints a scene as text and
`SceneAudit` checks it against the layout's invariants, which is how the layout is tested.
[How the diagram is arranged](how-the-diagram-is-arranged.md) is the reader's guide.

### 12. The model contract: one document with everything in it

**In:** the model, the graph, the solution, the scene, the diagnostics. **Out:** a `ModelContract`.
**Who:** `Model/ModelContractBuilder`; the wire records are in `Model/ModelContract.cs`; `Styles` resolves
presentation, `SymbolCatalog` supplies each kind's glyph, `ScaleDomain` settles the colour scales.

This is where SI ends. Every number is converted to the unit the script thinks in, kW and °C and
kPa gauge, and written beside a unit field. Every parameter says whether it was stated, sized (with
its basis) or defaulted. Every element gets its place on every colour scale, so switching from
temperature to pressure in the legend is a re-draw, not a request. The
[model contract](../functions/model-contract.md) page documents every field; the tables there are
generated from the records themselves.

### 13. The host: running the stages for a request

**Who:** `src/FluidScript.Api`, in particular `Pipeline/ScriptPipeline`.

The API is five HTTP calls under `/api/v1`: `validate` runs stages 0 to 3 and returns diagnostics
only; `compile` runs everything, tolerating an unconnected component so a half-written script still
draws; `solve` is `compile` with those tolerances turned into errors; `format` runs the formatter;
`metadata` returns the registry and the units for the editor. `ScriptPipeline` adds nothing to the
stages except a clock and the size limits. A `Session` keeps the last solution so the next compile
of the same document can warm-start. [Using the API](using-the-api.md) shows the calls.

### 14. The browser: text in, picture out

**Who:** `frontend/src`, organised by feature.

- `features/editor` is the script pane: one CodeMirror view per document. Its highlighting and
  completion come from `language/lexicon.generated.ts`, generated from the component registry, so
  the editor never has its own idea of what a keyword is.
- `features/pipeline` is the debounce: every keystroke restarts a timer, and when it fires the
  script goes to `/compile` through `api/client.ts`. The answer lands in the `draftStore`.
- `features/canvas` draws the contract. `scene.ts` prepares a `PreparedScene` from the contract's
  layout and colour scales, choosing nothing about position, and `SceneView.tsx` renders it as SVG.
  `Legend.tsx` is the colour scale; `features/hover` is the card with a component's values and bases.
- `features/export` serialises the same prepared scene to a standalone SVG or PNG, which is why the
  export and the canvas cannot disagree.
- `state/` holds the Zustand stores: the documents and their latest models, the run status, the
  selection, the UI. `features/shell` is the frame around all of it.

The frontend maps world units to pixels, draws, and formats numbers. It never solves, sizes or
places anything.

## Following one number through

`HE1 heat_exchanger power=30 in.t=20 out.t=50`:

| Stage | What happens to it |
|---|---|
| 3, binding | Three stated constraints: 30 000 W, 293.15 K in, 323.15 K out. `flow` and `dp` are absent, not zero |
| 5, sizing | `ExchangerSizer` pairs the default 20 kPa design drop with the flow it is measured at: 30 kW over 30 K is 0.239 kg/s, written with that basis |
| 6, counting | Power and both temperatures fix the coil's flow, so the mixing valve's `position` is promoted to deliver it |
| 9, seed | The coil branch is seeded from the duty at 0.359 kg/s, not at zero; the recirculation and return legs are partitioned from it |
| 10, Newton | The energy balances at `PU1__HE1` and `HE1__3WV` settle the enthalpies either side of the coil; the valve's position settles at 0.52, with 0.076 kg/s recirculating and 0.163 kg/s drawn from the primary |
| 12, contract | `power` and the temperatures are written `stated`; `flow` is written `sized` with its basis; the node states arrive in °C and kPa |
| 14, canvas | The exchanger's box is filled by the temperature scale at its outlet, 50 °C; the hover card shows the stated values, the sized flow and its basis |

## Where to read more

- The language, unit by unit and rule by rule: `plan/10-language/`.
- The components, the sizing rules and the layout rules: `plan/20-core-domain/`.
- The solver, its seed, its tolerances and why each number is what it is: `plan/30-solver/`.
- The API contract: `plan/40-api/`. The frontend: `plan/50-frontend/`.
- What each part found while being built, including what is still open: the `defects.md` in each
  of those folders. The register is where the reasons live.
