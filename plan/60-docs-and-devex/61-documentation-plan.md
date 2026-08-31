---
id: 61-documentation-plan
title: Documentation plan
tier: 60-docs-and-devex
status: draft
owns: [the /docs tree, page templates, the documentation gate, LLM-readability conventions, generated pages]
depends_on: [01-vision-and-scope, 22-component-model]
traces_to: [R-28, R-29, R-30, R-44, R-45, R-46, R-47, R-48, R-49, R-50]
open_questions: 0
last_review_pass: 0
---

# Documentation plan

## Purpose

`R-28` is absolute: every functionality has a page in `/docs`, no exceptions. `R-29` says the audience
includes LLM agents that must be able to author a valid circuit from the documentation alone. Those two
together mean documentation is not a write-up after the fact — it is a deliverable with a structure, a
template, and a gate, and this document owns all three.

## Responsibilities

**Owns.** The `/docs` tree, page templates, the documentation gate, LLM-readability conventions, and
which pages are generated rather than written.

**Explicitly does not own.** The plan tree (`plan/` is internal; `/docs` is user-facing), the content
of any individual page, CI enforcement
([`63-ci-and-repo-hygiene`](63-ci-and-repo-hygiene.md)).

## `plan/` versus `/docs/`

Confusing them is the most likely failure of this document, so:

| | `plan/` | `/docs/` |
|---|---|---|
| Audience | Implementers and reviewers | Users and agents |
| Answers | How should this be built, and why | How do I use this |
| Contains | Signatures, invariants, decisions, open questions | Examples, tutorials, reference |
| Lifespan | Until implemented, then a historical record | Forever, kept current |
| Tone | Argumentative — states trade-offs | Instructional — states what to do |

A `/docs` page never mentions an open question, a rejected alternative, or a C# type. A `plan/`
document never explains how to write a circuit.

## Tree

```
docs/
├── README.md                    entry point; what FluidScript is, where to start
│
├── tutorial/                    linear, in order, each building on the last
│   ├── 01-first-circuit.md      a heat exchanger and two nodes, rendered
│   ├── 02-connections.md        the connections section, inferred nodes
│   ├── 03-auto-sizing.md        why `pump` with no parameters works
│   ├── 04-overriding.md         stating a value as a constraint
│   ├── 05-units.md              bare numbers, explicit units, temperature vs difference
│   ├── 06-expressions.md        let, arithmetic, referring to other components
│   ├── 07-reading-results.md    hover, the log, the colour scale
│   └── 08-going-dynamic.md      fluid dynamic, disturbances, a controller
│
├── advanced/                    non-linear; each solves one real problem
│   ├── mixing-circuits.md       three-way valves, bypass, authority
│   ├── circuits-and-tags.md     numbering, subcircuits, how equipment tags are derived
│   ├── discretized-pipes.md     nodes=, transport delay, the accuracy trade
│   ├── control-loops.md         PI tuning, dead time, why it oscillates
│   ├── stratified-storage.md    mixed layers, port elevations, source/load ordering
│   ├── humid-air.md             psychrometrics, the dry-air enthalpy basis
│   ├── sizing-strategy.md       what auto-sizing decides and what it does not
│   ├── reading-diagnostics.md   every warning class and what to do about it
│   ├── troubleshooting.md       "it will not converge", "the sizes look wrong"
│   ├── plant-layout.md          headers, how the diagram is arranged, spacing
│   └── working-in-tabs.md       several documents, what keeps running when you switch
│
├── functions/                   reference. one page per thing. exhaustive.
│   ├── index.md                 the complete list — the agent's entry point
│   ├── circuit.md               ─┐
│   ├── project.md                │ directives
│   ├── spacing.md                │
│   ├── fluid.md                  │
│   ├── style.md                  │
│   ├── show.md                   │
│   ├── let.md                   ─┘
│   ├── supply-return.md         ─┐ statements
│   ├── control.md               ─┘
│   ├── node.md                  ─┐
│   ├── pipe.md                   │ components
│   ├── heat-exchanger.md         │
│   ├── valve.md                  │
│   ├── three-way-valve.md        │
│   ├── pump.md                   │
│   ├── tank.md                   │
│   ├── controller.md            ─┘
│   ├── tags.md                  [generated] every kind's tag code and an example tag
│   ├── units.md                 [generated] every symbol, dimension, canonical unit
│   ├── properties.md            [generated] every referenceable property
│   ├── diagnostics.md           [generated] every FSxxxx code
│   └── catalogs.md              [generated] shipped dimension tables + provenance
│
└── assets/                      diagrams and screenshots
```

## The documentation gate

**A milestone does not exit until every user-visible feature it added has its page**
([`05-milestones-and-acceptance`](../00-foundation/05-milestones-and-acceptance.md)). Not a separate
documentation milestone — those never happen.

Concretely, for a new component, the nine-file list in
[`03-repository-layout`](../00-foundation/03-repository-layout.md) ends with
`docs/functions/<kind>.md`, and that row is a gate, not a suggestion. **CI checks it**
([`63-ci-and-repo-hygiene`](63-ci-and-repo-hygiene.md)): every registered component kind, directive,
and diagnostic code must have a page or a generated entry, and the build fails otherwise.

This is the only mechanism that makes `R-28` real. A convention that documentation is required produces
documentation for the first three features.

### The gate covers statements and directives, not only components

`D-33`, `D-37` and `D-40` added five statements — `project`, `spacing`, `supply`/`return` and
`control` — and none of them is a component kind. The CI check must therefore enumerate the **reserved
word list** as well as the component registry, or the gate passes on a language feature with no page
at all. That is precisely how a documentation rule decays: it keeps working for the case it was
written against and silently stops covering everything else.

Two of the new pages are gates on *derived* behaviour rather than on syntax:

- **`functions/tags.md` is generated from the registry** (`D-34`), like `units.md` and
  `diagnostics.md`. A tag code is data, so a hand-written page would drift the first time a kind was
  added. The generated page lists every kind's code and an example tag, and it is what a reader
  matches against a drawing.
- **`advanced/circuits-and-tags.md` must explain declaration order**, because that is the part users
  will find surprising: inserting a pump renumbers every pump below it. The page states the rule, says
  why topological numbering was rejected, and points at the explicit *Apply tags* command for anyone
  who wants tags written into their script.

## The function-page template

Every page in `functions/` has the same sections in the same order. Uniformity is the point: an agent
that has parsed one page can parse all of them, and a human who has read one knows where to look.

```markdown
# three_way_valve

One sentence: what it is and when to use it.

## Syntax
    <name> three_way_valve [parameter=value ...]
List curated input aliases under the canonical spelling; aliases never become page titles (`D-15`).

## Ports
| Port | Role | Required | Meaning |

## Parameters
| Parameter | Dimension | Unit | Range | Default | Meaning |
Every parameter is optional. Omitting one normally means "size it"; list any explicit component
defaults and their basis. For `tank`, document 300 dm3, five layers, and 0.5 port elevation (`D-32`).

## Properties
Readable as `<name>.<property>` in an expression.
| Property | Dimension | Unit | Available | Meaning |

## Sizing
What is decided for you when you omit a parameter, and on what basis.

## Examples
### Minimal
### With a stated Kv
### As a two-way valve
Each: the script, what it produces, and the resulting numbers.

## Diagnostics
| Code | When |

## See also
```

**"Available" in the Properties table** says whether a property is known immediately, after sizing, or
only after a solve — which is exactly what an author of an expression needs and what
[`14-expressions-and-references`](../10-language/14-expressions-and-references.md)'s `FS1407` exists to
report.

## LLM readability

`R-29` is a design constraint on the documentation, not a hope about it. What it demands:

| Convention | Reason |
|---|---|
| **One page per concept**, named after the script keyword | An agent looking for `three_way_valve` finds `three-way-valve.md` by construction |
| **`functions/index.md` is a complete list** with one-line descriptions | The entry point: read one file, know the whole surface |
| **Every page has the same sections in the same order** | Parseable without heuristics |
| **Every example is complete and runnable** — never a fragment | A fragment cannot be validated, and an agent will emit it verbatim |
| **Every example is tested** (see below) | An example that does not run teaches an agent to write broken scripts |
| **Tables, not prose, for anything enumerable** | Parameters, ranges, codes |
| **Diagnostic codes are stable and cross-referenced** | An agent that hits `FS1503` reads what to do |
| **No screenshots as the only source of information** | A screenshot is opaque to a text consumer |
| **State units explicitly, everywhere** | The single most likely thing for a generator to get wrong |

**Every code block in `/docs` that is marked `fluidscript` is extracted and compiled by the test
suite.** Blocks marked as intentionally-broken are annotated and asserted to produce the stated
diagnostic. This is the mechanism that makes the documentation trustworthy for both audiences, and it
turns `/docs` into a second test corpus at almost no cost.

## Generated pages

Four pages are generated from the same data the implementation reads, because a hand-maintained copy
diverges:

| Page | Source |
|---|---|
| `units.md` | The unit table ([`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md)) |
| `properties.md` | The component registry ([`22-component-model`](../20-core-domain/22-component-model.md)) |
| `diagnostics.md` | `DiagnosticRegistry` ([`16-diagnostics`](../10-language/16-diagnostics.md)) |
| `catalogs.md` | The catalogue files, with provenance ([`27-component-catalog`](../20-core-domain/27-component-catalog.md)) |

Generated at build time, committed, and checked in CI: a generated page that differs from what the code
would produce fails the build. That check is what stops the documentation drifting silently.

**The parameter tables in the hand-written component pages are also generated** — inserted between
markers — for the same reason. The prose around them is written; the table is not.

## Tutorial rules

1. **Each page ends with a working script and a picture of what it produces.**
2. **Each page introduces at most two new concepts.**
3. **No forward references.** A tutorial page may only use what earlier pages introduced.
4. **Every number shown is real**, produced by running the script — not invented for the prose.
5. **The first page reaches a rendered diagram in under ten lines of script.** `R-30`'s promise.

Rule 4 matters more than it sounds: a tutorial with plausible-but-wrong numbers is the fastest way to
lose an engineer's trust, and it is the default outcome when the prose is written before the feature
works.

## Invariants

1. Every registered component kind, directive, and diagnostic code has a page or generated entry.
2. Every `fluidscript` code block in `/docs` compiles, or is annotated with the diagnostic it produces.
3. Generated pages match what the code would generate.
4. Every function page has every template section, in order.
5. No `/docs` page references a `plan/` document, a C# type, or an open question.
6. No tutorial page uses a concept introduced later.
7. Every dimensioned value in `/docs` states its unit.

## Error cases

Failures are build failures, not runtime ones:

| Check | Failure |
|---|---|
| Component kind with no page | Build fails, naming the kind |
| Code block that does not compile | Build fails, with the diagnostic |
| Generated page out of date | Build fails, showing the diff |
| Function page missing a section | Build fails, naming the section |
| Forward reference in the tutorial | Build fails, naming the concept and both pages |

## Worked example

`docs/functions/pump.md`, abbreviated:

```markdown
# pump

Adds pressure to a circuit. A pump with no parameters is sized from the loop it sits in.

## Syntax
    <name> pump [head=<length>] [flow=<mass flow>] [speed=<ratio>] [efficiency=<ratio>]

## Ports
| Port | Role   | Required | Meaning        |
|------|--------|----------|----------------|
| in   | inlet  | yes      | Suction side   |
| out  | outlet | yes      | Discharge side |

## Parameters
<!-- generated:parameters -->
| Parameter  | Dimension     | Unit | Range     | Default   | Meaning                    |
|------------|---------------|------|-----------|-----------|----------------------------|
| head       | Head          | m    | 0.1–500   | sized     | Head at the duty point     |
| flow       | MassFlow      | kg/s | 0–1000    | sized     | Duty flow                  |
| speed      | Dimensionless | –    | 0–1.2     | 1.0       | Relative speed             |
| efficiency | Dimensionless | –    | 0.1–0.95  | 0.7       | Hydraulic efficiency       |
<!-- /generated -->

Every pump parameter is optional. For `pump`, omitting one means "size it".

## Sizing
With no `head`, the pump is sized to the total pressure drop around its loop at the design
flow. **No safety margin is applied** — the figure is the computed loop drop and nothing
more. Fittings, strainers and balancing valves are not modelled, so a real installation
will need more. Write `head=` to state your own.

## Examples

### Minimal
    circuit simpleLoop
    fluid water

    HE1 heat_exchanger power=30 in=20 out=50
    CV1 valve
    PU1 pump
    P1  pipe length=25

    connections
    N1 - PU1 - N2 - HE1 - N3 - CV1 - N4 - P1 - N1

`PU1` is sized to 5.28 m at 0.241 l/s — the loop's drop of 51.7 kPa (2.4 kPa of pipe,
20 kPa across `HE1`, 29.4 kPa across `CV1`).

### Stating a head
    PU1 pump head=8

The pump now delivers 8 m and the loop settles at a higher flow. If the loop cannot
absorb 8 m, `FS2303` reports the mismatch rather than silently ignoring it.

## Diagnostics
| Code    | When                                                 |
|---------|------------------------------------------------------|
| FS2104  | `head` and `dp` are both stated and disagree         |
| FS2303  | A stated head the circuit cannot use                 |
| FS4007  | Operating outside the curve's usable range           |

## See also
[valve](valve.md) · [Sizing strategy](../advanced/sizing-strategy.md)
```

Both examples compile in CI, the parameter table is generated, and 5.28 m is the number the solver
actually produces for **this** circuit — the same figure as
[`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)'s worked example, which uses the same
**simple loop** reference ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)).

**The circuit and the number have to match, and that is easier to get wrong than it sounds.** An
earlier draft of this page showed a valve-less loop while quoting the loop drop of a circuit that has
one — 51.7 kPa includes 29.4 kPa of valve, so without `CV1` the honest figure is about 2.3 m. Tutorial
rule 4 ("every number shown is real") is not satisfied by a number that is real *somewhere else*.
An agent reading this page has the syntax, the full parameter set with units and ranges, the sizing
basis, two working examples, and the errors it might hit. That is what `R-29` asks for.

## Acceptance criteria

- [ ] Every component kind, directive, and diagnostic code has a page or generated entry; CI enforces it.
- [ ] Every `fluidscript` block in `/docs` compiles, or produces its annotated diagnostic.
- [ ] Generated pages regenerate identically; a stale one fails the build.
- [ ] Every function page has every template section in order.
- [ ] The tutorial has no forward references.
- [ ] Every number in the tutorial is produced by running its script.
- [ ] An agent given only `functions/index.md` and the pages it links can author a valid circuit —
      tested by actually doing it and checking the result compiles and solves.
- [ ] Tutorial page 01 reaches a rendered diagram in under ten lines.

## Open questions

None. v1 ships plain Markdown under `/docs`; a generated site/playground needs later adoption evidence.
`/api/v1/metadata.docsIndex` points to `docs/functions/index.md` (or its deployed URL) without embedding
duplicate prose. `plan/` and its undated roadmap remain public with their README explaining planning
status and evidence gates (`D-30`).
