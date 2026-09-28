---
id: 11-language-overview
title: Language overview and design principles
tier: 10-language
status: reviewed
owns: [language design principles, inference rules, compilation pipeline stages]
depends_on: [01-vision-and-scope, 02-glossary, 06-decision-log]
traces_to: [R-01, R-02, R-03, R-05, R-06, R-46, R-49]
open_questions: 0
last_review_pass: 6
---

# Language overview and design principles

## Purpose

Sets the rules the rest of tier 10 is judged against. When a grammar question has two reasonable
answers — and it always does — this document is what breaks the tie. Without it, "as easy as possible"
(`R-01`) is a preference rather than a criterion, and the language accretes convenience features until
it is Python with different punctuation.

[`19`](19-fluidscript-2.md) records how they decided the language's statements — the file's shape, how
ports and cases are written, the controller (`D-165`–`D-171`).

## Responsibilities

**Owns.** The design principles, the inference rules the language applies on the user's behalf, and
the stage boundaries of the compilation pipeline.

**Explicitly does not own.** The concrete syntax ([`12-grammar`](12-grammar.md)), units
([`13-type-and-unit-system`](13-type-and-unit-system.md)), evaluation
([`14-expressions-and-references`](14-expressions-and-references.md)), what components mean
([`22-component-model`](../20-core-domain/22-component-model.md)).

## Principles

### P1 — The example is the benchmark

Any proposed syntax change is measured against the brief's original script. If it makes that script
longer, it needs an argument stronger than "it is more consistent". Consistency that costs density is
the wrong trade for this language; a general-purpose language makes the opposite trade correctly.

### P2 — Absence is meaningful

A missing parameter is a request for the kind registry to resolve: normally by sizing, or through a
binding visible default such as `D-32`'s tank values (`D-02`). This means the AST must distinguish
*absent* from *present with a value equal to the resolved default*, all the way through the binder,
because `pump` and `pump head=15` where 15 happens to be the sized answer must behave differently: the
second one is a constraint that the circuit must satisfy or report.

### P3 — Infer only what is unambiguous

The language inserts nodes, terminates open ports, and orders flow (`R-06`). Bare connections remain
ideal zero-loss topology links under `D-25`. Each inference has exactly
one defensible answer given the connection list. Inference stops at the point where a second answer
becomes defensible — the language never guesses a *value*, only a *structure*. Guessing structure that
turns out wrong produces a diagram the user can see and correct; guessing a value produces a number
they will trust.

### P4 — Errors are ordinary

A script under active editing is malformed most of the time. Parse failure is a normal state, not an
exception: the parser recovers, the binder does as much as it can, and the renderer keeps the last good
picture (`R-05`). Nothing in the pipeline may throw on bad input — malformed input is a return value.

### P5 — Text is the source of truth

The canvas is a view. Every canvas edit becomes a text edit (`R-25`), never a parallel model that the
text is regenerated from. This is why the printer must be lossless
([`17-formatting-and-round-trip`](17-formatting-and-round-trip.md)) and why the AST carries trivia.

### P6 — One way to say each thing

No synonyms, no optional punctuation with identical meaning, no two spellings of a keyword. Every
alternative form is another thing for `/docs` to explain (`R-28`), another branch in the printer, and
another way for two scripts that mean the same thing to look different in a diff.

### P7 — Readable by an agent

`R-29` is a language requirement, not only a documentation one. Diagnostics carry stable codes,
keywords are words rather than symbols, and structure is explicit enough that a generator does not
have to model whitespace. A language that is easy for a person to skim is usually easy for an agent to
emit; where they conflict, the person wins, and `/docs` closes the gap.

## How the principles chose the statements

The statements below are listed here because the principles above were the argument for each of their
shapes, and reading them together is the clearest demonstration of what those principles actually decide:

| Statement | Shape | Which principle chose it |
|---|---|---|
| `circuit "AHU":` with `number = 101`, `role = ahu` | A block with a quoted title; the number and the role are settings | **P1** — a single-circuit script writes no number and no role. **P6** — the title names the circuit and is never a reference, and the role is a setting of its own rather than a second reading of the name (`D-33`, `D-35`, `D-165`) |
| `N3 - PU_AHU - HE_AHU` in another circuit | An ordinary link naming the parent's node | **P3** — where a circuit joins its parent is structure the language refuses to guess, because a wrong guess still solves (`D-166`) |
| `TV1 - SP - NS` | A chain read in the direction of flow; ports taken from the inflows and outflows | **P3** — a count of inflows and outflows settles a three-way valve's function with no guess, and what it cannot settle is `FS1804`, never a guess (`D-166`) |
| `spacing = 20` in the project block | A project setting, in world units | **P5** — presentation lives in the text with everything else, rather than in a side file (`D-37`, `D-171`) |
| `TC1 controller:` with `moves`, `reads`, `setpoint` | One declaration, every field a named setting, each a qualified reference or a component (`D-43`, `D-61`) | **P7** — a positional form is shorter for a person and strictly worse for an agent, which must get the order right without seeing an example |

**Joining circuits is where P3 did the most work.** A circuit joins its parent where its links name the
parent's nodes; there is no attachment statement (`D-166`). The rejected alternative — inferring the
joining point from proximity or declaration order — fails P3: the joining point sets the flow split, so
a wrong inference yields a model that is well-posed, solvable, and wrong. Structure the language cannot
determine unambiguously is structure the user writes.

**The controller's named settings are the one place P1 lost.** A positional form — actuator, measurement,
setpoint in a fixed order — is shorter, and P1 says brevity wins ties. This was not a tie: the positional
form has no memorable order, and transposing two arguments produces a controller that drives the wrong
way while binding cleanly (`D-40`, `D-168`). P1 asks for an argument stronger than consistency, and "the
short form silently reverses the control loop" is one.

## The inference rules

These are `R-06` made precise. Each states its trigger, its result, and — critically — when it does
**not** fire.

| # | Rule | Fires when | Result | Does not fire when |
|---|---|---|---|---|
| I1 | **Undeclared node** | An identifier appears only in connection lines, never in a declaration | A `node` component with that name is created | The identifier matches a declared component of any other kind |
| I2 | **Implicit intermediate node** | Two non-node components are connected directly (`N2 - HE1` is fine; `HE1 - 3WV` is not) | A node is inserted between them, named `<A>__<B>` | Either side is already a node |
| I3 | **Open-port termination** | A component's declared port has no connection | A boundary node is attached, carrying the circuit's default boundary condition | The port is optional for that component kind (e.g. a three-way valve used as two-way) |
| I4 | **Flow direction** | A connection `A - B` is written | Nominal flow is A → B; it seeds the solver's sign convention and the arrow drawn on the canvas | Never — but a solved negative flow is legal and is drawn reversed, with an info diagnostic |
| I5 | **Single-circuit membership** | A component is declared in a file with exactly one `circuit` header | It belongs to that circuit | The file declares more than one circuit (M6) |
| I6 | **Chained connections** | `A - B - C` | Two connections, `A - B` and `B - C` | Never |
| I7 | **Implicit pipe** (`D-110`, `D-166`) | A one-link connection line ends in pipe properties: `N5 - N1  25 m  DN25` | The link becomes a `pipe` named `<A>__<B>` carrying those properties, with I2's nodes beside it named `<A>__<B>__in` / `__out`; `length` unwritten is zero | The line carries no properties: it stays a lossless link. On a longer chain the properties are `FS1803` |
| I8 | **Implicit sensor** (`D-151`) | A controller reads a node's `t`, `p` or `flow` directly: `reads = NS.t` | A sensor of that kind named `<Node>__TE` / `__PE` / `__FE` is placed `at` the node, and the controller reads through it | A sensor of that kind already stands on the node, or the node is a junction (`FS1548`, `D-150`) |
| I9 | **Port point on a junction** (`D-183`) | A component's port is wired to a node with three or more connections: `N2 - PU1`, where N2 also meets N1 and the valve | A node named `<Component>__<Port>` goes between the port and the junction, joined to it by a zero-length link, so the port has its own state and the junction holds only the mix | The component is a node or a pipe, or the node is not a junction |

**I2's naming matters.** `HE1__3WV` is derived, stable, and visible in hover and diagnostics. It is
also a legal identifier the user can reference, which lets them promote an inferred node to a declared
one by writing it down — the only migration path that does not require them to guess a name.

**I3 has a sharp edge worth naming now.** A three-way valve has three ports; wired on two of them, it
leaves I3 to fire on the third. What that boundary node *is*
(a dead leg? an open supply?) is a physics question, not a language one, and it is deferred to
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md). The language's obligation is to
create it and say so.

## What the language deliberately cannot do

Restating the non-goals as language rules, because each is a thing someone will try to add:

- **No loops.** Four identical heat exchangers are four lines. If that becomes common, the answer is a
  subsystem component (M6), not iteration.
- **No conditionals.** A model has one shape. Scenario comparison is a tooling feature — run the
  script twice with different inputs — not a language feature.
- **No user-defined functions or macros.** A named expression is a `let`. Anything more is a program.
- **No imports or includes in v1.** One file is one model. Composition is M6 and gets its own
  design.
- **No mutation.** A `let` binds once. There is no assignment, so there is no evaluation order to
  reason about beyond the dependency graph.

## The pipeline

```
  source text (.fluid)
        │
        ▼  Lexer            → tokens + trivia                    (12-grammar)
        ▼  Parser           → syntax tree, error-recovering      (12-grammar, 16-diagnostics)
        ▼  Binder           → semantic model: symbols, resolved  (15-semantic-model)
        │                     kinds, typed parameter values
        ▼  Evaluator        → constant-folded quantities,        (13, 14)
        │                     dependency graph resolved
        ▼  Lowering         → domain graph: components, ports,   (23-topology-and-graph)
        │                     connections, inference applied
        ▼  Sizing           → unspecified parameters resolved    (24-auto-sizing)
        ▼  Solve            → steady state or transient          (tier 30)
        │
        └──► Model contract → API, canvas, exporters             (26-model-contract)
```

**Every stage is total.** Each takes whatever the previous stage produced, however damaged, and
returns a result plus diagnostics. No stage throws on user input (P4). A stage that cannot produce its
output for a given element produces it for the others and marks that one unresolved.

**The stage boundary that matters most is Binder → Lowering.** Above it, everything is about text and
names and has no physics. Below it, everything is about a graph and has no idea a script existed. That
boundary is what lets the language be tested without CoolProp and the physics without a parser.

## Worked example

The brief's script, annotated with which stage produces what:

| Source | Lexer | Parser | Binder | Lowering |
|---|---|---|---|---|
| `circuit "coolingLoop":` | `ident(circuit)`, `string("coolingLoop")`, `colon` | `CircuitHeadSyntax`, the head of a `BlockSyntax` | circuit symbol `coolingLoop` | `Circuit` |
| `fluid = water` | `ident(fluid)`, `equals`, `ident(water)` | `SettingLineSyntax` in the circuit's block | substance `Water` | circuit's `Substance` |
| `HE1 heat_exchanger power = 30 …` | `ident(HE1)`, `ident(heat_exchanger)`, … | `ComponentDeclarationSyntax` | kind `HeatExchanger`, `Power = 30 kW → 30000 W` | `HeatExchangerComponent` with 2 ports |
| `3WV three_way_valve` | `ident(3WV)`, `ident(three_way_valve)` | `ComponentDeclarationSyntax`, 0 params | kind `ThreeWayValve`, all params **absent** | `ThreeWayValveComponent`, 3 ports, all resolved by registry policy |
| `PU1 pump` | `ident(PU1)`, `ident(pump)` | `ComponentDeclarationSyntax`, 0 params | kind `Pump`, in no connection (`FS1507`) | **I3** terminates both its ports |
| `N1 - N2` | `ident(N1)`, `minus`, `ident(N2)` | `ConnectionSyntax` | two unresolved symbols | **I1** creates both as `Node` |
| `HE1 - 3WV.ab` | … | `ConnectionSyntax` | both resolve to declared components | **I2** inserts node `HE1__3WV` |
| `3WV.a - N3` | … | `ConnectionSyntax` | `N3` unresolved | **I1** creates `N3` |

Counting the result: the user wrote 3 components and 5 connections. The graph contains 3 declared
components, 3 inferred nodes from I1 (`N1`, `N2`, `N3`), 1 inferred node from I2, and 2 boundary
nodes from I3 on `PU1`'s ports — nine components, as [`01`](../00-foundation/01-vision-and-scope.md)'s
diagnostic table counts them. That ratio is the point of the language, and it is also the
reason hover (`R-23`) must show inferred names: the user must be able to see what was created for them.

## Invariants

1. Every inference rule fires on its stated trigger and on nothing else.
2. Absence of a parameter is representable and distinct from any default, through every stage (P2).
3. No pipeline stage throws on any input (P4).
4. The language never infers a *value*, only a structure (P3).
5. Every canvas-originated change reaches the model as a text edit, never as a parallel model (P5).
6. There is exactly one syntactic form for each thing the language can express (P6).
7. Every line is classifiable from its first token and, when that is a name, the qualified name it
   starts and the token after it — never from the lines around it (`19` §Lines, blocks and names). A
   proposed statement that needs more is rejected, not accommodated — this is what makes P4's
   line-granular recovery possible.

## Acceptance criteria

- [ ] Every inference rule I1–I9 has a test whose name states both the firing and the non-firing case.
- [ ] No pipeline stage's public API can throw on any byte sequence — verified by a fuzz test over
      the sample corpus with random mutations.
- [ ] The brief's example produces the component count derived above, and each inferred component is
      attributable to a named rule.
- [ ] Every principle P1–P7 is cited by at least one other document in tier 10.

## Open questions

None. I2 identity is `{orderedLeft}__{orderedRight}`; repeated endpoint pairs append a one-based ordinal
derived from their relative source order. Unrelated statements do not rename existing inferred nodes.
A rename operation supplies the old/new mapping to consumers (`D-30`).
