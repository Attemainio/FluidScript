---
id: 06-decision-log
title: Decision log
tier: 00-foundation
status: reviewed
owns: [architectural decisions D-xx, their rationale, their supersession chain]
depends_on: [01-vision-and-scope]
traces_to: [R-01, R-02, R-03, R-04, R-07, R-16, R-18, R-19, R-30, R-44, R-45, R-46, R-47, R-48, R-49, R-50]
open_questions: 0
last_review_pass: 6
---

# Decision log

## Purpose

The running record of decisions that shaped the plan, each with the reasoning and the alternatives
that lost. Git records *what* changed; this records *why*, which is the part that evaporates. A
decision here is binding: do not re-litigate a `D-` entry: supersede it with a new entry that says
what changed and cites the one it replaces.

## Responsibilities

**Owns.** The `D-` decisions, their rationale, their rejected alternatives, and the supersession chain.

**Explicitly does not own.** The consequences of any decision — those live in the documents each entry
constrains, which cite the `D-` id rather than restating the reasoning.

## Format

Each entry: **status** (accepted / superseded by `D-xx` / revisited) · **date** · **decision** ·
**why** · **rejected alternatives, and what they cost** · **what it constrains**.

An entry with no rejected alternatives is not a decision, it is a note. Move it into the document it
belongs to.

---

## D-01 · The script is declarative with light expressions

**Accepted · 2026-08-29**

The language has `let` bindings, arithmetic on dimensioned values, unit literals, and references to
other components' resolved properties (`HE1.dp`). It has **no** control flow, no user-defined
functions, no imports.

**Why.** The brief's stated goal is "as easy as possible", and the example script's density is the
product. Pure declaration cannot express `out = in + dT`, which is how designers actually think about
a heat exchanger, and forces every derived value to be computed by hand and pasted in — where it goes
stale silently. Expressions buy that at the cost of an evaluator and a dependency graph, both of which
are small and well understood.

**Rejected.**
- *Declaration only.* Simplest parser, no evaluator, no cycle detection. Cost: users hand-compute
  derived values, and a change to one number silently invalidates three others. Fails `R-02`'s spirit.
- *Full scripting* (loops, conditionals, functions). Cost: scoping rules, control-flow error recovery,
  a much larger test surface, and a syntax that stops resembling markdown. Directly contradicts `R-01`.
  If repeated subsystems become painful, the answer is a subsystem *component* (M6), not a `for` loop.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`14-expressions-and-references`](../10-language/14-expressions-and-references.md).

---

## D-02 · Every parameter is optional; explicit values are constraints

**Accepted, amended by `D-32` · 2026-08-29**

Omitting a component parameter follows that kind's registry policy: normally "size it", or a binding,
visible default where a decision defines one (`D-32`). Providing a value does **not** merely supply an
input — it adds a constraint the resolver must satisfy, and the resolver reports when it cannot.

**Why.** This is what makes the brief's `PU1 pump` line work: a pump with no parameters at all is a
complete, solvable declaration. The subtlety is the second half. If an explicit `head=15` were just a
seed value, a user who states a head they own from a datasheet would get it silently overridden or
silently ignored, and both are worse than an error saying the circuit cannot deliver it.

**Rejected.**
- *Explicit values as plain inputs, defaults for the rest.* Simpler to implement — no constraint
  propagation, no over-determined detection. Cost: a stated value that conflicts with the circuit
  produces a wrong answer instead of a diagnostic. In a sizing tool that is the failure mode that
  matters most.
- *Required parameters with defaults documented.* Honest and conventional, but every declaration grows
  to five parameters and the example script triples in length.

**Constrains.** [`24-auto-sizing`](../20-core-domain/24-auto-sizing.md),
[`22-component-model`](../20-core-domain/22-component-model.md),
[`31-solver-architecture`](../30-solver/31-solver-architecture.md).

---

## D-03 · Core emits topology and layout hints; the frontend owns geometry

**Accepted · 2026-08-29**

Core produces the solved graph plus ordering, port-side, flow-direction, and grouping hints. The
frontend computes placements, routes, and pixels.

**Why.** Keeps Core headless and unit-testable without a rendering dependency (`R-16`), and keeps
interaction latency local — dragging or hovering must not round-trip to the server. Core still shapes
the drawing, because only Core knows which node is upstream and which components form a subsystem;
those are topology facts, not visual preferences, and recovering them from coordinates is guesswork.

**Rejected.**
- *Core computes full geometry.* One layout implementation shared by canvas and CAD export, fully
  testable in `dotnet test`. Cost: every visual interaction round-trips, and the frontend becomes a
  dumb renderer with no way to respond to a drag at 60 fps.
- *Frontend owns everything including topology ordering.* Fastest to build. Cost: layout can never be
  produced server-side for export, and no layout regression can be caught by `dotnet test`.

**Constrains.** [`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md), and it leaves the export-geometry
question open in [`03-repository-layout`](03-repository-layout.md).

---

## D-04 · Plan documents are contract-level

**Accepted · 2026-08-29**

Each document states responsibilities, public type and interface signatures, invariants, error cases,
worked numeric examples, and acceptance criteria — but no method bodies.

**Why.** These documents are implemented by future sessions with no memory of this conversation.
Architecture-level prose leaves dozens of interface decisions to be improvised; near-implementation
detail is half stale the moment code exists and nobody rereads a spec they have caught out once.
Signatures plus invariants is the level that survives refactoring while leaving no ambiguity about the
seams.

**Rejected.**
- *Architecture-level.* Shorter, more durable. Cost: a fresh session invents the interfaces.
- *Near-implementation with pseudocode.* Zero ambiguity. Cost: enormous, slow to review, and stale
  fast.

**Constrains.** `_template.md`, and the "contract precision" axis of the review rubric.

---

## D-05 · The plan-review skill reports; the session applies on approval

**Accepted · 2026-08-29**

Reviewer agents are read-only. They write a findings file; the orchestrating session presents the
findings and applies only what the user accepts.

**Why.** A specification is a statement of intent, and an agent editing intent unattended produces
drift that reads plausibly and is wrong in ways nobody notices. The audit trail — findings file, then
an explicit application — is what makes a multi-session loop trustworthy.

**Rejected.**
- *Report only, never apply.* Safest. Cost: every pass needs the user to drive the fixes, so the loop
  converges at human speed and mostly does not converge.
- *Autonomous fixing.* Converges unattended. Cost: no approval gate on intent, and two passes can
  undo each other — the classic oscillation where pass N "fixes" what pass N−1 introduced.

**Constrains.** `.claude/skills/plan-review/SKILL.md`, `.claude/agents/plan-reviewer.md`.

---

## D-06 · REST for compile and solve; WebSocket for transient frames

**Accepted · 2026-08-29**

Compile, validate, steady-state solve, and export are REST. Transient runs stream frames over a
WebSocket.

**Why.** The 300 ms debounce path (`R-21`) is request/response by nature and benefits from being
cacheable, curl-able, and trivially testable. A transient run produces frames over seconds to minutes;
returning them in one payload delays playback until completion (`R-19`) and polling for progress
reinvents streaming badly.

**Rejected.**
- *REST only.* One contract, simplest testing. Cost: no playback until a run finishes.
- *WebSocket for everything.* Lowest latency, one contract. Cost: harder to version, cache, curl, and
  integration-test; a dropped socket takes editing down with it.

**Constrains.** [`42-rest-contract`](../40-api/42-rest-contract.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md),
[`51-frontend-architecture`](../50-frontend/51-frontend-architecture.md).

---

## D-07 · Implicit units per parameter, explicit units permitted, SI internally

**Accepted, amended by `D-14` · 2026-08-29**

Each parameter kind declares a canonical script unit — power → kW, temperature → °C, pressure → kPa.
A bare number means that unit. An explicit unit (`power=30000 W`) is accepted and converted. Core
stores and computes in SI base units throughout.

**Why.** It is what makes the brief's example read as written: `power=30 in=20 out=50` with no unit
noise. The escape hatch costs one optional token in the grammar and removes the failure mode where a
user with a datasheet in watts has to divide by a thousand in their head. SI internally because mixed
units inside a solver is how sign and scale errors get in.

**Rejected.**
- *Explicit units required everywhere.* Unambiguous and self-documenting; unit errors become compile
  errors. Cost: noisier, and it breaks the example syntax the brief specified.
- *Strict SI in the script.* Nobody writes `in=293.15`.

**Constrains.** [`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md), and every
component's parameter table.

---

## D-08 · The review loop sweeps by tier with a two-sweep convergence gate

**Accepted · 2026-08-29**

Each pass reviews one tier. State persists in `.claude/plan-review/state.json`. The loop stops when a
complete sweep of all tiers yields no blocking and no new should-fix findings, **twice consecutively**.

**Why.** Tier-at-a-time keeps each pass affordable and lets parallel reviewers hold a coherent slice.
The two-sweep rule exists because a single clean sweep most often happens immediately after a large
edit — precisely when the plan is least settled and the reviewers are most likely to be agreeing with
text they just saw applied.

**Rejected.**
- *Whole-tree sweep each pass.* Best at cross-document contradictions. Cost: expensive per pass,
  repeats work on stable tiers, converges slowly in wall-clock terms.
- *Manual invocation only.* Most control. Cost: nothing tracks convergence across sessions.

**Constrains.** `.claude/skills/plan-review/SKILL.md`, `.claude/plan-review/state.json`, and the tier
numbering in `plan/`.

---

## D-09 · Monorepo with `src/ tests/ frontend/ docs/ plan/`

**Accepted · 2026-08-29**

One public repository. The existing root `FluidScript.csproj` and `Class1.cs` are deleted, not grown.

**Why.** The script-to-render contract spans backend and frontend and changes on nearly every feature;
splitting repositories turns each such change into a two-repo, two-PR, version-negotiation problem for
no benefit at this size. The root csproj is `dotnet new` output with nothing in it worth keeping.

**Rejected.**
- *Keep the root csproj and grow it into Core.* Less churn on day one. Cost: a project at the root
  alongside `src/` gets awkward immediately, and the migration happens later under worse conditions.
- *Separate frontend repository.* Cleaner per-side CI. Cost: contract versioning across repos.

**Constrains.** [`03-repository-layout`](03-repository-layout.md).

---

## D-10 · Scripts use the `.fluid` extension

**Accepted · 2026-08-29**

**Why.** `.fs` is F#. `.fsc` is taken by several unrelated tools. `.fluid` is unambiguous, readable,
and free.

**Rejected.** `.fs` (collides with a first-class .NET language in a .NET repository — editors and
`dotnet` tooling both mis-handle it), `.fsx` (F# scripts), `.fld` (opaque).

**Constrains.** [`03-repository-layout`](03-repository-layout.md),
[`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md).

---

## D-11 · Two named reference circuits, and no unnamed variants

**Accepted, amended by `D-16`, `D-18`, `D-32`, `D-33` · 2026-08-29**

[`01-vision-and-scope`](01-vision-and-scope.md) defines exactly two solvable reference circuits — the
**cooling loop** (a three-way mixing circuit, used for topology, layout, the model contract and
rendering) and the **simple loop** (one series circuit with one flow, used for sizing and solver
arithmetic). Every worked example in `plan/` states which it uses and reproduces `01`'s solved-state
figures. Introducing a third, or an unnamed variant of either, is a review finding.

**Why.** The first review pass found that "the corrected cooling loop" meant three different circuits
across six documents, with contradictory node counts (four, five and "the 5th node" in a document that
could not name it) and contradictory solved states — one document putting a node at 12 °C that another
constrained to 20 °C. None of the individual documents was obviously wrong; the drift was only visible
across them. Two circuits rather than one, because they answer different questions: the mixing circuit
is the interesting topology and has three different flows, which makes it useless for a sizing example
a reader can check by hand.

**Rejected.**
- *One reference circuit.* Simplest to state. Cost: every sizing and solver example either uses the
  mixing circuit — where three flows and a split make hand-checking impractical — or quietly invents a
  simpler one, which is exactly the drift this entry exists to stop.
- *Let each document choose its own example.* Most freedom per author. Cost: what the tree already had.
  A contradiction between two documents' examples is invisible until someone implements both.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md), and every document with a worked
example: `23`, `24`, `25`, `26`, `31`, `32`, `33`, `34`, `53`, `57`, `61`.

---

## D-12 · STEP and IFC are out of scope; `R-31` is amended

**Accepted · 2026-08-29**

Export targets are SVG and PNG first, then DXF and a versioned JSON/XML model. **STEP and IFC will not
be implemented.** `R-31` is amended to say so, and [`71-export-formats`](../70-future/71-export-formats.md)'s
open question 3 is closed by this entry.

**Why.** STEP is a 3D solid-model format and FluidScript has no 3D geometry — no routes in space, no
elevations beyond a scalar, no fitting or equipment envelopes. Exporting one means synthesising
geometry the model does not contain, producing a file whose dimensions are meaningless but which looks
authoritative. Someone will measure it. IFC needs the same invented geometry plus a large semantic
mapping. DWG additionally needs a commercial library; DXF is a documented ASCII format every CAD tool
imports, so the capability is reachable without the procurement.

**Rejected.**
- *Implement STEP anyway, since `R-31` named it.* Honours the original requirement literally. Cost: a
  misleading artefact, and the requirement was written before anyone had established that the model
  carries no geometry — which is new information, not a change of mind.
- *Leave it as an open question.* Cost: it stays a live expectation, and `72-roadmap`'s "deliberately
  never planned" list already treats it as settled. A requirement and a roadmap disagreeing in the
  same tree is worse than either answer.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md)'s `R-31`,
[`71-export-formats`](../70-future/71-export-formats.md), [`72-roadmap`](../70-future/72-roadmap.md).

---

## D-13 · `#` begins a comment; hex colours are quoted

**Accepted · 2026-08-30**

The line-comment character is `#`, not `|`. A hex colour in a `style` directive is written as a quoted
string: `style "#2f6f9f" 2px fillet -`. `|` becomes unallocated — not an operator, not a comment.

**Why.** `|` is awkward to type on the layouts this project's users have: `AltGr`-plus-a-key on the
Nordic, German and several other European layouts, and a chorded key on many compact keyboards. A
comment character is typed dozens of times in a working script, and a language that trades on density
cannot afford friction in the one token users type most. `#` is unshifted or single-shifted nearly
everywhere and is what every reader already recognises from shell, Python, YAML, TOML, and Markdown
headings. The cost is one collision — `#rrggbb` — and the `string` token already existed to absorb it.

**Rejected.**
- *Keep `|`.* Zero churn, and it reads genuinely well as a margin rule when comments are column-aligned,
  which is how the brief's example uses it. Cost: friction on every comment, on the layouts of the
  people writing the scripts. Density is the product (`P1`), and typing effort is part of density.
- *Accept both `#` and `|`.* Nobody is inconvenienced. Cost: two spellings of one thing, which is
  exactly what `P6` forbids, and the printer must then decide which to emit when write-back inserts a
  comment — a decision with no right answer.
- *`//`.* Familiar from C-family languages and collides with nothing. Cost: two characters instead of
  one, and `/` is already the division operator, so a mis-typed `/` in an expression would silently
  comment out the rest of a line rather than erroring.
- *Keep `#rrggbb` unquoted by making `#` a comment only when followed by whitespace.* Preserves both
  forms. Cost: `#comment` with no space stops being a comment, which is how most people type one, and
  the rule is invisible until it bites.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md), every
`fluidscript` block in `plan/` and `/docs`, and the editor's syntax highlighting
([`52-editor`](../50-frontend/52-editor.md)).

---

## D-14 · Canonical script units are SI, with three stated exceptions

**Accepted, amended by `D-32` · 2026-08-30** · *amends `D-07`*

A bare number in the script means the **SI unit of its dimension**. `length=45` is 45 metres.
Three dimensions keep a non-SI canonical unit, and they are the whole list:

| Dimension | Canonical | Instead of SI | Why |
|---|---|---|---|
| `Temperature` | °C | K | `in=293.15` is unusable in a design script |
| `Power` | kW | W | `R-04` states `power=30` means kW; it is the brief's flagship line |
| `Pressure` / `PressureDelta` | kPa | Pa | Hydronic circuits are specified in kPa and bar; `p=300000` on a node line is noise a reader stops trusting |

The canonical unit is a property of the **dimension**, never of the parameter. A separate **display
unit** per dimension governs what the UI prints, so enthalpy can be J/kg on the wire and kJ/kg in a
tooltip without either being ambiguous.

**Why.** `D-07` left the canonical unit to be chosen per dimension on readability grounds, and the
result was `Length → mm` in [`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md)
while [`22-component-model`](../20-core-domain/22-component-model.md) declared `length` in m,
`roughness` in mm and `elevation` in m — three canonical units for one dimension. Under `13`'s own
rule `P1 pipe length=25` meant 25 millimetres, and every pipe pressure-drop figure in the tree was out
by a factor of 1000. Nothing in the result would have looked wrong. "The SI unit" is the only rule a
reader who has never opened the unit table can guess correctly, and the three exceptions are few
enough to state in one line each.

**Rejected.**
- *Strict SI, no exceptions.* One rule, no table, nothing to memorise. Cost: `in=293.15`,
  `power=150000`, `p=300000`. It amends `R-04`, rewrites the brief's example, and rewrites every
  worked example in the tree — to make three lines of documentation unnecessary.
- *Per-parameter canonical units, as `22` was already doing.* Most readable line by line: `length` in
  m, `roughness` in mm, each chosen for its own magnitude. Cost: the reader must know which parameter
  they are looking at to know what the number means, so the number is no longer self-describing —
  which is the entire purpose of having a canonical unit. It is also what produced the 1000× ambiguity.
- *Require an explicit unit on every dimensioned value.* Unambiguous, self-documenting, and every unit
  error becomes a compile error. Cost: `power=30 kW in=20 C out=50 C`, and `D-07` already rejected this
  for the same reason — it breaks the syntax the brief specified.

**Constrains.** [`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md),
[`22-component-model`](../20-core-domain/22-component-model.md)'s parameter tables,
[`26-model-contract`](../20-core-domain/26-model-contract.md)'s unit fields,
[`55-design-system`](../50-frontend/55-design-system.md)'s number formatting.

---

## D-15 · Component kinds resolve by normalisation, curated aliases, then similarity

**Accepted · 2026-08-30** · *qualifies principle `P6`*

A `kind-name` resolves in three stages: normalise (lowercase, drop `_` and spaces), exact match
against the canonical keyword and a curated alias list, then similarity scoring with a **0.70**
threshold and a **0.05** ambiguity margin. A stage-3 resolution always emits `FS1512` (info) naming
what was read; an ambiguous one is `FS1513` (error) naming both candidates.

Similarity applies to **kind names, parameter names, and property names** — closed sets the registry
owns. It never applies to component *names*, where a typo must dangle rather than silently merge.

**Why.** A designer types `3-way-valve`, `mixing valve`, `radiator`, or `exchanger`; an agent
generating a script from a text brief (`R-29`) produces all four spellings with roughly equal
probability. Rejecting them teaches the user that the tool is fussy about something that carries no
meaning, and the canonical spelling is not discoverable from the error unless the error already does
the similarity search — at which point the tool knew the answer and withheld it. Curated aliases carry
real domain knowledge (`radiator` and `boiler` really are heat exchangers here) that edit distance
cannot supply, and similarity catches the keystroke errors aliases cannot enumerate.

**This qualifies `P6`**, which says one way to say each thing. `P6`'s costs are `/docs` explaining
every form and the printer choosing between them; neither applies, because `/docs` has one page per
canonical keyword with an "also written as" line and the printer never emits an alias. The cost that
does land — two scripts meaning the same thing looking different in a diff — is accepted.

**Rejected.**
- *Exact canonical spelling only, with a similarity-based suggestion in `FS1502`.* Preserves `P6`
  exactly, and no input is ever silently misread. Cost: every one of the spellings above is an error
  the user must fix by hand, including forms that are unambiguous to any reader. It also makes the
  question in [`22`](../20-core-domain/22-component-model.md)'s open question 2 — whether
  `heat_exchanger` is the right name at all — a high-stakes decision, when aliases make it a low one.
- *Similarity with no ambiguity margin — resolve to the best match.* Simplest to state. Cost: not
  deterministic in any useful sense. `valv` scores 0.80 against `valve` and 0.78 against
  `3_way_valve`; picking the higher is a coin flip, and adding an alias to one component could
  silently change how a script that never mentions it resolves.
- *Similarity on component names too.* Maximally forgiving. Cost: `PU1` and `PUI` merge, and a
  circuit silently loses a component. A name is the user's own vocabulary; the registry has no
  standing to correct it.

**Constrains.** [`15-semantic-model`](../10-language/15-semantic-model.md),
[`12-grammar`](../10-language/12-grammar.md)'s `FS1108`,
[`02-glossary`](02-glossary.md)'s naming rule,
[`61-documentation-plan`](../60-docs-and-devex/61-documentation-plan.md)'s per-kind page template.

---

## D-16 · A third reference circuit, for the transient and control demo

**Accepted, amended by `D-18` · 2026-08-30** · *amends `D-11`*

[`01-vision-and-scope`](01-vision-and-scope.md) defines a **third** named reference circuit, the
**demand-step loop** (`samples/m4-demand-step.fluid`): the cooling loop with a discretized pipe on the
recirculation leg, a schedule, and a controller. Tiers 30–50 use it for every transient, controller,
streaming and playback example. `D-11`'s prohibition on unnamed variants is unchanged and now covers
three circuits rather than two.

**Why.** `34-controllers`' worked example, and its acceptance criterion that "the measurement does not
move for at least 30 s after the step", both depend on the disturbance reaching the measured node
through several pipe volumes. In the cooling loop it cannot: the measurement is `N2.t`, the path from
`HE1` to `N2` runs `HE1 → HE1__3WV → 3WV.a → 3WV.b → N2` with no declared pipe on it, and `P1` — the
only pipe in the circuit — sits on the primary return **downstream** of `N2`, discharging to `N3` and
never returning. As specified, the controller saw the disturbance within one timestep and the entire
tuning derivation rested on a dead time that did not exist. A circuit cannot be repaired by adjusting
the numbers computed from it.

The steady circuit is left alone deliberately: every solved figure in `01` is reproduced across nine
documents, and moving `P1` to fix a transient example would invalidate all of them to no benefit.

**Rejected.**
- *Move `P1` onto the recirculation leg in the cooling loop itself.* One circuit fewer to maintain.
  Cost: the cooling loop's four branches, six nodes, 20×20 counting table and every solved flow change,
  and nine documents are rewritten to fix an example in two.
- *Add the pipe as an unnamed variant inside `34`.* Smallest edit. Cost: exactly the drift `D-11`
  exists to stop — "the cooling loop with a pipe added" would mean something slightly different in
  `33`, `34` and `43` within two review passes, which is the history `D-11` records.
- *Drop the dead-time claim and tune against a first-order lag.* Honest and smaller. Cost: the dead
  time is the reason `nodes=` exists and the reason `R-14` is a requirement; a controller demo without
  it demonstrates the thing the tool is least interesting at.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md),
[`33-transient-time-domain`](../30-solver/33-transient-time-domain.md),
[`34-controllers`](../30-solver/34-controllers.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md),
[`57-state-visualization`](../50-frontend/57-state-visualization.md).

---

## D-17 · The heat exchanger is two-sided and rated, in M2

**Accepted, amended by `D-19` · 2026-08-30**

`heat_exchanger` gains a second side (`in2`, `out2`), a flow arrangement, and a thermal rating:
`ua`, `area`, `u`, `approach`, and plate geometry (`plates`, `lamella`, `plate_area`). The governing
relation is **ε-NTU**; LMTD is a reported property, not a residual. It ships in **M2**, not M6.

A one-sided declaration keeps working unchanged: with `in2`/`out2` unconnected the component falls
back to **duty mode**, which is exactly what every existing script and both steady reference circuits
use. The two modes are selected by topology, through the same optional-port mechanism inference rule
I3 already applies to a three-way valve used as a two-way.

**Why.** A component called a heat exchanger that cannot be sized thermally is a duty block wearing the
name, and it fails the tool's central promise: `D-02` says an omitted parameter normally means "size
it", and
the one thing a designer most wants sized about an exchanger — how big is it — had no answer. The gap
also made three other things unreachable: `FS4008` and `hx.approach_min` were allocated but permanently
dead, `22`'s open question 2 (is `heat_exchanger` even the right name) could not be settled on the
merits, and a district-heating substation — the single most common thing a Nordic HVAC designer models
— was inexpressible.

**ε-NTU rather than LMTD as the residual** is the load-bearing sub-decision. LMTD is singular when the
two end differences are equal, which is precisely the balanced-counterflow case `C_r = 1` — a common
design point, not an edge case. A residual that divides by zero where designs actually sit is
unusable. ε-NTU needs only the two *inlet* temperatures, is smooth in NTU everywhere, and its own
removable singularity at `C_r = 1` is handled by the same C¹ blend the valve law already uses.

**Rejected.**
- *Keep the duty block in M2; specify the rated model as M6.* Smallest scope change, and M2 stays the
  size it was estimated at. Cost: "can FluidScript size an exchanger" stays "no" through the milestone
  that exists to prove auto-sizing works, and the sizing story is told with pipes and valves only —
  the two components where the answer was least interesting.
- *Rated single-side: `ua`/`area`/`u` against a stated secondary temperature profile.* Two thirds of the
  value for a fraction of the work, no second circuit, no coupled solve. Cost: the secondary profile is
  an input the user has to know, which is backwards — in a substation the secondary side is the
  circuit being designed, and its return temperature is an *outcome*.
- *Geometry-free rating: `ua` only, no plates or spacing.* Avoids a correlation and a catalogue. Cost:
  `ua` is not a number anyone has; a plate count and a channel gap are what a datasheet gives, and
  deriving `U` from them is the step that turns a rating into a selection.

**Constrains.** [`22-component-model`](../20-core-domain/22-component-model.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md) (two hydraulic circuits in one
solve, and the flow-group refinement of "junction element"),
[`24-auto-sizing`](../20-core-domain/24-auto-sizing.md),
[`27-component-catalog`](../20-core-domain/27-component-catalog.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md), and `R-35`.

---

## D-18 · A fourth reference circuit: the substation

**Accepted · 2026-08-30** · *amends `D-11` and `D-16`*

[`01-vision-and-scope`](01-vision-and-scope.md) defines a fourth named reference circuit, the
**substation** (`samples/m2-substation.fluid`): a two-sided plate exchanger between a district-heating
primary and a heating secondary, 150 kW at 85/45 °C against 40/60 °C. Every worked example touching
two-sided exchangers, coupled circuits, or thermal rating uses it.

**Why.** `D-17` adds a capability none of the three existing circuits exercises — all of them use the
exchanger in duty mode, on one side, with no rating. A capability with no reference circuit gets
worked examples invented per document, which is the drift `D-11` exists to stop and which had already
happened twice in this tree. The substation is also the smallest circuit that forces the two structural
consequences of `D-17` to be faced: two hydraulic components in one solve, and a component that is
interior to a branch on each of two sides.

**Rejected.**
- *Extend the cooling loop to two sides.* One circuit fewer. Cost: its solved state is reproduced across
  nine documents and every figure would move, to demonstrate a feature the circuit was not chosen for.
- *No reference circuit; let each document show its own exchanger.* Cost: what `D-11` was written about.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md), and every document with a two-sided
worked example: `22`, `23`, `24`, `27`.

---

## D-19 · The exchanger has three modes, promoted by evidence of a second side

**Accepted · 2026-08-30** · *amends `D-17`*

`D-17` selected between duty and rated mode on whether `in2`/`out2` were **connected**. That is too
narrow. The mode is chosen from **evidence of a second side**, which is either secondary flow
properties or secondary connections:

| Mode | Trigger | Second side is | Rating parameters |
|---|---|---|---|
| **Duty** | No secondary properties, no secondary connections | Absent | Inert — `FS2110` |
| **Rated** | Secondary properties stated (`in2`, `out2`, `dt2`, `flow2`), ports open | A stated boundary profile | Live |
| **Coupled** | Secondary ports connected | A solved stream in its own circuit | Live |

**Rated and Coupled together are the "extended" exchanger**; Duty is the standard one, and it stays
the default. Stating `area`, `u`, `ua` or plate geometry **does not on its own promote anything** — a
rating parameter with nothing to rate against is inert, warned, and the component remains a duty
block.

**Why.** Rating parameters describe *how* heat crosses; they cannot say *what it crosses to*. `u=3300`
on a component with one stream is a number with no second temperature to work against, so promoting on
it would produce an exchanger the solver cannot evaluate — an error caused by the tool's own inference
rather than by anything the user did wrong. Evidence of a second side, by contrast, is exactly what
ε-NTU needs: two inlet temperatures and two capacity rates.

**Rated mode is the genuinely new one, and it is the common case.** A designer sizing a substation
against a district-heating primary they are not modelling hydraulically writes `in2=85 out2=45 u=3300`
and gets an area — no second circuit, no extra pipework, no pump they do not care about. Requiring
connections for any rating at all forced them to model a circuit to ask a question about a component.

**Rejected.**
- *`D-17`'s original rule — connections only.* Simplest trigger, one bit of state. Cost: rating an
  exchanger against a known external profile is impossible without modelling that profile as a
  circuit, which is most of the work for none of the answer.
- *Promote on any rating parameter.* Matches the instinct that "if you gave it an area, you want it
  rated". Cost: `ua=` alone yields a rated exchanger with one stream, which has no ε and no `C_min`;
  the tool would have to error on a promotion it chose itself.
- *An explicit `mode=` parameter.* Unambiguous, no inference. Cost: it is a parameter whose value is
  always derivable from the others, which is exactly what `D-02` and principle P3 say not to ask for —
  and a user who sets it wrong gets a contradiction instead of a model.

**Constrains.** [`22-component-model`](../20-core-domain/22-component-model.md),
[`24-auto-sizing`](../20-core-domain/24-auto-sizing.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md).

---

## D-20 · Components describe a symbol; they do not draw one

**Accepted · 2026-08-30**

`IComponent` splits into a small hierarchy, and every component carries a **`SymbolId`** resolving into
a declarative `SymbolDefinition` — primitives, port anchors, a label anchor — held in Core and shipped
over `/api/v1/metadata`. There is **no `Draw()` method and no `ICanvasObject` in Core.** The frontend
resolves symbol + placement + style into SVG; the DXF exporter resolves the same symbol against
supplied placements.

```
IComponent                      identity, kind, parameters, SymbolId
├── IFlowComponent               ports, flow groups, residuals, equation count
└── IObserver                    reads model state, contributes no equations
    └── IController              also drives an actuator
```

**Why not `ICanvasObject.Draw()`, which is the natural instinct.** A `Draw()` on a Core type means Core
holds a drawing surface, which breaks `R-16` and the project's standing rule that Core has no UI
dependency. That is the stated objection; three practical ones matter more.

*It cannot be tested.* `dotnet test` can assert that a pump's symbol has an inlet anchor on its west
edge and a circle of radius 0.4. It cannot assert anything about a `Draw()` that took a canvas context
and returned `void`, so every symbol regression would be caught by eye or not at all.

*It has to be written twice.* `Draw()` binds to one rendering target. The canvas is SVG in a browser;
DXF export is a server-side writer with no browser; PNG export is a third path. A declarative symbol is
consumed by all three from one definition, and this is what closes
[`03-repository-layout`](03-repository-layout.md)'s open question about where the exporter gets its
geometry — a question that existed *because* `D-03` put drawing in the frontend and left nothing behind
for the exporter to draw from.

*It puts the wrong thing in the hierarchy.* A pump's shape is a property of the **kind**, not of the
instance — every pump in a model draws identically. Hanging `Draw()` off each instance implies a
per-instance decision that never varies, while the thing that *does* vary per instance (where it sits,
which way it faces, what colour) is precisely what `D-03` keeps in the frontend.

**What is kept from the instinct**, because it is a good one: every component really does answer "what
do I look like", uniformly, through one member on the root interface. Sensors, controllers and flow
components all carry a `SymbolId`, so the canvas enumerates one list and never asks what kind of thing
it is holding.

**This does not reopen `D-03`.** `D-03` divides *topology* from *pixels*. A symbol's intrinsic shape is
neither: it is a fact about a component kind, like its port list, and it is as much geometry as "a
pump is a circle with a triangle in it" is geometry. Placement, routing, scale and colour stay in the
frontend, unchanged.

**Rejected.**
- *`ICanvasObject` with `Draw()` on every component.* One obvious member, no library, no lookup, and a
  new component kind is drawable the moment it exists. Costs above.
- *Symbols as a TypeScript map in the frontend.* Simplest for React, no wire format, no C# type. Cost:
  the exporters cannot reach it, no `dotnet test` covers it, and a component added in Core ships
  invisible until someone remembers to edit a second repository directory.
- *Symbols as SVG files on disk.* Designer-editable, which is a real advantage. Cost: an SVG carries no
  port anchors, so the renderer has to guess where a pipe attaches — and that guess is the one thing
  the symbol exists to answer.

**Constrains.** [`22-component-model`](../20-core-domain/22-component-model.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`71-export-formats`](../70-future/71-export-formats.md),
[`03-repository-layout`](03-repository-layout.md), and `R-37`.

---

## D-21 · Sensors are observer components

**Superseded by `D-23` · 2026-08-30**

A `sensor` is a component with **no ports, no flow, and no equations**. It names one property of one
target — `TI1 sensor measure=N2.t` — and its measured dimension selects its ISA symbol letter, its
units, and its display format. One kind covers temperature, pressure, mass flow and volume flow;
the property reference distinguishes them, not the keyword.

**Why a component rather than a UI annotation.** A sensor is where a real plant is instrumented, and
that is a *design* fact: it belongs in the script, versioned and diffed with everything else (`P5`).
Three things follow that a UI-only marker could not give. It appears in the model contract, so exports
and logs carry the same named points the drawing does. It is a stable target for a controller's
`measure=`, which is how a real loop is built and is what later makes sensor lag modellable. And it
gives the canvas a persistent readout, which `R-23`'s hover deliberately does not — hover shows one
component on demand; instrumentation shows the four numbers you always want, always.

**One kind, not four.** `temperature_sensor`, `pressure_sensor` and `flow_sensor` are aliases (`D-15`)
resolving to `sensor`; what a sensor measures is `measure=`, because the alternative is four kinds that
differ only in which property they read and a fifth the moment someone wants density. Writing a
dimension-specific alias whose `measure` resolves to a different dimension is `FS2113` — cheap to check,
and it catches the one wart the alias creates.

**Rejected.**
- *Four distinct kinds.* More discoverable in completion, and each could fix its own property. Cost:
  four registry entries, four `/docs` pages, and four symbols for one concept — and the fifth and sixth
  arrive as soon as anyone wants density or enthalpy.
- *A UI-only overlay the user places on the canvas.* No language change at all. Cost: it is a parallel
  model the script does not contain, which is exactly what `P5` forbids, and it cannot be exported,
  diffed, or pointed at by a controller.
- *A property of the node itself (`N2 node display=t`).* No new kind, no new symbol. Cost: a sensor is
  not always on a node — flow is measured on a branch — and it conflates "this node exists" with "this
  point is instrumented", which are different decisions with different lifetimes.

**Constrains.** [`22-component-model`](../20-core-domain/22-component-model.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`34-controllers`](../30-solver/34-controllers.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md), and `R-36`.

---

## D-22 · An active transient is isolated from the editable draft

**Accepted · 2026-08-30**

Starting a transient creates an immutable **run snapshot** containing the compiled model, resolved
catalogue version, initial state, solver settings, schedule, and contract version. Subsequent editor
changes compile into a separate draft model and **do not cancel, mutate, block, or replace** the active
run. The UI shows `Script changed — restart to apply` until the user explicitly stops or starts again.

Transient calculation runs on a backend worker, never on the browser UI thread or the WebSocket I/O
continuation. In the frontend, frame decoding, delta application, state-scale calculation, and render
preparation run in a Web Worker. SVG DOM mutation remains on the browser UI thread because DOM APIs
are main-thread-only; it is reduced to one bounded `requestAnimationFrame` commit of prepared values.

A run stops only on: explicit Stop; socket disconnect; cancellation by a newer Run command for the
same session; an internal solver invariant failure; an incompatible/corrupt frame; or worker failure.
Malformed edits, compile diagnostics, and ordinary changes are draft failures and cannot reach the
run snapshot. If isolation itself is breached — a contract/version mismatch, impossible frame order,
or state shape differing from the snapshot — the client stops that run, keeps the last valid frame,
and reports the fault instead of attempting recovery against mixed models.

**Why.** A designer must be able to prepare the next case while watching the current one. Cancelling
on every keystroke makes that impossible. Sharing a mutable model between editor and solver is worse:
an edit can change the equation count halfway through an integration step. Snapshot isolation makes
the desired behavior the simplest behavior and turns "editing somehow broke the run" into a detectable
invariant violation rather than a race.

**Rejected.**
- *Cancel on any edit.* Easy consistency rule. Cost: typing destroys a useful run and makes comparison
  work impossible.
- *Hot-patch the running model.* Immediate feedback. Cost: no defined physical meaning when topology,
  parameters, or controller state change mid-step; equation counts can change under the integrator.
- *Run solver and frame preparation on the UI thread.* Fewer workers and messages. Cost: long frames
  freeze input, pan, hover, and Stop — precisely when cancellation must remain responsive.
- *Render SVG from a Web Worker.* Satisfies "everything off-thread" literally. Cost: workers cannot
  mutate the DOM; adopting Canvas/OffscreenCanvas would discard SVG accessibility and export benefits.

**Constrains.** [`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`07-quality-attributes`](07-quality-attributes.md),
[`33-transient-time-domain`](../30-solver/33-transient-time-domain.md),
[`41-api-architecture`](../40-api/41-api-architecture.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md),
[`51-frontend-architecture`](../50-frontend/51-frontend-architecture.md), and `R-41`.

---

## D-23 · Persistent sensor components are deferred beyond v1

**Accepted · 2026-08-30** · *supersedes `D-21`*

Controllers in v1 continue to measure a `PropertyReference` directly (`measure=N2.t`). Hover, pinned
readouts, and accessible tables expose state without adding a `sensor` language kind. A persistent
instrumentation component may return after v1 under `R-36`, when sensor location, lag, calibration,
and export semantics are required together.

**Why.** `D-21` added a language kind, aliases, diagnostics, model-contract shape, symbol, controller
relationship, exporter behavior, and documentation page without a requirement or milestone. Direct
property references already satisfy control, and a pinned readout satisfies the immediate UI need.
Deferring removes an entire public surface while preserving the later design direction.

**Rejected.**
- *Implement `D-21` in v1.* More faithful P&I instrumentation. Cost: substantial cross-tier scope with
  no v1 acceptance criterion, while its only required consumer already works without it.
- *Delete the idea permanently.* Smallest product. Cost: real instrumentation, sensor dynamics, and
  report/export points are likely valuable once transient models mature.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`22-component-model`](../20-core-domain/22-component-model.md),
[`34-controllers`](../30-solver/34-controllers.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`72-roadmap`](../70-future/72-roadmap.md), and `R-36`.

---

## D-24 · Declarative symbols land with the M3 renderer

**Accepted · 2026-08-30** · *phases `D-20`*

`D-20`'s declarative `SymbolDefinition` and `SymbolId` remain the architecture, but they are delivered
in **M3**, not M2. The schema is owned by the model contract, stored with Core component-kind metadata,
served from `/api/v1/metadata`, and consumed by both the SVG renderer and later exporters. M2 Core may
carry the identifier and definitions as inert metadata but its physics exit does not depend on drawing.

**Why.** Symbols are necessary when the canvas exists and unnecessary to validate hydraulics. Putting
their acceptance in M3 preserves one source for rendering/export without allowing visual schema work
to block the solver vertical slice.

**Rejected.**
- *Make symbols an M2 exit criterion.* Ensures every component is drawable early. Cost: presentation
  contracts can block physics before there is a renderer to validate them.
- *Keep symbols frontend-only until export.* Faster M3 implementation. Cost: recreates the drift and
  duplicate-source problem `D-20` settled.

**Constrains.** [`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`22-component-model`](../20-core-domain/22-component-model.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`42-rest-contract`](../40-api/42-rest-contract.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md), and `R-37`.

---

## D-25 · Bare connections are ideal links

**Accepted · 2026-08-30**

A connection with no declared component has zero length, zero pressure drop, zero storage, and no
heat loss. It connects topology only. Every physical resistance or volume is explicit: write a `pipe`,
`valve`, exchanger drop, or another component. A pump is auto-sized only from explicit losses; if its
connected circuit contains none, its sized head is zero and `FS2312` explains that no resistance was
modelled.

**Why.** An implicit pipe needs a length, material, roughness, and fittings that the script does not
supply. Inventing any of them violates P3 and produces trustworthy-looking fiction. Ideal links make
the terse syntax predictable and make the syntax reference honestly a topology example, not a sizing
example.

**Rejected.**
- *Implicit pipe with a default length.* Preserves the claim that pipework is always sized. Cost: pump
  head becomes a function of an invisible invented value.
- *Connection syntax with inline length.* Expressive but creates a second way to declare a pipe and
  duplicates pipe parameters progressively.

**Constrains.** [`11-language-overview`](../10-language/11-language-overview.md),
[`22-component-model`](../20-core-domain/22-component-model.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md),
[`24-auto-sizing`](../20-core-domain/24-auto-sizing.md), and `R-06`.

---

## D-26 · Pressure reference and temperature-delta syntax are context-free

**Accepted · 2026-08-30**

Bare script pressure and the canonical `kPa`/`bar` forms are **gauge pressure**. `kPag`/`barg` are
explicit gauge spellings; `kPaa`/`bara` are absolute. The v1 atmospheric reference is standard
atmosphere, 101.325 kPa absolute, recorded in every solved model. Core hydraulic equations use
pressure differences; the single substance adapter converts gauge to absolute before property calls.

`K` is absolute temperature. Temperature differences use `dK` (canonical) or `dC`; therefore
`let dT = 30 dK` is a `TemperatureDelta` without backward inference, while `let t = 300 K` is a
`Temperature`. Existing °C literals remain absolute.

**Why.** Hydronic designers state gauge pressure, thermodynamic packages require absolute pressure,
and an unlabelled conversion is a cavitation error waiting to happen. Separately, deciding whether
`300 K` is absolute or a delta from where it is later used makes the binder non-local and gives unused
bindings no type. Distinct delta symbols make token meaning independent of use.

**Rejected.**
- *Absolute pressure everywhere.* Internally simple. Cost: unfamiliar scripts and misleading UI for
  the target domain.
- *Infer `K` from use.* Preserves `30 K` for deltas. Cost: backwards inference, cyclic dependencies,
  and no type for unused bindings.
- *`K` as delta, `Kabs` as absolute.* Keeps old examples. Cost: reverses the scientific meaning of K
  and makes the common absolute spelling the exceptional one.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md),
[`14-expressions-and-references`](../10-language/14-expressions-and-references.md),
[`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md), and `R-04`.

---

## D-27 · v1 files are durable, versioned, and locally owned

**Accepted · 2026-08-30**

M3 opens and saves `.fluid` files using the browser File System Access API when available and
upload/download fallback otherwise. Autosaved recovery remains separate from the named file and never
silently marks it saved. The UI exposes filename, dirty state, Save, Save As, Open, recovery status,
and conflict/error handling.

Every saved script begins with `fluidscript 1` and may pin `catalog <id>@<version>`. The language major
selects parsing semantics. Unsupported newer majors are rejected without mutation; older supported
majors are parsed under their own semantics and upgraded only by an explicit, previewable migration.
Pre-1.0 application releases use `0.x`, but language and model-contract versions evolve independently.

**Why.** A tool that loses the second design is a demo, not a design tool. Versioning must precede
durable files: saving an unversioned script merely postpones data loss until syntax changes. Explicit
migration preserves text as source of truth and prevents a new release from silently reinterpreting
engineering values.

**Rejected.**
- *localStorage only.* Zero file permissions and easy recovery. Cost: one unnamed draft, no sharing,
  no reliable backup, and browser-data clearing destroys work.
- *Server-side project database.* Rich projects and collaboration. Cost: authentication, storage,
  conflict resolution, and hosting before any are requirements.
- *Always rewrite to the latest syntax on open.* Convenient. Cost: opening a file mutates it and can
  change engineering meaning before the user sees a diff.

**Constrains.** [`18-script-compatibility`](../10-language/18-script-compatibility.md),
[`52-editor`](../50-frontend/52-editor.md), [`58-file-lifecycle`](../50-frontend/58-file-lifecycle.md),
[`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md), `R-38`, and `R-39`.

---

## D-28 · v1 is hydronic; psychrometrics is a validated property capability

**Accepted · 2026-08-30**

The v1 solver supports hydronic circuits. Humid-air psychrometrics remains a first-class, tested
`ISubstance` capability and is exposed in metadata, but a complete air-side circuit is not claimed
until fan, duct, coil, humidity-source, and condensation behavior have their own reference model.
`fan` is not a v1 alias for `pump`; accepting the word while omitting fan physics would overstate the
supported domain.

**Why.** A property call and a solved air network are different product promises. The current plan can
validate humid-air values but cannot model humidity balance, coil condensation, duct leakage, or fan
curves. Calling that a supported air-side system would make the most important limitation invisible.

**Rejected.**
- *Alias fan to pump and coil to exchanger.* Produces a drawable approximation. Cost: reads as real
  support while omitting the equations that distinguish air-side design.
- *Remove psychrometrics until air networks ship.* Cleaner scope. Cost: loses early validation of the
  abstraction and delays a high-risk property basis issue.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`72-roadmap`](../70-future/72-roadmap.md), and `R-08`.

---

## D-29 · Delivery follows risk-separated vertical slices

**Accepted, amended by `D-32` · 2026-08-30**

M2 is divided into **M2a hydraulic core** and **M2b coupled thermal rating**. M3 delivers the usable
static product, including local file lifecycle, declarative symbols, accessibility, and SVG/PNG export.
M4 requires PI control; PID derivative mode is optional until a use case needs noise filtering.
Evolutionary sizing, DXF, and subsystem composition remain M6 evidence-driven work. XML interchange
is removed until an external consumer requires a schema compatibility commitment.

**Why.** The rated exchanger adds a second hydraulic component and fixed-point geometry sizing — a
different risk from proving a single-loop Newton solve. File persistence and static export, by
contrast, are necessary for the first useful frontend session. Phase boundaries should follow
independently testable user outcomes, not the order features were imagined.

**Rejected.**
- *One large M2.* Fewer labels. Cost: exchanger coupling can hide whether the hydraulic foundation
  ever became trustworthy.
- *Keep persistence and export in M6.* Smaller M3. Cost: the visible product cannot preserve or share
  its result.
- *Commit now to XML and a cost optimizer.* Rich interchange and optimization. Cost: compatibility and
  data-model promises with no consumer or cost source.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`35-evolutionary-sizing`](../30-solver/35-evolutionary-sizing.md),
[`52-editor`](../50-frontend/52-editor.md),
[`71-export-formats`](../70-future/71-export-formats.md),
[`72-roadmap`](../70-future/72-roadmap.md), `R-31`, and `R-38`.

---

## D-30 · v1 uses explicit defaults for the remaining implementation-shaping questions

**Accepted, amended by `D-32` · 2026-08-30**

The remediation pass closes questions whose recommended answer is sufficient for v1. Measurement may
supersede a numeric threshold through `07`'s evidence process; it does not leave the implementation to
choose a policy ad hoc.

| Area | v1 decision |
|---|---|
| Valve coefficients | Accept `kv=` only. `cv=` is an unknown parameter with guidance to convert; silent imperial conversion is not part of v1. |
| Architecture/contracts | A `dotnet test` architecture test enforces Core's allowed references. Shared JSON Schemas generate REST and realtime DTOs for C# and TypeScript; OpenAPI documents REST but is not the transport source of truth. |
| Stable inferred ids | I2 uses the ordered endpoint pair and a source-order ordinal for repeated pairs. Unrelated edits do not renumber it. |
| Physical thresholds | One versioned Core table supplies fixed v1 thresholds and metadata/docs. Per-script/project overrides are post-v1. |
| Formatting/write-back | Format aligns only consecutive non-blank declaration runs. Write-back preserves the user's existing unit spelling and scale. |
| Topology metadata | `ComponentKindInfo.DrivesFlow` is explicit; residual inspection is forbidden. |
| Layout | Loop members have no `Rank`; `Loops` owns them. Core supplies orientation derived from flow leaving the first flow driver. Shared loops render side-by-side around the shared component. Groups over 10 members, or scenes over 500 elements, start collapsed. |
| Solver | Sizing/deferred expressions remain outside Newton in one outer loop. Steady and transient expose cancellable `Task` APIs and execute on dedicated workers. v1 uses dense LU up to the published 800-unknown hard limit; M0/M2 benchmarks must meet `07`, and there is no modified-Newton reuse. |
| Evolutionary extension | M6 starts with the evolutionary algorithm and engineering objectives; cost objectives require a user-supplied versioned table. Accepting a result writes all chosen parameters in one previewed/undoable script transaction. |
| Diagnostics | `/validate` supplies early syntax/bind feedback; `/compile` supplies the full result. Transient diagnostics carry stable occurrence ids plus started/cleared events, and the log retains their time intervals. |
| Visual design | Pin Dark+/Light+ token values. Temperature colouring is on by default with a toggle. “Fun” means restrained responsive feedback, not illustrated symbols. |
| Visualization | `show` is script-owned; a UI override is session-only and does not write back. The existing range grammar fixes domains. A multi-state component's representative value is its downstream/outlet state. Cross-design comparison is post-v1 and uses explicit fixed domains. |
| Documentation/repository | v1 docs are plain Markdown. Metadata links the docs index. `plan/` and the undated evidence-gated roadmap remain public with explanatory READMEs. |
| Delivery | No hosted demo is promised in v1; use screenshots/video. Property accuracy runs on Linux on every PR after M0 proves packaging, with supported-platform smoke tests before release. Application releases use 0.x from M2 and reach 1.0 only at M5 exit; language/contract majors remain independent. |
| Agent guidance | Add no project-owned always-loaded `.claude/rules/` file. Project-local skills are listed in the workflow paragraph/reference index. |

**Why one entry.** These questions all had a recommended conservative answer and no missing user
outcome. Leaving them open transfers design work to whichever agent first touches the code and causes
different surfaces to choose separately. The choices above favour one source of truth, deterministic
output, reversible edits, and measurement before optimization.

**Rejected.**
- *Keep them open until implementation.* Maximum flexibility. Cost: public shapes and acceptance tests
  are selected accidentally in code, which is exactly what the planning phase exists to prevent.
- *Implement every flexible form now.* Configurable thresholds, Cv conversion, generated sites,
  sparse/modified Newton, hosted deployment, and richer reporting. Cost: several new compatibility,
  security, documentation, and validation surfaces before the core product works.
- *Delete the features behind the questions.* Smaller. Cost: removes required v1 workflows such as
  deterministic layout, transient warning history, contract generation, and reproducible write-back.

**Constrains.** `02`, `04`, `11`, `16`, `17`, `23`, `25`, `31`, `32`, `35`, `44`, `53`, `55`, `56`,
`57`, `61`, `63`, `64`, `72`, plus `03`, `42`, `43`, and `51` where those contracts are consumed.

---

## D-31 · Plant diagrams follow thermal progression from left to right

**Accepted · 2026-08-30**

The computed P&I layout places the plant's **nominal thermal progression** from left to right:
environmental and cooling-side sources first, conversion/coupling equipment and storage next, and
heating consumers last. Parallel sources share a left-side stage; parallel heating networks share a
right-side stage. A coupled source circuit, such as a ground loop connected through an exchanger,
stays on the source side of the component receiving its heat.

This is not a claim that every fluid arrow points right. Closed hydronic circuits necessarily have
return branches whose mass flow runs right to left. Core therefore emits thermal stages separately
from per-connection fluid-flow direction. The renderer preserves monotonically increasing X for the
thermal stages and routes supply/return branches around them without reversing the engineering arrows.

Thermal stages are fixed from the compiled design point: stated duties and temperatures first,
otherwise nominal connection direction and source order. A transient reversal changes arrows and
state, not placement. Components with no defensible thermal role stay with their hydraulic group and
use the existing deterministic order. This keeps an incomplete or temporarily unsolved draft stable.

**Why.** A hydronic graph is cyclic, so a traversal from the pressure datum is not a useful statement
of plant intent: changing which node supplies the arbitrary datum can put a radiator left of its heat
source. Designers read plants as energy chains. Keeping source circuits left, conversion/storage in
the middle, and heating networks right makes a multi-source, multi-consumer system readable without
manual coordinates and matches the way the script is meant to be authored.

**Rejected.**
- *Lay out strictly in fluid-flow order.* Correct for one supply branch, but every return branch then
  fights the same ordering and coupled circuits can appear on either side of their exchanger.
- *Use declaration order as X position.* Stable and simple. Cost: it makes topology formatting a
  drawing API, and one declaration moved for readability rotates the plant.
- *Reorder from every solved frame.* Reflects reversible operation immediately. Cost: the whole
  diagram jumps when a duty crosses zero, violating the stable live-canvas and transient contracts.

**Constrains.** [`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`59-static-export`](../50-frontend/59-static-export.md), and `R-44`.

---

## D-32 · v1 includes a finite-volume stratified tank

**Accepted · 2026-08-30** · *amends `D-02`, `D-11`, `D-14`, `D-29`, and `D-30`*

`tank` is a v1 flow component delivered in M4. `container` is a curated kind alias. It has indexed
ports `in1`…`in16` and `out1`…`out16`; `in1` and `out1` always exist, and further ports materialise
when a qualified connection or matching elevation parameter names them. Multiple-port scripts qualify
the endpoints (`T1.in2`) so their intent survives reordering.

The canonical parameters are `volume`, `layers`, `t`, indexed initial temperatures `t1`…`tN`, and
indexed normalized elevations such as `in2_elevation`. `v` is a curated parameter alias for `volume`.
Bare `Volume` is **dm³**, a fourth dimension-wide exception to `D-14`; `l` and `dm3` remain equivalent
explicit spellings. Omitted `volume` is the explicit default **300 dm³**, omitted `layers` is **5**,
and omitted port elevation is **0.5**. These are visible `source=default` values, not auto-sized
values. They are narrow exceptions to `D-02`: no sizing rule can infer storage capacity or port
height from a hydraulic solve, and pretending otherwise would be less honest than a declared default.

Each of the N equal-volume layers is perfectly mixed and isothermal; the stack of layers is the
stratification model, ordered bottom to top. Elevation is dimensionless height in `[0,1]`, mapped to
one layer, because volume alone does not determine physical vessel height. `layers=1` is the fully
mixed model. In steady state the tank is one mixed junction—volume and layer count have no equilibrium
effect. In a transient, external streams enter or leave the layer at their port elevation, adjacent
layer displacement enforces fixed volume and exact mass balance, and each layer integrates enthalpy.
After an accepted step, adjacent density inversions remix minimally until density is non-increasing
from bottom to top, conserving total mass and enthalpy. Jet entrainment, wall conduction, ambient heat
loss, and hydrostatic pressure differences are not inferred in v1.

`t=` initializes every layer uniformly. Alternatively all `t1`…`tN` may state a bottom-to-top initial
profile; a partial profile or using `t` with any indexed temperature is an error. With neither form,
the mixed steady solution initializes every layer. Actual flow sign, not the `in`/`out` prefix, decides
whether a reversed stream enters or leaves during a solve.

[`01-vision-and-scope`](01-vision-and-scope.md) adds the named **storage header** reference case for
the component, multiple source/load branches, thermal layout, and transient energy conservation. It
is the fifth reference circuit; `D-11`'s ban on unnamed variants now covers five.

**Why.** Buffer vessels are normal plant equipment, and without one the time-domain model can represent
pipe delay but not intentional thermal storage. A one-node tank loses stratification; a continuous
1D model adds a PDE and a mesh policy out of proportion to v1. Equal-volume mixed layers give a
conservative finite-volume model whose accuracy is directly controlled by one visible parameter.

**Rejected.**
- *Keep tanks post-v1.* Smaller M4. Cost: the transient milestone can model accidental pipe volume
  but not the storage component central to multi-source heating plants.
- *One perfectly mixed node only.* Simple and useful for hydraulic buffering. Cost: cannot represent
  the usable hot layer above a cool return, which is the reason inlet/outlet elevation matters.
- *A continuous stratification PDE or CFD model.* Higher fidelity. Cost: contradicts the lumped-model
  scope, needs geometry and calibration the script does not carry, and cannot justify a default mesh.
- *Physical elevation in metres.* Familiar dimension. Cost: `volume=300` gives no vessel height, so
  validating a 1.2 m port would require invented geometry. Normalized height is complete as written.
- *Unbounded dynamic ports.* Expressive but makes registry metadata, symbols, and input limits open
  ended. Sixteen of each direction covers the supported v1 scale; exceeding it is a clear diagnostic.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md),
[`02-glossary`](02-glossary.md), [`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`07-quality-attributes`](07-quality-attributes.md),
[`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md),
[`18-script-compatibility`](../10-language/18-script-compatibility.md),
[`22-component-model`](../20-core-domain/22-component-model.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md),
[`24-auto-sizing`](../20-core-domain/24-auto-sizing.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`33-transient-time-domain`](../30-solver/33-transient-time-domain.md),
[`42-rest-contract`](../40-api/42-rest-contract.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md),
[`52-editor`](../50-frontend/52-editor.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`57-state-visualization`](../50-frontend/57-state-visualization.md),
[`61-documentation-plan`](../60-docs-and-devex/61-documentation-plan.md),
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md),
[`72-roadmap`](../70-future/72-roadmap.md), `R-09`, and `R-45`.

---

## D-33 · A script holds several numbered circuits, and subcircuits attach explicitly

**Accepted · 2026-08-31**

A script may declare more than one circuit. `circuit <name> [<number>]` gains an optional integer
designation — `circuit groundSource 400` — and `SemanticModel.Circuit` becomes `Circuits`. An omitted
number is resolved automatically: the lowest unused multiple of 100 in declaration order, so a
single-circuit script never mentions a number and every existing script keeps its meaning.

A **subcircuit** is a circuit that attaches to another rather than standing alone. It states its
attachment explicitly, with two statements naming the parent's nodes:

```fluidscript
circuit AHU 101
HE1 duty in=50 out=30 power=24 kW
TV1 three_way_valve
PU1 pump

supply N3        # takes flow from the parent circuit at N3
return N5        # returns it to the parent at N5
```

There is no automatic attachment. `supply`/`return` become reserved words; `in`/`out` were the obvious
spelling and are rejected below.

This entry also adds the **sixth reference circuit, the distribution header** — one parent circuit
numbered 100 with two subcircuits 101 and 102 on a shared supply/return pair — and amends `D-11`
accordingly. Without it, `D-34`, `D-36` and `D-38` have no fixture to be tested against.

**Why.** Every real plant in the reference drawings is several numbered circuits sharing a
distribution header, and the tagging scheme (`D-34`), the ownership rule (`D-36`) and the header
layout (`D-38`) are all expressed *per circuit*. A one-circuit model cannot express any of them.
Attachment is explicit because the alternative is inferring which parent node a subcircuit belongs to,
and a wrong inference there is a wrong hydraulic topology that still solves — the failure class `P3`
exists to refuse.

**Rejected.**
- *`in N3` / `out N5` as the attachment keywords.* Matches the user's own first draft and reads well.
  Cost: `in N3` already parses today — the disambiguation rule in
  [`12-grammar`](../10-language/12-grammar.md) sees a first token that is not reserved and a second
  token that is not `-`, and produces a component named `in` of kind `N3`. Silently. Reserving `in`
  and `out` would fix the parse but collides with `in=`/`out=` as heat-exchanger parameter names,
  where the same two words already mean inlet and outlet temperature. Two meanings, one word, one
  document — the collision would be discovered by a user, not by a test.
- *Automatic attachment by proximity or by declaration order.* Removes two lines per subcircuit.
  Cost: the attachment point determines flow split and pressure drop; guessing it produces a model
  that solves and is wrong, and the user has no way to see what was assumed.
- *One circuit per file, composed by an include mechanism.* Keeps the semantic model singular. Cost:
  `R-03` has no imports and adding them makes the language a build system; a header and its branches
  are one drawing and belong in one file.

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md),
[`02-glossary`](02-glossary.md),
[`12-grammar`](../10-language/12-grammar.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`16-diagnostics`](../10-language/16-diagnostics.md),
[`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md), `D-11`, and `R-46`.

---

## D-34 · Equipment tags are derived metadata, never identity

**Accepted · 2026-08-31**

Every device carries a **tag** of the form `<circuit><code><ordinal>` — `400PU01`, `101TV01`,
`100S02` — derived by Core and carried in the model contract. It is *not* the component's identifier.
The script keeps the user's name, `PU1`, and that name remains the stable id
[`25-layout-hints`](../20-core-domain/25-layout-hints.md) keys selection, DOM reconciliation, worker
commits and export identity to.

| Part | Source |
|---|---|
| `<circuit>` | The circuit's number (`D-33`) |
| `<code>` | A `TagCode` field on the component kind in the registry — `PU`, `HE`, `TV`, `S` |
| `<ordinal>` | Two digits, per `(circuit, code)`, **in declaration order**, from `01` |

An optional `.NN` branch extension — `100TE01.02` — appends the branch ordinal for a component on a
numbered branch of a distribution header. The format is fixed now so it does not change later; v1
emits it only for devices, because the case that motivates it most (a supply and return sensor per
branch) needs the sensors `D-23` defers.

Tags never enter the script by themselves. An explicit **Apply tags** editor operation rewrites them
in as identifiers, reported through `IScriptEditor`'s old-id/new-id mapping like any other rename.

**Why.** Ordinals renumber whenever a declaration is inserted above another. If the tag were identity,
that renumbering would invalidate selection, diagnostic anchors, route caches and export identity on a
keystroke — the diagram-jumps-while-typing failure that
[`25-layout-hints`](../20-core-domain/25-layout-hints.md)'s whole determinism section exists to
prevent. Declaration order rather than topological order for the same reason: topological ordinals
churn every time a connection is edited, and a connection edit is the most common edit there is.

The cost is real and is accepted: writing the return line before the supply line gives the return the
lower ordinal. That is visible, local, and fixed by moving one line — unlike a tag that changes because
something three screens away changed.

**Rejected.**
- *The tag is the identifier; auto-naming rewrites the script.* Makes "every device is auto renamed"
  literally true. Cost: the churn above, on every keystroke, against every consumer keyed by id.
- *Topological ordinals, supply before return.* Matches how a drafter numbers a finished drawing.
  Cost: a finished drawing is not edited live. Tags would move under the cursor whenever a connection
  changed, and every diagnostic and export identity would move with them.
- *An explicit `tag=` parameter on every component.* Fully predictable, no inference. Cost: it is the
  noise `P1` exists to prevent, and it defeats the feature — the point is not typing them.

**Constrains.** [`15-semantic-model`](../10-language/15-semantic-model.md),
[`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md),
[`22-component-model`](../20-core-domain/22-component-model.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`59-static-export`](../50-frontend/59-static-export.md), `D-23`, and `R-47`.

---

## D-35 · Circuit roles resolve through a registry, not through keywords

**Accepted · 2026-08-31**

`circuit AHU 101` and `circuit radiators 102` classify the circuit by **role**, and the role name
resolves against a circuit-role registry using `D-15`'s existing three stages — normalise, exact match
against canonical names and curated aliases, then similarity. `AHU`, `radiator`, `hot_water` and
`ground_loop` are registry entries, not reserved words. An unresolved name is not an error: the circuit
gets the `Neutral` role and an info diagnostic, exactly as an unresolved component kind still produces
a component.

The role feeds `D-31`'s thermal classification — a `radiator` role is a `Consumer`, a `ground_loop` is
a `Source` — which is what places the circuit on the canvas.

**Why.** The reserved-word list is eleven words and `P6` keeps it that way. Component *kinds* are
already registry-resolved precisely so that adding a kind never breaks a script that used the name as
an identifier; circuit roles are the same shape of problem and get the same mechanism rather than a
second one. Making `AHU` a keyword would mean every new consumer type is a language change with a
version bump behind it, and any script with a component named `AHU` breaks on upgrade.

**Rejected.**
- *`AHU` and `radiator` as reserved words.* Simplest to parse. Cost: an unbounded reserved list that
  grows with the component library, each addition breaking existing scripts, for no expressive gain
  over a registry lookup.
- *An explicit `role=` parameter on the circuit header.* Unambiguous. Cost: `circuit AHU 101 role=ahu`
  says the same thing twice, and the name is already the natural place to say it.
- *Infer the role from the circuit's contents.* No new syntax at all. Cost: a circuit with a pump and
  an exchanger is a source or a consumer depending entirely on which side of the exchanger it is on,
  which is what `D-36` has to resolve separately — inference here would double-guess it.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md), `D-15`, `D-31`, and `R-46`.

---

## D-36 · A two-sided component belongs to the circuit on its enthalpy-losing side

**Accepted · 2026-08-31**

A component touching two circuits — a rated exchanger, a heat pump, a tank with coils — is owned by
exactly one of them for tagging (`D-34`) and grouping. The owner is the circuit on the side **losing**
nominal enthalpy across the component's heat-transfer edge. A heat pump cooling circuit 400 and heating
circuit 100 is `400HP01`. A tank charged by circuit 100 and discharging to hot-water circuit 201 is
`100S02`.

Fallbacks, in order, when that edge does not decide it: both sides in one circuit → that circuit; one
side against a boundary → the circuit side; still undecided → the lower circuit number, with an info
diagnostic naming the ambiguity.

**Why.** The intuitive statement of this rule is "the leftmost circuit owns it", and that is what a
designer reading a drawing sees. But leftmost is a *layout outcome* and `D-03` forbids Core from
knowing about pixels — a Core-computed tag cannot depend on where the renderer put something. The
enthalpy-losing side is the same rule stated in Core's own terms:
[`25-layout-hints`](../20-core-domain/25-layout-hints.md) already builds exactly that directed edge
for thermal staging, "from the side losing nominal enthalpy to the side gaining it". Under `D-31` the
losing side is the left one, so the rule and the intuition agree — but the rule is computable without
a canvas and testable without a renderer.

**Rejected.**
- *"The leftmost circuit owns it", taken literally.* Matches how a designer describes it. Cost:
  violates `D-03`; Core would need layout coordinates to compute a tag, and the tag would change when
  the renderer changed.
- *The circuit that declared the component owns it.* Trivial to compute, no new concept. Cost:
  declaration order is a text-editing accident. Moving a line between two circuit blocks would
  renumber equipment on a drawing, which is the property `D-34` is built to avoid.
- *Ownership by both, with a compound tag.* Loses no information. Cost: `100/400HP01` is not a tag any
  plant convention uses, and it doubles every equipment schedule row.

**Constrains.** [`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md), `D-03`, `D-31`, `D-34`, and `R-47`.

---

## D-37 · `project` carries the solve mode; `spacing` is presentation, never a hint

**Accepted · 2026-08-31**

Two global directives, valid only in the declaration section and only after the version directive:

```fluidscript
fluidscript 1
project dynamic plant_01     # names the project; `dynamic` is the default solve mode for every circuit
spacing 20                   # component spacing on the canvas, in world units
```

`project [dynamic|static] <name>` sets the **default** solve mode for every circuit in the file. A
circuit's own `fluid dynamic|static` still wins locally; stating both with different modes is a
warning, not a silent resolution, because the two readings differ and neither is obviously right.

`spacing` lands in `StyleSettings` — the opaque presentation payload Core already parses and passes
through untouched — and **not** in `LayoutHints`. Core never interprets it. The default is sparse.

**Why.** `spacing` is a distance, and `LayoutHints` invariant 1 is that it contains no coordinate,
dimension or pixel value; `D-03` puts every such quantity on the frontend side of the line. The
temptation is to treat spacing as "layout, therefore a layout hint", which would put the first number
into a payload whose entire testable property is that it holds none. `style` already exists as the
channel for presentation values that travel through Core without being understood by it, and spacing is
exactly that.

Solve mode moves up because it is a property of the run, not of a fluid: stating `dynamic` once per
circuit in a six-circuit file is five repetitions of one decision.

**Rejected.**
- *`spacing` as a field on `LayoutHints`.* Keeps all layout inputs in one payload. Cost: breaks
  invariant 1 and `D-03`, and makes Core's layout tests depend on a number Core cannot check.
- *`project` replaces the `fluidscript` version directive.* One fewer line. Cost:
  [`18-script-compatibility`](../10-language/18-script-compatibility.md) requires the version to be
  the first non-trivia token so a file can be rejected before it is parsed; a project name in front of
  it means parsing an unsupported file to find out it is unsupported.
- *Project-level mode only, dropping per-circuit `fluid dynamic`.* Simplest precedence — there is
  none. Cost: a mixed model where one slow circuit is transient and the rest are steady becomes
  inexpressible, and that is a real design question users ask.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`55-design-system`](../50-frontend/55-design-system.md), `D-03`, and `R-48`.

---

## D-38 · Header layout is a second layout mode beside the loop rectangle

**Accepted · 2026-08-31**

[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md) gains a second layout mode. The existing
mode lays a loop out as a rectangle with components on its perimeter. The **header** mode lays a
distribution circuit out as a horizontal supply line along the top and a return line along the bottom,
with its subcircuits stacked vertically *between* them, each connecting up to supply and down to
return. A branch leaving the header continues away from it vertically and then turns in the heat
direction — right for heating, left for cooling, per `D-31`.

Selection is structural, not stylistic: a circuit with two or more subcircuits attached to a shared
supply/return pair (`D-33`) renders as a header; everything else keeps the rectangle. Core supplies the
grouping through `LayoutHints`; the renderer owns every coordinate, as `D-03` requires.

**Why.** The rectangle is the right picture for one closed loop and the wrong one for a plant. Every
reference drawing this project is measured against is a header with branches, and a renderer that only
knows rectangles draws a six-circuit plant as six disconnected rectangles with long routes between
them — technically correct, unrecognisable to a designer, and a direct failure of `R-27`. Two modes
rather than one general algorithm because the two shapes have genuinely different rules, and a general
algorithm that produced both would be tuned until it produced one badly.

**Rejected.**
- *One generalised layout algorithm covering both.* No mode selection to get wrong. Cost: the
  constraints differ (a loop distributes around a perimeter, a header stacks between two rails); a
  single parameterised algorithm is the force-directed trap `53` already rejects, one level up.
- *Header layout only, dropping the rectangle.* One mode. Cost: the cooling loop and simple loop —
  two protected reference circuits — are single loops with no header, and both would regress.
- *Let the script choose the mode with a directive.* Explicit and predictable. Cost: it asks the user
  to describe the drawing rather than the plant, which is the line `R-01` draws.

**Constrains.** [`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`55-design-system`](../50-frontend/55-design-system.md), `D-03`, `D-31`, `D-33`, and `R-48`.

---

## D-39 · Documents are tabbed, and a run detached by a tab switch keeps running

**Accepted, amended by `D-42` · 2026-08-31**

[`58-file-lifecycle`](../50-frontend/58-file-lifecycle.md)'s single `DocumentState` becomes a
collection with one active document. Each tab owns its own source, dirty state, file handle, recovery
entry and run. Only the active document renders and streams frames.

Switching tabs **detaches rendering; it does not stop the run.** The transient continues from its
immutable snapshot, off the UI thread, and switching back resumes playback from the frames it
produced. A run is stopped only by the user's Stop, by closing its document, or by leaving the
application. [`07-quality-attributes`](07-quality-attributes.md) owns the cap on concurrent runs and
the memory budget that follows from it.

**Why.** The brief for tabs said the previous tab "stops converging" on switch, and that is the one
part rejected. `D-22` and `R-41` establish that a run owns an immutable snapshot precisely so that
activity in the editor cannot destroy it; a tab switch is a cheaper and more frequent gesture than an
edit, and having it silently discard a 600-frame run would defeat the isolation `D-22` exists to
provide. The computation is already off the UI thread, so nothing about the responsiveness argument
requires stopping it — only the *rendering* needs to stop, and that is what detaching does.

**Rejected.**
- *A tab switch stops the run.* The simplest resource story: at most one run exists. Cost: silent loss
  of completed work on a stray click, and a new cancel path contradicting `D-22`'s guarantee.
- *Prompt on switch — keep running or stop.* No silent loss. Cost: a modal in the most repeated
  interaction in the application.
- *Background tabs keep rendering.* No resume logic. Cost: pays full frame-application and
  render-preparation cost for pixels nobody sees, against `07`'s budgets.

**Constrains.** [`07-quality-attributes`](07-quality-attributes.md),
[`51-frontend-architecture`](../50-frontend/51-frontend-architecture.md),
[`58-file-lifecycle`](../50-frontend/58-file-lifecycle.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md), `D-22`, `R-41`, and `R-50`.

---

## D-40 · A controller is defined once and bound separately, by named role

**Accepted, amended by `D-43` · 2026-08-31**

The single controller declaration of
[`34-controllers`](../30-solver/34-controllers.md) splits into a definition and a binding:

```fluidscript
PID1 pid kp=3                                       # definition: an ordinary component declaration
control actuate=TV1 measure=N2.t by=PID1 setpoint=20 # binding: `control` is a reserved word
```

The definition is a component declaration and needs no new grammar — `pid`, `pi` and `p` resolve
through the registry like any other kind (`D-15`). The binding is a new statement whose arguments are
**named, not positional**. `setpoint` stays on the binding, because under `D-23` there is no sensor
component to carry it.

**Why.** Splitting them lets one tuning be stated once and read at each place it is used, and it puts
the gains next to the algorithm rather than in the middle of a wiring statement. Named arguments
because the positional form — `control TV1 TE01 PID1` — has no memorable order: reversing the
actuator and the measurement produces a model that binds, solves, and is wrong. A binding that fails
loudly on a typo is worth four extra words.

The `dT` mode from the original sketch is **not** included. Its stated meaning, "measured by a setpoint
and actual temperature difference", is the error signal every controller already computes, so the
keyword would name the default. A genuine two-sensor differential mode needs two measurement points
and can be added later without changing this syntax, since every argument is named.

**Rejected.**
- *Keep the single `TC1 controller measure=… actuate=… setpoint=…` declaration.* No new statement, and
  it already works. Cost: the gains and the wiring are one line, so a retuning edit and a rewiring edit
  touch the same statement, and the same tuning cannot be shared.
- *Positional binding arguments.* Shortest to type, closest to the original sketch. Cost: silent
  argument transposition, above.
- *A `dT` mode keyword now.* Matches the sketch. Cost: it would name the default behaviour, and the
  name would then be unavailable for the real differential mode.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`16-diagnostics`](../10-language/16-diagnostics.md),
[`34-controllers`](../30-solver/34-controllers.md),
[`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md), `D-15`, `D-23`, and `R-49`.

---

## D-41 · Component identifiers are unique across the script; circuits scope tags, not names

**Accepted · 2026-09-01**

A component's identifier is unique in the whole file. Two circuits may not both declare `PU1`; the
second is `FS1501`, the duplicate-name error that already exists. **Circuits scope equipment tags
(`D-34`), not identifiers** — `101PU01` and `102PU01` are distinct tags belonging to components that
must be named, say, `PU_AHU` and `PU_RAD`.

`15-semantic-model`'s invariant 2 is corrected from "unique within its circuit" to unique within the
model. Every id-keyed structure stays a flat `string`: `LayoutHints.Order`, `Rank`, `Inferred`,
`ComponentGroup.Children`, `26`'s `component.id`, `IScriptEditor`'s parameters, `SymbolMap`, DOM keys
and export ids are all unchanged by `D-33`.

**Why.** The phrase "within its circuit" predates `D-33` and was written when a script held exactly one
circuit, where it meant the same as "within the model". `D-33` made a dormant qualifier load-bearing
without supplying anything a real namespace needs, and the result was incoherent: `25-layout-hints`
warned that a renderer keying components by bare name "would silently collapse the two", while
`LayoutHints.Rank` is itself an `ImmutableDictionary<string, int>` keyed by bare name. Core would have
committed the error the document warns the renderer against.

Restoring global uniqueness is the fix that **removes** machinery rather than adding it. Per-circuit
naming would require, at minimum: a qualification syntax in the language, a qualified-id type threaded
through four documents and every consumer, cross-circuit reference rules, and a rewrite of every
worked example in the tree. It would also be **the only scoping construct in the language** — `R-03`
has no control flow, no functions, and no imports — introduced so that two components may share a
name.

The qualification syntax alone is disqualifying. `.` is already the port separator (`3WV.b`) and the
property separator (`N2.t`), so `AHU.PU1` is lexically indistinguishable from a property reference,
and any other separator is a new token in a language whose reserved list is deliberately tiny (`P6`).

**Cross-circuit references settle themselves under this rule.** `supply N3` names a node in the parent
circuit, and a `control` binding may measure and actuate across circuits. With unique identifiers each
of those is an ordinary lookup; with per-circuit scoping every one needs a qualified form that does not
exist.

The cost is real and small: a user copying a subcircuit block must rename its components. They are
already renaming the circuit and choosing its number, and the diagram still distinguishes the two
pumps, because the *tags* do exactly what the user wanted them to do.

**Rejected.**
- *Qualify ids at construction as `circuit.name` (`AHU.PU1`).* Keeps every flat structure and needs no
  new type. Cost: collides lexically with property references; and either every id in every existing
  worked example changes — breaking `D-11`'s reproduce-the-figures rule across nine documents — or
  qualification applies only when ambiguous, which makes an id context-dependent, so adding a second
  circuit silently renames a component in the first.
- *A `ComponentId(string Circuit, string Name)` record struct.* Type-safe, and the compiler finds every
  site. Cost: a two-field key in every hot dictionary, an object-or-encoded-string decision on the
  wire, and signature churn through four documents — all to buy a namespace nothing else in the
  language has.
- *Let the binder disambiguate duplicates automatically.* No user-visible constraint at all. Cost: it
  reintroduces exactly the instability `D-34` rejects for tags — adding a circuit would rename a
  component in another one, invalidating selection, diagnostics and export identity.

**Constrains.** [`15-semantic-model`](../10-language/15-semantic-model.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md),
[`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md), `D-33`, `D-34`,
and `R-46`.

---

## D-42 · Detaching a run stops presentation, not transport

**Accepted · 2026-09-01 · amends `D-39`**

A run whose document is not active **keeps receiving and reconstructing frames**. Detaching removes
only the presentation stages: colour scales, geometry, and the DOM commit. Decode, delta application,
checksum verification and checkpoint retention continue at full rate.

This supersedes `D-39`'s clause "Only the active document renders and streams frames", which
contradicts `D-39`'s own next paragraph and
[`43-realtime-contract`](../40-api/43-realtime-contract.md). The correct phrasing is **only the active
document performs presentation work**. `R-50` and the glossary's *Active document* entry are corrected
to match; `43`'s detached-run section was already right and is unchanged.

**Why.** Frames are deltas against the previous frame (`43` invariant 8). A client that stops applying
them holds state at frame 200 while the socket delivers frame 640, and cannot bridge the gap without a
reconnection and a fresh `base` — so "stops streaming" and "resumes on switching back" cannot both be
true. `D-39` decided the run continues; that decision is unchanged and this entry states what it costs.

**Rejected.**
- *Stop draining the socket while detached.* Saves decode and delta work for frames nobody watches.
  Cost: backpressure stalls the solver against a client that is deliberately not reading, and resume
  needs a fresh `base` — which throws away the scrubbing history for the detached interval.
- *Buffer frames server-side and replay on reattach.* Also avoids client work. Cost: unbounded
  server memory per detached run, and v1 has no detached server jobs
  ([`58-file-lifecycle`](../50-frontend/58-file-lifecycle.md)).

**Constrains.** [`01-vision-and-scope`](01-vision-and-scope.md) (`R-50`),
[`02-glossary`](02-glossary.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md),
[`58-file-lifecycle`](../50-frontend/58-file-lifecycle.md), `D-39`, and `R-50`.

---

## D-43 · A control binding's actuator is a qualified parameter reference

**Accepted · 2026-09-01 · amends `D-40`**

`actuate=` takes `component.parameter` — `actuate=3WV.position`, never `actuate=3WV`. This matches
`ControlBindingSymbol.Actuator`'s declared `ParameterReference` type
([`15-semantic-model`](../10-language/15-semantic-model.md)) and the symmetry with `measure=N2.t`.
`D-40`'s illustrative line and the examples derived from it wrote the bare component name and are
corrected.

**Why.** A bare component name would require a "default actuated parameter" per kind, and there is no
defensible default for several: a pump can be driven by speed or by head, and a three-way valve's
`position` is obvious only until a second settable parameter exists. The shorthand also breaks the
symmetry that makes the statement readable — `measure=` already names a property, so `actuate=` naming
a parameter is the form a reader predicts.

**Rejected.**
- *Allow the bare component name as shorthand for its default actuated parameter.* Shorter, and `P1`
  favours brevity. Cost: a registry field naming each kind's default actuator, a diagnostic for kinds
  with no default, and two spellings for one thing (`P6`). The saved characters are four.
- *Accept both forms.* No migration of existing examples. Cost: the same `P6` violation, plus a
  script whose meaning depends on which form the author happened to use.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`11-language-overview`](../10-language/11-language-overview.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`34-controllers`](../30-solver/34-controllers.md), `D-40`, and `R-49`.

---

## D-44 · A pipe turns on bare pipe; no component is ever placed at a corner

**Accepted · 2026-09-01**

**A corner carries no component. Ever.** Where a run changes direction, the turn is made on bare pipe,
and every component — oriented or not, declared or inferred, node included — is placed on a straight
section. This is a hard rule with no exceptions and no severity ladder: a layout that puts a component
at a corner is a renderer invariant breach, not a degraded result.

A **corner** is a point where a run changes direction and nothing branches. A **junction**, where three
or more runs meet, is not a corner: a three-way valve belongs at its T because it *is* the junction,
and a node belongs where connections actually meet. The rule constrains turns, not branches.

When a placement would otherwise land a component on a corner, the renderer lengthens the affected run
and reflows — deterministically, as it already does for overlap — until every component sits on a
straight section. Running out of room is an invariant failure that surfaces as one; it is never
resolved by placing the component anyway.

**Why.** Two reasons, and the second is the one that matters.

The first is that it looks wrong. A symbol at a bend absorbs a 90° turn in its own geometry: an
exchanger's two passes stop being parallel, a pump's triangle points diagonally, a valve's bowtie
becomes a chevron. A reader can no longer tell which way anything flows, at exactly the components they
look at first.

The second is that **it is wrong**. Plants are not built this way. Nobody welds an elbow into a pump
casing or bends a plate exchanger around a corner; the turn is made in pipework and the equipment sits
in a straight run, because that is what is fabricable, supportable and maintainable. A diagram that
puts equipment in an elbow depicts an installation that cannot exist, and the whole point of this tool
is that its output resembles the plant.

Stated as a hard rule rather than a preference because a soft version has one outcome: it holds until
the first cramped layout, then quietly stops holding, and the exception becomes the common case in
exactly the dense diagrams where legibility matters most.

**Rejected.**
- *Allow it when space is tight, with a rotation penalty.* What a generic graph layout does, and it
  always fits. Cost: the tight cases are the crowded ones, so the rule would be suspended precisely
  where it is most needed, and the diagram degrades without saying so.
- *Restrict it to oriented components, letting nodes sit on corners.* Nearly free — a node is a dot and
  has no direction to get wrong. Cost: two rules where one would do, a per-kind exception list to
  maintain, and the reflow machinery is needed anyway for the oriented case. A dot on a bend also
  hides a real junction: a reader cannot tell a routing corner from a two-connection node.
- *Leave it as a renderer guideline rather than a binding decision.* No decision-log entry, less
  ceremony. Cost: `53`'s first version had no orientation policy at all and placed the cooling loop's
  exchanger on a corner for exactly that reason. A guideline is what this already was.

**Constrains.** [`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md),
[`25-layout-hints`](../20-core-domain/25-layout-hints.md),
[`59-static-export`](../50-frontend/59-static-export.md),
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md), `D-03`, `D-38`, and `R-48`.

---

## D-45 · A quality harness installs at M0; its baseline is recorded by the milestone that first produces the artifact

**Accepted · 2026-09-01**

`05`'s M0 gate asks for benchmark fixtures with baselines for "200 declarations, 200 solver unknowns,
800-unknown refusal/support behavior, and a 200-component render", and for accessibility automation
plus keyboard and screen-reader harnesses running in CI. Three of those measure artifacts that do not
exist at M0: there is no solver until M2a and no renderer, and nothing accessible, until M3.

**The two are separated.** M0 installs the **machinery** — the benchmark project, the trait
categories, Verify, Vitest, Playwright, axe, and a recorded `benchmarks/reference-environment.json`
— and proves it executes end to end on a trivial subject. The **baseline** for each budget in `07` is
recorded by the milestone that first produces the artifact that budget measures, and becomes an exit
criterion of that milestone: solver-scale baselines at M2a, render, editor-response and accessibility
baselines at M3, transient throughput, stop latency and retention baselines at M4.

**Why.** A gate that cannot be met has exactly two outcomes, and both are worse than moving it. Either
M0 never exits, or it exits on a fiction — someone records a "200-component render baseline" against a
default Vite page, and from then on the number is a decoration nobody trusts. `07`'s budgets are
release gates, and a release gate that everyone has learned to wave through has already failed.

Installing the harness early is still the right instinct and is kept: the expensive part of an
accessibility or performance harness is wiring it into CI, and a harness added at M3 arrives after the
UI it was supposed to shape. What M0 cannot do is measure something that is not there yet.

**Rejected.**
- *Leave the criteria as written and interpret them loosely at review.* No document changes. Cost: the
  interpretation is unwritten, so it is re-argued at every milestone, and the first person to record a
  placeholder number establishes that placeholders are acceptable.
- *Move the whole item — harness and baseline — to the milestone that needs it.* Honest, and simpler
  than a split. Cost: the harnesses arrive after the code they exist to constrain. Accessibility
  retrofitted onto a finished canvas is the specific failure `R-42` is trying to avoid, and a
  performance harness first run at M3 has no earlier number to compare against.
- *Drop the M0 benchmark item and rely on `07`'s budgets alone.* Cost: `07` states budgets and names no
  point at which anyone measures them, so nothing would be measured until a release that missed them.

**Constrains.** [`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`07-quality-attributes`](07-quality-attributes.md),
[`08-implementation-sequence`](08-implementation-sequence.md),
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md),
[`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md).

---

## D-46 · Core's model types are authoritative; the schema and every other shape are generated from them

**Accepted · 2026-09-01**

Two documents pointed in opposite directions. [`04-engineering-standards`](04-engineering-standards.md)
said shared JSON Schemas *generate* the C# and TypeScript REST/realtime DTOs;
[`26-model-contract`](../20-core-domain/26-model-contract.md) puts `ModelContract` in Core with
hand-written XML documentation, and [`41-api-architecture`](../40-api/41-api-architecture.md)'s
invariant 1 forbids a `Contracts/` type from exposing a Core type — which requires an Api mirror that
nobody had costed.

**The direction is fixed as Core → schema → everything else.**

1. Core's `ModelContract` and the realtime frame types are **hand-written C#** with the unit and
   sign-convention XML documentation `04` requires, and carry no serialization attribute or package
   (`03`).
2. A build step **emits** the JSON Schema from those types. The emitted schema is committed, and CI
   fails when a build's emitted schema differs from the committed one. That check is the anti-drift
   mechanism the M0 criterion was reaching for.
3. The TypeScript DTOs **and** the Api `Contracts/` mirror are generated from the committed schema, so
   the mirror cannot drift from Core and `41`'s invariant 1 survives without hand maintenance.
4. Every schema property carries a `description`, because the generators emit it as the `<summary>` a
   generated public C# member needs to satisfy `TreatWarningsAsErrors` plus `GenerateDocumentationFile`.

**Why.** The deciding constraint is `04`'s invariant 3 — every public dimensioned member states its
unit and sign convention. That documentation is written by a person who understands why a pump reports
its head as a negative pressure drop, and a code generator cannot produce it from a schema that never
contained it. Making the schema authoritative would either lose that documentation or move it into the
schema, where it would be maintained by whoever edits JSON rather than by whoever writes the physics.
Losing it is not cosmetic: `04`'s own worked example exists because, without the sign convention on the
member, half the components get written with the opposite sign and loop closure looks correct in
isolation.

**Rejected.**
- *Schema-first for all three shapes.* One file to change, drift impossible by construction. Cost: as
  above — generated Core types cannot carry the `<value>` unit and sign documentation, and Core would
  need serialization attributes, breaking `03`'s no-serialization-on-Core's-public-surface rule.
- *Core's `ModelContract` is the wire DTO; drop the Api mirror.* Least code by a wide margin. Cost:
  deletes `41`'s invariant 1, and a Core record change then silently reshapes the public API — the
  exact failure `03` introduced the mirror to prevent.
- *Hand-write Core, the Api mirror and the TypeScript DTOs, with tests asserting they agree.* No
  generator to build. Cost: three-way drift, and the drift is silent because each side's tests are
  written against its own shape. Cross-shape agreement tests are the thing people delete when they go
  red on a Friday.
- *Emit the schema but generate only TypeScript, hand-writing the Api mirror.* Smaller generator. Cost:
  the mirror is the shape most likely to drift, since it is the one nobody reads — it exists only to
  satisfy an architecture rule.

**Constrains.** [`04-engineering-standards`](04-engineering-standards.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`41-api-architecture`](../40-api/41-api-architecture.md),
[`42-rest-contract`](../40-api/42-rest-contract.md),
[`43-realtime-contract`](../40-api/43-realtime-contract.md),
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md), and `D-30`.

---

## D-47 · Core's serialization rule constrains Core's own code, not its transitive package graph

**Accepted · 2026-09-01**

`63-ci-and-repo-hygiene`'s architecture table asserts that Core references no ASP.NET, UI, or
serialization package. The M0 spike (P1.1) found that **SharpProp 9.0.0 declares `Newtonsoft.Json`
and `UnitsNet.Serialization.JsonNet` as direct dependencies**, so both reach Core and ship in every
consumer's output. `ExcludeAssets` does not remove them: it severs Core's *compile* surface while
SharpProp's own dependency edge puts the assemblies back in the closure, which is worse than doing
nothing because it looks like a guard.

**The rule is restated as a rule about Core's own code**, and gains the two assertions that make it
checkable:

1. No source file under `src/FluidScript.Core` references a serialization namespace — no
   `Newtonsoft.Json`, no `System.Text.Json`, no `System.Runtime.Serialization`.
2. No public member of Core exposes a serialization type, and Core's own *assembly* references none.
3. A package Core takes deliberately still may not be a serialization, UI, hosting or transport
   package. Only what a third-party dependency drags behind it is exempt, and only while no Core code
   touches it.

**Why.** What the original rule was protecting is that Core must not shape its model contract around a
serializer, and that `D-46`'s hand-written types stay hand-written. Newtonsoft riding along inside the
property backend threatens neither: no Core type can see it under assertion 1, and nothing in the
model contract knows it exists. Stating the rule as "Core's package closure contains no serializer"
made it a claim about SharpProp's packaging decisions, which the project does not control and cannot
enforce — and an unenforceable invariant is the kind that gets quietly reinterpreted the first time it
fails, taking the enforceable part with it.

The cost is real and is accepted with open eyes: roughly 700 KB and a CVE surface in every consumer's
output for an assembly the project never calls.

**Rejected.**
- *Exclude the assets and keep the original wording.* Tried first, and it compiles. Cost: it does not
  work — the assemblies are still in the build output — so the repository would carry an exclusion
  that reads as protection and provides none. Discovered by inspecting `bin/`, which is the only way
  it would ever have been discovered.
- *Isolate SharpProp in its own project so Core's graph is genuinely clean.* Architecturally the
  best answer, and it is what `21`'s package-risk rationale is really asking for: Core would declare
  `ISubstance` and nothing else, and the adapter would be swappable by construction. Cost: a new
  project, an amendment to `03`, and `21`'s substance registry becomes host-injected rather than
  self-composed — real design work for a benefit that assertion 1 already delivers on the axis that
  matters. Worth revisiting if Core ever needs a second property backend, which is the change that
  would make the project earn its keep.
- *Drop SharpProp for a hand-rolled property library.* Cost: `R-07` exists because correlations are
  where a plant model silently goes wrong. Writing IAPWS-95 to 0.1 % is not a package-hygiene task.

**Constrains.** [`03-repository-layout`](03-repository-layout.md),
[`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md),
[`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md), and `R-16`.

---

## D-48 · Interactive latency is budgeted end to end, from keystroke to visible diagnostic

**Accepted · 2026-09-01**

[`07-quality-attributes`](07-quality-attributes.md) gains one normative interactive gate: the time
from the last keystroke of an edit to that edit's diagnostic being on screen. Two values, by script
size — 250 ms p95 for the M1 syntax tour, 400 ms p95 for the 200-declaration reference script.

The debounce and draft-compile figures become **components** of that gate and stop being
independently normative. A component may be missed while the end-to-end gate is met; meeting every
component while missing the end-to-end gate is a failure.

**Why.** The two figures the plan already stated compose to 450 ms p95 — a 300 ms debounce plus a
150 ms compile — and no document stated the sum, which is the only latency a user ever experiences.
Measurement makes the split stark: a loopback request to the running host answers in 0.31 ms median
and 0.44 ms p95, and [`41`](../40-api/41-api-architecture.md)'s own worked example puts a full
compile, size, solve and serialize at 14 ms. Over 95 % of what a user waits for is a timer we chose
rather than work we do, and nothing was gating that.

A budget expressed only in components is also the shape that hides a regression: every component
stays green while the sum drifts, because no test computes the sum.

**Rejected.**
- *Keep component budgets only.* Nothing new to maintain. Cost: this is the state that let 450 ms sit
  unnoticed through six review passes.
- *One number regardless of script size.* Simplest to state and to test. Cost: either unachievable at
  200 declarations or slack at 30 — and a gate that is slack at the size users spend most of their
  time at is not a gate.
- *Budget the round trip instead of the keystroke.* Measurable server-side with no browser harness.
  Cost: it excludes the debounce, which measurement says is the dominant term. It would budget the
  5 % and ignore the 95 %.

**Constrains.** [`07-quality-attributes`](07-quality-attributes.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`26-model-contract`](../20-core-domain/26-model-contract.md),
[`51-frontend-architecture`](../50-frontend/51-frontend-architecture.md),
[`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md).

---

## D-49 · The debounce is a measured value with a typing-cadence floor, not a constant and not a control loop

**Accepted · 2026-09-01**

The idle debounce stops being a constant chosen in advance. It is set from measurement on the
reference environment, recorded as a baseline like every other budget, and bounded at both ends:

- **Floor — typing cadence, not server load.** It may not be short enough to fire inside an average
  typist's inter-key interval, about 200 ms at 40 wpm. Below that it is per-keystroke behaviour under
  another name, and it reports half-typed tokens: *a heat_exchanger has no 'pow'* while the user is
  still writing `power`.
- **Ceiling — whatever `D-48` leaves** after the measured compile time for that script size.

It is set once per environment and recorded. It does **not** adapt at runtime.

**Why.** [`51`](../50-frontend/51-frontend-architecture.md) already said the value should be measured,
and scheduled nothing, so the default would have shipped by default. The two usual objections to
shortening it do not apply here: server load is handled, because
[`41`](../40-api/41-api-architecture.md) cancels a superseded compile within one solver iteration,
and squiggle flicker is handled, because [`52`](../50-frontend/52-editor.md) keeps diagnostics across
the debounce gap. What actually bounds the debounce is neither — it is that someone pausing mid-word
should not be told their unfinished token is wrong.

**Runtime adaptation is rejected rather than deferred**, and it was the first design proposed here.
Latency that varies with recent load is worse than latency that is consistently longer: a user
calibrates to a rhythm, and a rhythm that moves reads as the tool being erratic rather than as the
tool being fast. It also makes a report unreproducible, because the reporter cannot say what the
debounce was when they saw the problem.

**This does not change `R-21`**, whose substance is that the diagram updates on an idle debounce
rather than on every keystroke, and whose figure is written "~300 ms". A measured value that departs
from 300 ms by more than a factor of two is a requirement conversation, not a benchmark result.

**It does not change `D-06` either.** The draft path stays request/response; a shorter idle gap makes
it no less cacheable, curl-able or testable.

**Rejected.**
- *Keep 300 ms fixed.* No work, and defensible right up until it is measured. Cost: `41`'s worked
  example already shows a 14 ms compile, so it spends 95 % of the user's wait on a number nobody has
  justified.
- *Adapt at runtime from the rolling compile p95.* Tracks the machine it actually runs on. Cost:
  unpredictable latency and unreproducible bug reports, as above.
- *Drop the debounce and rely on supersession.* Lowest possible latency, and the load argument no
  longer forbids it. Cost: diagnostics about half-typed tokens, and it contradicts `R-21`.

**Constrains.** [`51-frontend-architecture`](../50-frontend/51-frontend-architecture.md),
[`52-editor`](../50-frontend/52-editor.md),
[`05-milestones-and-acceptance`](05-milestones-and-acceptance.md),
[`07-quality-attributes`](07-quality-attributes.md),
[`15-semantic-model`](../10-language/15-semantic-model.md),
[`31-solver-architecture`](../30-solver/31-solver-architecture.md),
[`32-steady-state-newton`](../30-solver/32-steady-state-newton.md),
[`41-api-architecture`](../40-api/41-api-architecture.md).

---

## D-50 · One spelling, one meaning: `Head` is bare-only and `-` is not a unit symbol

**Accepted · 2026-09-01**

Two corrections to [`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md)'s unit
symbol table, found by checking the table against itself and against
[`12-grammar`](../10-language/12-grammar.md) before implementing it.

**`Head` takes no unit symbol.** It joins `Kv` as bare-only; `mH2O` belongs to `Pressure` and
`PressureDelta` alone. The table as written gave `mH2O` to all three, and `13`'s own acceptance
criterion — no symbol maps to two dimensions outside the documented pressure/delta pair — failed
against it.

**`-` is removed from `Dimensionless`, which keeps `%`.**

**Why.** Head and pressure are not the same quantity. Head is metres of the *pumped fluid*; `mH2O` is
metres of *water column*, a pressure of 9806.65 Pa per metre. They coincide only for water, so a
circuit carrying glycol would read `head=15 mH2O` as a value that is wrong by the density ratio and
plausible on the diagram. A bare number in `head=` already means metres of the pumped fluid, from the
parameter's declared dimension (`D-07`), so nothing is lost by refusing the explicit spelling — and
`13`'s criterion already anticipates a dimension "explicitly marked as bare-only", which is what `Kv`
is.

`-` is worse, because it breaks parsing rather than arithmetic. `13`'s rule is that a known unit
symbol is recognised when it follows a number token, optionally separated by whitespace, and is not
followed by `=`. In `let x = 5 - 3` the `-` follows a number and is not followed by `=`, so it lexes
as the dimensionless unit and `3` is stranded. `13`'s own quoted line `let Tret = Tflow - dTdesign`
survives only because `Tflow` is an identifier rather than a number. Separating the two cases needs
lookahead past the following token, which `12`'s invariant 5 forbids. A bare number is already
dimensionless, so the symbol carried no information in exchange.

**Rejected.**
- *Give `Head` its own spelling (`mfluid`, `mhead`).* Keeps an explicit unit writable. Cost: a symbol
  no engineer writes and no other tool recognises, invented to fill a hole nobody had fallen into.
- *Resolve `mH2O` by the target parameter, as `Pressure`/`PressureDelta` already are.* Consistent with
  an exception that exists. Cost: much larger than it looks — those two share an SI base unit and
  differ only in affine meaning, whereas Head and Pressure have different base units, so a quantity
  would have no `SiValue` until its destination were known. That breaks "parse converts to SI once"
  and makes `Quantity` non-self-describing.
- *Keep `-` with a lexer special case, or only when unspaced.* Preserves the engineering `[-]`
  convention. Cost: neither works. `5-3` stays ambiguous with subtraction under both, so the collision
  becomes rarer and harder to diagnose rather than resolved.

**Constrains.** [`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md),
[`12-grammar`](../10-language/12-grammar.md).

## D-51 · Two lexical collisions: `%` is a unit and not an operator, and `.` joins a number only before a digit

**Accepted · 2026-09-01**

Two collisions in [`12-grammar`](../10-language/12-grammar.md)'s lexical rules, found by checking that
document against [`14-expressions-and-references`](../10-language/14-expressions-and-references.md)
and against itself before implementing the lexer. Both are the failure `D-50` names for `-`: one
spelling with two readings, separable only by lookahead that invariant 5 forbids.

**`%` is removed from the operator set.** It stays a unit symbol for `Dimensionless`, as `D-50` left
it, and `14`'s `multiplicative` production loses it: `multiplicative = unary , { ("*" | "/") , unary }`.
There is no modulo operator in the language.

**A `.` is part of a number only when a digit follows it.** `number` no longer admits a trailing dot,
so `30.` lexes as the number `30` followed by a `.` token, and `30..60` lexes as `30`, `..`, `60`.

**Why.** `%` is `D-50`'s `-` with the sides reversed. `13`'s rule is that a known unit symbol is
recognised when it follows a number token, optionally separated by whitespace, and is not followed by
`=`; in `let x = 10 % 3` the `%` follows a number and is not followed by `=`, so it lexes as the
dimensionless unit and `3` is stranded. `D-50` resolved the identical case for `-` by dropping the
unit, because a bare number is already dimensionless and the symbol carried no information. `%` is
not that: it carries a factor of 0.01, and `position=50 %` and `efficiency=72 %` are how engineers
write the two dimensionless parameters that occur most. The operator is the side with nothing behind
it — a declarative language with no loops, no indices and no branching (`D-01`) has no use for a
remainder, no example in `plan/` or `/docs` uses one, and the closed function set in `14` does not
include one either.

The dot is a contradiction inside one document rather than between two. `12`'s `number` production
ends `[ "." , { digit } ]`, which admits `30.`, while its `range` production is `expression , ".." ,
expression`. Maximal munch resolves `30..60` in the number's favour: `30.` then `.` then `60`, one dot
where the range needs two, and the schedule line does not parse. Every range in the specification is
written with spaces around the `..`, which is why the contradiction survived six review passes — but
`over 30..60 HE1.power = 45` is what a user types, and the printer must round-trip whatever they
typed. Requiring a digit costs a literal nobody writes.

**Rejected.**
- *Keep both `%` readings and let the parser report the stranded number.* Costs nothing to implement:
  `10 % 3` becomes two tokens the parser cannot join, so it is `FS1104` rather than a wrong number.
  Cost: the diagnostic names neither `%` nor the unit, and points at a line that is valid arithmetic
  in every language the user knows. A confusing error is cheaper than a wrong answer and dearer than
  no error, and the next reader re-derives this entire analysis from scratch.
- *Drop the `%` unit and keep modulo, the way `D-50` went for `-`.* Symmetric with the precedent.
  Cost: it trades the one dimensionless spelling engineers actually write for an operator no script
  in this repository, this plan, or this specification's own examples uses.
- *Recognise `%` as a unit only when unspaced (`50%`) and as an operator when spaced.* Preserves both.
  Cost: `D-50` rejected exactly this for `-` and the reason holds here — whitespace is trivia
  everywhere else in the language, so making it significant in one position means a script's meaning
  changes when a formatter touches it, and `50 %` is the spelling `13`'s own examples use.
- *Let the number production keep its trailing dot and require spaces around `..`.* Keeps the EBNF as
  written. Cost: it makes whitespace significant to disambiguate two productions, the same trade
  rejected above, and it turns a typo into a silent reinterpretation rather than an error.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md),
[`14-expressions-and-references`](../10-language/14-expressions-and-references.md),
[`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md).

## D-52 · Sections are scoped to a circuit, not to the file

**Accepted · 2026-09-01**

A `circuit` header is legal anywhere a statement is legal. It ends whatever section the previous
circuit was in and starts the new circuit's declaration section, so `connections` and `schedule` are
**at most one each per circuit** rather than one each per file
([`12-grammar`](../10-language/12-grammar.md)'s invariant 4, restated).

**Why.** The distribution-header reference circuit in
[`01-vision-and-scope`](01-vision-and-scope.md) does not parse without it, and that circuit is
binding under `D-11`. It writes `heating`'s declarations, then `connections`, then its topology, then
`circuit AHU 101` and `circuit radiators 102` with their own declarations and attachments — which
`12`'s section table refused, on the reasoning that a circuit header below `connections` "would begin
a new circuit inside another circuit's topology, which has no reading at all". It has exactly one
reading, and it is the one `12` states two sections earlier: a circuit header begins a circuit, and
every declaration and connection that follows belongs to it until the next header. The header is a
section marker in effect, so it ends a section as well as beginning one. The table encoded the file
as the unit of sectioning; the prose encoded the circuit. The prose was right.

The same defect was found and fixed once before, in `D-13`'s pass: `12`'s acceptance criteria record
that "both reference circuits previously failed to parse under this document while every acceptance
criterion here passed", which is why a corpus test over the specification's own examples is an
acceptance criterion. That test does not exist yet — it lands with the parser — and this is the
second instance it would have caught.

Nothing else moves. `FS1101` still fires on a second `connections` or `schedule` within **one**
circuit; `FS1103` still fires on a directive or `let` below `connections` in the same circuit;
`project` and `spacing` still precede the first `circuit` header, which is what makes them file-wide
(`D-37`). One token of lookahead still classifies every statement, because `circuit` is a reserved
word and falls in the disambiguation rule's first clause.

**Rejected.**
- *Rewrite the distribution header to put all three circuit blocks before one `connections` section.*
  Keeps `12`'s table. Cost: it separates each subcircuit's `supply`/`return` from the header nodes
  they attach to, turns three readable blocks into one declaration wall followed by one topology
  wall, and contradicts `D-33`'s design, in which a circuit is a contiguous block a reader can scan.
  It also does not generalise: a two-circuit file where both circuits have connections cannot be
  written at all under one file-wide connection section.
- *Give each circuit its own explicit `end`, or brace its body.* Makes the scoping unambiguous
  without touching the section rules. Cost: `12`'s shape-of-the-language section forbids exactly this
  — no braces, no `end` — and it costs a line per circuit to state what the next header already says.
- *Keep one `connections` per file and attribute each connection to a circuit by the components it
  names.* Requires no grammar change. Cost: attribution becomes a binder inference that can fail or
  straddle two circuits, and a connection naming components of two circuits would be silently
  assigned to one of them. Attachment (`supply`/`return`) exists precisely so that cross-circuit
  topology is written rather than inferred.

**Constrains.** [`12-grammar`](../10-language/12-grammar.md).

---

## Adding an entry

1. Append with the next `D-` number. Never renumber, never delete — supersede.
2. State the rejected alternatives and what each would have cost. This is the part reread in a year.
3. Add the `D-` id to the `constrains` list of the documents it binds, and cite it there.
4. If it supersedes an entry, edit the old entry's status line to `superseded by D-xx` and leave its
   body intact.

## Invariants

1. `D-` numbers are never reused or renumbered.
2. An entry's body is never edited after acceptance; only its status line changes.
3. Every entry names at least one rejected alternative and what it would have cost.
4. Every document constrained by a decision cites it by id.

## Acceptance criteria

- [ ] Every `D-` entry names at least one rejected alternative with its cost.
- [ ] Every document constrained by a decision cites it by id.
- [ ] No `D-` entry has been edited except to mark it superseded.

## Open questions

None. This document records decisions that are settled; an unsettled question belongs in the Open
questions section of the document it blocks, not here.
