# CLAUDE.md

FluidScript is a plant-modeling tool: you describe a hydronic or air-side system in a terse,
markdown-like script, and it sizes the components, solves the circuit, and draws it as a live P&I
diagram beside the text. Backend is .NET 10 (`FluidScript.Core` + `FluidScript.Api`); frontend is
React + Vite.

This file is the **operating contract** — the rules that apply in every session and where to read the
detail. It is not the manual. The project is in its planning phase: `plan/` is the specification being
implemented, `docs/` is the user-facing documentation that ships with each feature.

## Reference index

Read the relevant document before working in that area; update it when you learn something new there.

| Topic | File |
|---|---|
| Why the project exists, the requirement register (`R-xx`), non-goals, phases | `plan/00-foundation/01-vision-and-scope.md` |
| Decisions already settled and why — **binding** | `plan/00-foundation/06-decision-log.md` |
| What implementing a tier actually found — defects, deferrals, traps | `plan/<tier>/defects.md` |
| Canonical term for every concept, casing conventions | `plan/00-foundation/02-glossary.md` |
| Repository layout, build props, project boundaries | `plan/00-foundation/03-repository-layout.md` |
| Coding-standard routing, XML docs, frontend lint | `plan/00-foundation/04-engineering-standards.md` |
| Milestones and their exit criteria | `plan/00-foundation/05-milestones-and-acceptance.md` |
| Work-package order inside a milestone, and why — **the plan** | `plan/08-implementation-sequence.md` |
| What has shipped, which phase we are in, what is next — **the state** | `plan/09-project-state.md` |
| Performance, scale, accuracy, execution isolation, stop, accessibility | `plan/00-foundation/07-quality-attributes.md` |
| The script language: grammar, units, expressions, binding, diagnostics, printer | `plan/10-language/` |
| Fluids, components, topology, sizing, catalogue, model contract, the layout (`28`) and its ladder (`29`) | `plan/20-core-domain/` |
| Solvers, controllers, tolerances, convergence | `plan/30-solver/` |
| API hosting and the REST / WebSocket / diagnostics contracts | `plan/40-api/` |
| Frontend, editor, canvas, design system, state visualization | `plan/50-frontend/` |
| Script compatibility, files, and static export | `plan/10-language/18-script-compatibility.md`, `plan/50-frontend/58-file-lifecycle.md`, `plan/50-frontend/59-static-export.md` |
| Documentation structure and the docs gate | `plan/60-docs-and-devex/61-documentation-plan.md` |
| Test tiers, validation cases, tolerances | `plan/60-docs-and-devex/62-testing-strategy.md` |
| CI, public-repo requirements, releases | `plan/60-docs-and-devex/63-ci-and-repo-hygiene.md` |

**Plan review:** `/plan-review` audits `plan/`; `/loop /plan-review` sweeps it across sessions.
`.claude/skills/plan-review/SKILL.md` owns the protocol.

## Working a plan

Nothing a session learns survives it unless it is written into `plan/` or `docs/`. This is the loop
that makes that happen. It is not optional and it is not only for large changes.

### Before starting

1. **`plan/08-implementation-sequence.md`** — the whole map: every phase, every work package, and why
   each sits where it does. Written in the future tense; it never changes to match what happened.
2. **`plan/09-project-state.md`** — the position on that map: what has shipped, what the current phase
   is, what is next. Read second, always.
3. **The plan documents of the phase you are in**, and the **`defects.md` of every tier they belong
   to**. The defect file is where the traps are; the contract document alone will mislead you.

### While working

- **A defect gets its entry the moment it is understood, not when it is fixed.** File it as an open
  question in the owning tier's `defects.md` immediately.
- **Except when it can be fixed now.** Something resolvable in the same change is an edit, not a
  finding, and filing it produces a register that only grows. Fix it and say so in the commit.

### When a package or phase completes

1. **Update the tier's `defects.md`** — move what closed, add what opened, keep observations worth
   keeping. Every tier whose documents the work touched, not just the one the code lives in.
2. **Write the entry from the beginning, not from the conclusion** (see Response style). The existing
   entries are the standard: what the code meant to do, what it did instead, why that is wrong
   physically, what was measured, and what is still open.
3. **Update `plan/09-project-state.md`** — the package's row, the ids it closed, any open id that
   changes what the next session should do, and the baselines if they moved.

`09` references defect ids and never restates them. A description in two places is a description that
disagrees with itself.

## Non-negotiable rules

- **Every feature ships with its `/docs` page.** No exceptions. CI enforces it; do not work around the
  gate, and do not defer documentation to a later milestone.
- **Core has no UI or hosting dependency.** Physics never moves to the frontend, and neither does
  geometry: Core solves the layout (`D-103`); the frontend maps world units to pixels, draws, and
  formats numbers, nothing else.
- **Everything is SI internally.** Canonical units exist only at the language boundary and on the wire.
- **Exactly one type references SharpProp.** Everything else depends on `ISubstance`.
- **Every dimensioned public member states its unit and sign convention** in its XML docs.
- **A change that alters a settled decision adds a new `D-` entry** to the decision log; it never edits
  the old one.
- **Implementing a phase records what it found** in the `defects.md` of every `plan/` tier whose
  documents it worked against, **and updates `plan/09-project-state.md`**. See *Working a plan*.
- **No pipeline stage throws on user input.** A script under editing is malformed most of the time;
  malformed input is a return value.
- **Look engineering conventions up; never derive them.** Before settling a sizing rule, coefficient,
  rounding direction or default, search published manufacturer and industry guidance and cite it in the
  `plan/` document. First principles are how a plausible invention reaches the spec, and the user's own
  experience is empirical and partial. Mark whatever the search cannot confirm as this project's
  reasoning, and treat it as the part most worth testing.

## Navigating C# code

For anything C#, use the `dotnet-toolkit` MCP tools — not `Grep`, `Glob`, `find`, or reading whole
`.cs` files to locate something. Grep is blind to interface and virtual dispatch, counts comment and
string matches as hits, and under-reports silently on truncation. The protocol lives in the plugin's
`dotnet-read` / `dotnet-write` / `dotnet-explore` / `dotnet-review` skills and is not restated here.

`Read` is still right for a **known** file and region. `Grep`/`Glob` are still right for **non-C#**
files — TypeScript, CSS, Markdown, `.fluid` scripts, config — which is most of this repository.

## Commands

```bash
dotnet build                              # zero warnings, or it fails (TreatWarningsAsErrors)
dotnet test                               # everything
dotnet test --filter-trait Category=Unit  # under 2 s — run it constantly
cd frontend && npm run dev                # Vite dev server, proxies /api and /ws
```

### FluidScript experiment protocol

For every FluidScript experiment: execute the actual script; read the complete `SolveExplanation`
report; trace its counting, constraints, seeds, solved values, residuals, sizing decisions, warnings,
and conditioning together; change only what that full evidence supports; then rerun and repeat. A
passing diagnostic harness means only that it wrote the report — convergence is stated inside it.

## Interaction protocol

- **Get to 95 % certainty before non-trivial work.** Ask focused clarifying questions until you are
  sure what is being asked. Do not guess intent where there are multiple reasonable approaches.
- **Plan first, execute after approval.** Explain the approach before writing code.
- **This is collaborative.** The user does not blindly trust outputs — discuss, challenge, iterate.

## Response style

- **Default: concise.** No filler.
- **Technical questions: detailed.** Go into reasoning, trade-offs, and mechanics.
- **Be analytical and critical** — say why something might fail, and where the uncertainty is.
- **Present multiple options when meaningful alternatives exist**, each with advantages,
  disadvantages, and risk (high/med/low). End significant design decisions with a comparison table.
- **For non-trivial new logic, give a step-by-step walkthrough with real sample values** — what goes
  in, what comes out, and why. This project's physics needs numbers, not descriptions.
- **Be disagreeable by default.** Challenge ideas and point out blind spots. A tool that computes
  engineering numbers needs its assumptions attacked.
- **Explain a defect from the beginning, not from the conclusion.** For a bug, a wrong claim or plan
  drift: what the code meant to do, what it did instead, why that is wrong *physically*, and how it got
  there — plain language first, mechanism second, `C-`/`S-` number last. Assume an energy engineer not
  carrying this convention today: an explanation they can argue with beats one that is merely correct.

## Non-obvious invariants

Each of these is a mistake a session makes first, with the consequence that follows.

- **`Temperature` and `TemperatureDelta` are different dimensions.** `20 °C + 30 °C` must be an error,
  not 596 K. → `plan/10-language/13-type-and-unit-system.md`
- **A missing parameter follows its registry omission policy — normally sizing, or a visible decided
  default — and a stated one is always a constraint.** Absence, never null. → `D-02`, `D-32`,
  `plan/10-language/15-semantic-model.md`
- **`Print(Parse(x)) == x` byte for byte.** Canvas write-back depends on it; a printer that tidies
  while printing breaks the feature. → `plan/10-language/17-formatting-and-round-trip.md`
- **The solver system is scaled before solving.** An unscaled residual norm measures the pressure
  equation and nothing else. → `plan/30-solver/36-numerics-and-convergence.md`
- **`EvaluateResiduals` allocates nothing.** It runs N+1 times per Newton iteration. →
  `plan/20-core-domain/22-component-model.md`
- **Humid-air enthalpy is per kg of *dry air*,** unlike every other substance. The error is a few
  percent and looks like a modelling choice. → `plan/20-core-domain/21-fluid-and-state.md`
- **DN is a designation, not a diameter.** DN25 steel pipe has a 27.3 mm bore. →
  `plan/20-core-domain/27-component-catalog.md`
- **Never scrape or redistribute paywalled standards.** Dimensions are facts; a standard's tables are
  not. Every catalogue row carries two public sources. →
  `plan/20-core-domain/27-component-catalog.md`
- **Editing never mutates or cancels an active transient.** A run owns an immutable source/version
  snapshot; computation and render preparation stay off the UI thread. Any isolation breach stops the
  run at its last verified frame. → `D-22`, `plan/00-foundation/07-quality-attributes.md`

## Keeping this current

- New subsystem document → a row in the reference index.
- New coding convention → the plugin's standards or `04-engineering-standards.md`, **never here**.
- New always-applicable rule or invariant a session would break → here.
- Everything else → the document that owns it, with at most a pointer here.

**Anything operational that lives only in this file is a finding**, not a convenience: it means the
document that should own it does not.

## Context budget

This file is always loaded, and `AGENTS.md` points at it so that an agent which does not read
`CLAUDE.md` by convention still lands here. Keep it a declaration of *when* and *where*, under ~190
lines. An overage is fixed by moving guidance behind a pointer, not by deleting it. Do not add a second unfrontmattered
file to `.claude/rules/` — it would be always-loaded for every session and every subagent.

# Compact instructions

When compacting, preserve: the task in flight and its remaining steps; any settled decision from this
conversation and its `D-` number; open questions the user has answered; and which `plan/` documents
have been read or edited. Drop resolved tool output, file listings, and superseded drafts.
