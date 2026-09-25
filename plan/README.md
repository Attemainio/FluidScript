# FluidScript — implementation plan

This tree is the specification FluidScript is built from. No code lives here, and no *contract*
document describes work already done. Once a subsystem is implemented, its user-facing documentation
moves to `/docs` and the plan document becomes the historical record of *why* it is shaped the way it
is.

**Three kinds of file break that rule on purpose**, and each says so in its own header: the
[implementation sequence](08-implementation-sequence.md), which sequences the work; the
[project state](09-project-state.md), which records what has shipped; and each tier's `defects.md`,
which records what implementing that tier found. A specification with no memory beside it is
re-derived from the commit log every session.

## How to read this

0. If you are about to *implement* rather than read, start with
   [`08-implementation-sequence.md`](08-implementation-sequence.md) and then
   [`09-project-state.md`](09-project-state.md) — the plan and the position on it. The full workflow
   is in the repository's `CLAUDE.md` under *Working a plan*.
1. Start with [`00-foundation/01-vision-and-scope.md`](00-foundation/01-vision-and-scope.md) — it
   holds the numbered requirements (`R-01`…) that every other document traces back to.
2. Read [`00-foundation/06-decision-log.md`](00-foundation/06-decision-log.md) for the decisions
   already settled and why. Do not re-litigate a `D-` entry without adding a new one that supersedes it.
3. Then read the tier that owns your task. Each document states what it owns and what it explicitly
   does not, so you should never need to read the whole tree to make one change.

**Tier numbers encode dependency direction.** A document may depend on an equal or lower tier, never
a higher one. `depends_on` pointing up-tier is a review finding, not a style preference — it means a
foundational decision was made inside a leaf.

**Every contract document follows [`_template.md`](_template.md)**: Purpose · Responsibilities ·
Contracts · Invariants · Error cases · Worked example · Acceptance criteria · Open questions.
Contracts are signatures and data shapes, never method bodies (decision `D-04`). The three record
files named above do not follow it, and should not — that shape describes a contract, and a record of
what happened has none of those sections to fill.

## Status

`draft` — written, not yet reviewed. `reviewed` — one review pass applied. `stable` — two consecutive
sweeps with no blocking or should-fix findings.

The 2026-08-30 remediation pass reviewed the original corpus. The documents touched by the accepted
left-to-right thermal-layout and stratified-tank additions (`D-31`, `D-32`) are reset to `draft` until
an independent semantic pass reviews the new cross-tier contract; untouched documents remain
`reviewed`. None is `stable`. A status is a claim about review history, not about how good prose looks.

The five **reference circuits** all live in `01-vision-and-scope`, and `D-11` still forbids unnamed
variants: the cooling loop (topology), the simple loop (sizing arithmetic), the substation (two-sided
rating and coupled circuits), the demand-step loop (transient and control), and the storage header
(multiple sources/consumers, thermal ordering, and stratified storage).

### Plan root

| Doc | Owns | Status |
|---|---|---|
| [08-implementation-sequence](08-implementation-sequence.md) | work-package decomposition and order inside each milestone | draft |
| [09-project-state](09-project-state.md) | which phase we are in, what has shipped, what is next | living |
| [70-core-refactoring](70-core-refactoring.md) | which Core sections are rewritten wholesale, in what order, and the parameter-ownership and solved-view shapes they are rewritten to | draft |
| [71-source-structure](71-source-structure.md) | the folder and namespace layout of Core, partial files and one type per file, base and derived naming, and the component and sizer bases (`D-147`) | draft |

All three sit above the tiers because all three are about the whole project; `70` takes the number the future tier's folder leaves unused, so its short reference clashes with no document. `08` moved here from
`00-foundation/` on 2026-09-09; it had always been a whole-plan document filed as a foundation one.

### 00 · Foundation

| Doc | Owns | Status |
|---|---|---|
| [01-vision-and-scope](00-foundation/01-vision-and-scope.md) | requirements `R-xx`, non-goals, phase boundaries | draft |
| [02-glossary](00-foundation/02-glossary.md) | domain vocabulary, canonical spelling of every term | draft |
| [03-repository-layout](00-foundation/03-repository-layout.md) | directory tree, solution files, build props | reviewed |
| [04-engineering-standards](00-foundation/04-engineering-standards.md) | coding standards routing, XML docs, lint | reviewed |
| [05-milestones-and-acceptance](00-foundation/05-milestones-and-acceptance.md) | phases M1–M6 and their exit criteria | draft |
| [06-decision-log](00-foundation/06-decision-log.md) | `D-xx` decisions and their rationale | draft |
| [07-quality-attributes](00-foundation/07-quality-attributes.md) | performance, scale, accuracy, isolation, stop, and accessibility gates | draft |

### 10 · Language

| Doc | Owns | Status |
|---|---|---|
| [11-language-overview](10-language/11-language-overview.md) | design principles, the inference rules | reviewed |
| [12-grammar](10-language/12-grammar.md) | lexical + syntactic grammar, AST node shapes | reviewed |
| [13-type-and-unit-system](10-language/13-type-and-unit-system.md) | dimensions, canonical units, coercion | draft |
| [14-expressions-and-references](10-language/14-expressions-and-references.md) | `let`, arithmetic, member refs, evaluation order | reviewed |
| [15-semantic-model](10-language/15-semantic-model.md) | binder, semantic model, lowering to the domain graph | draft |
| [16-diagnostics](10-language/16-diagnostics.md) | `FSxxxx` codes, severities, spans, recovery | reviewed |
| [17-formatting-and-round-trip](10-language/17-formatting-and-round-trip.md) | printer, write-back, trivia preservation | draft |
| [18-script-compatibility](10-language/18-script-compatibility.md) | language versions, catalogue pins, compatibility, migration | draft |
| [19-fluidscript-2](10-language/19-fluidscript-2.md) | language 2: blocks, port inference, cases and drivers, controllers, runs | draft |

### 20 · Core domain

| Doc | Owns | Status |
|---|---|---|
| [21-fluid-and-state](20-core-domain/21-fluid-and-state.md) | fluid abstraction, state points, SharpProp boundary | reviewed |
| [22-component-model](20-core-domain/22-component-model.md) | component interfaces, v1 kinds, ports, governing equations | draft |
| [23-topology-and-graph](20-core-domain/23-topology-and-graph.md) | graph construction, implicit nodes, well-posedness | draft |
| [24-auto-sizing](20-core-domain/24-auto-sizing.md) | sizing rules, constraint propagation, default catalogue | draft |
| [25-layout-hints](20-core-domain/25-layout-hints.md) | the classification the layout starts from: order, thermal stages, flow, groups, circuits, inferred | draft |
| [26-model-contract](20-core-domain/26-model-contract.md) | the serialized model shared by API, canvas, exporters | draft |
| [27-component-catalog](20-core-domain/27-component-catalog.md) | standard pipe/valve dimension tables, sourcing policy, provenance | reviewed |
| [28-layout-solver](20-core-domain/28-layout-solver.md) | the layout: the model, the standard, the rules the ladder establishes, the candidate algorithms, the diagnostic text (`D-103`, `D-106`, `D-107`) | draft |
| [28-layout-solver.source](20-core-domain/28-layout-solver.source.md) | the user's specification `28` part D draws its candidates from, kept verbatim | draft |
| [29-layout-ladder](20-core-domain/29-layout-ladder.md) | the step log of the layout ladder: what each step drew, what the user corrected, which rule came of it | draft |

### 30 · Solver

| Doc | Owns | Status |
|---|---|---|
| [31-solver-architecture](30-solver/31-solver-architecture.md) | `ISolver` seam, residual formulation, unknowns | reviewed |
| [32-steady-state-newton](30-solver/32-steady-state-newton.md) | Newton–Raphson, Jacobian, damping, convergence | reviewed |
| [33-transient-time-domain](30-solver/33-transient-time-domain.md) | time integration, transport delay, step control | draft |
| [34-controllers](30-solver/34-controllers.md) | PID/PI, setpoints, anti-windup, coupling | reviewed |
| [35-evolutionary-sizing](30-solver/35-evolutionary-sizing.md) | evolutionary optimizer for sizing problems | reviewed |
| [36-numerics-and-convergence](30-solver/36-numerics-and-convergence.md) | tolerances, scaling, failure taxonomy | reviewed |

### 40 · API

| Doc | Owns | Status |
|---|---|---|
| [41-api-architecture](40-api/41-api-architecture.md) | hosting, DI, session and model lifetime | reviewed |
| [42-rest-contract](40-api/42-rest-contract.md) | compile/solve/validate/edit/metadata endpoints | draft |
| [43-realtime-contract](40-api/43-realtime-contract.md) | WebSocket frame protocol for transient runs | draft |
| [44-diagnostics-contract](40-api/44-diagnostics-contract.md) | diagnostics and warnings over the wire | reviewed |

### 50 · Frontend

| Doc | Owns | Status |
|---|---|---|
| [51-frontend-architecture](50-frontend/51-frontend-architecture.md) | React/Vite layout, state, the debounce pipeline | reviewed |
| [52-editor](50-frontend/52-editor.md) | script editor, highlighting, completion, squiggles | draft |
| [53-canvas-renderer](50-frontend/53-canvas-renderer.md) | the canvas: drawing Core's scene, symbols, labels, interaction | draft |
| [54-interaction-and-writeback](50-frontend/54-interaction-and-writeback.md) | hover, edit-on-canvas, script mutation | reviewed |
| [55-design-system](50-frontend/55-design-system.md) | themes, palette, tokens, typography, motion | reviewed |
| [56-console-log](50-frontend/56-console-log.md) | the warning/log stream and its phrasing | reviewed |
| [57-state-visualization](50-frontend/57-state-visualization.md) | the `show` directive, colour scales, gradients, the legend | draft |
| [58-file-lifecycle](50-frontend/58-file-lifecycle.md) | new/open/save, dirty state, conflicts, recovery | reviewed |
| [59-static-export](50-frontend/59-static-export.md) | M3 standalone SVG and PNG export | draft |

### 60 · Docs and dev-ex

| Doc | Owns | Status |
|---|---|---|
| [61-documentation-plan](60-docs-and-devex/61-documentation-plan.md) | the `/docs` tree and its templates | draft |
| [62-testing-strategy](60-docs-and-devex/62-testing-strategy.md) | test layout, validation, isolation, worker, file, and accessibility tests | draft |
| [63-ci-and-repo-hygiene](60-docs-and-devex/63-ci-and-repo-hygiene.md) | CI, public-repo requirements, contribution flow | reviewed |
| [64-claude-md-plan](60-docs-and-devex/64-claude-md-plan.md) | the planned contents of `CLAUDE.md` | reviewed |
| [65-working-the-plan](60-docs-and-devex/65-working-the-plan.md) | the session runbook: reads, commands, filing / reopening / closing, package and phase close | draft |

### 70 · Future

| Doc | Owns | Status |
|---|---|---|
| [71-export-formats](70-future/71-export-formats.md) | evidence gate for future DXF/model interchange | reviewed |
| [72-roadmap](70-future/72-roadmap.md) | scope beyond v1 | draft |
| [73-equipment-list](70-future/73-equipment-list.md) | the equipment list's rows, columns, provenance, and gate | draft |

## Review protocol

This tree is audited by the project-local **`plan-review`** skill, not by ad-hoc reading.

- A session runs `/plan-review` for a single tier, or `/loop /plan-review` to sweep the whole tree.
- Reviewers are read-only. They report; the session applies only what you accept.
- State lives in `.claude/plan-review/state.json`; findings in `.claude/plan-review/findings/`.
- The loop converges when a complete sweep produces no blocking and no new should-fix findings,
  **twice consecutively**.

Details: `.claude/skills/plan-review/SKILL.md` and its `rubric.md`.
