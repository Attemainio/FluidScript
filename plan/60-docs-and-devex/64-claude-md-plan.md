---
id: 64-claude-md-plan
title: CLAUDE.md plan
tier: 60-docs-and-devex
status: reviewed
owns: [the planned contents of CLAUDE.md, its size budget, its update policy]
depends_on: [04-engineering-standards, 61-documentation-plan, 63-ci-and-repo-hygiene]
traces_to: [R-16, R-17, R-28]
open_questions: 0
last_review_pass: 2
---

# CLAUDE.md plan

## Purpose

`CLAUDE.md` is loaded into every session in this repository, so it is the most expensive file in the
project per byte and the only one every future contributor-agent reads. This document specifies what
goes in it, drawing on the two reference files the user named — `PandaAI/CLAUDE.md` and
`dotnet-toolkit/CLAUDE.md` — and, as importantly, what stays out.

## Responsibilities

**Owns.** The planned contents of `CLAUDE.md`, its size budget, and its update policy.

**Explicitly does not own.** The engineering standards themselves
([`04-engineering-standards`](../00-foundation/04-engineering-standards.md)), CI
([`63-ci-and-repo-hygiene`](63-ci-and-repo-hygiene.md)), documentation
([`61-documentation-plan`](61-documentation-plan.md)).

## What each reference file contributes

| From `dotnet-toolkit/CLAUDE.md` | Why it applies here |
|---|---|
| The **operating-contract** framing — rules that apply every session, plus where to read detail | Sets the right expectation: this is not the manual |
| A **when → read what** routing table instead of inlined procedure | Keeps the file small and the detail current where it lives |
| **Non-obvious invariants** section | This project has several that a session would otherwise break: SI internally, one SharpProp reference, Core has no UI dependency |
| **Context budget** — this file is always loaded, keep it declarative | The discipline that keeps it useful |
| **Compact instructions** | What to preserve when the conversation is summarised |

| From `PandaAI/CLAUDE.md` | Why it applies here |
|---|---|
| The **Reference Index** table | FluidScript has many subsystem documents; a router is essential |
| **MCP tools, never grep, for C#** — with the Read/Grep-still-right-for-non-C# carve-out | Same plugin, same reasoning; and FluidScript has a lot of non-C# (TypeScript, markdown, `.fluid`) |
| **Interaction Protocol** — 95 % certainty before non-trivial work, plan-then-execute, collaborative | Matches how this project is being built |
| **Keeping docs current** self-update rule | `R-28` demands it; a session that adds a feature must add its page |
| **Response style** — multiple options with risk and priority, walkthroughs with real sample values | The physics here genuinely needs worked examples with numbers |
| **Disagreeable by default** | A tool that computes engineering numbers needs its assumptions challenged |

## What stays out

As important as what goes in, because every line is paid for in every session:

- **The coding standards.** Plugin-owned ([`04`](../00-foundation/04-engineering-standards.md)); a copy
  here would diverge and would never reach a consumer of the plugin.
- **Physics, the solver, the language.** All of it is in `plan/` and later `/docs`. `CLAUDE.md` points.
- **Command lists beyond the three that matter.** `dotnet build`, `dotnet test`, `npm run dev`.
- **Anything transient** — current milestone status, what is half-finished. It goes stale and misleads.

## Planned structure

```markdown
# CLAUDE.md

<one paragraph: what FluidScript is, and that this file is the operating contract, not the manual>

## Reference index
<table: when → read what. Rows for plan/ during M0–M5, docs/ after.>

## Non-negotiable rules
- Every feature ships with its /docs page. CI enforces it; do not work around the gate.
- Core has no UI or hosting dependency. Physics never moves to the frontend.
- Everything is SI internally; canonical units only at the language and wire boundaries.
- Exactly one type references SharpProp.
- Every dimensioned public member states its unit and sign convention in XML docs.
- A decision that changes a documented one adds a D- entry; it never edits the old one.
- No pipeline stage throws on user input; malformed source is a return value.

## Navigating C# code
<MCP-tools-not-grep, with the non-C# carve-out. Points at the plugin's skills; restates nothing.>

## Commands
dotnet build · dotnet test · dotnet test --filter Category=Unit · npm run dev
<plus the one-line note that Category=Unit is under two seconds and worth running constantly>

## Interaction protocol
<95% certainty · plan then execute · collaborative and disagreeable by default>

## Response style
<concise by default · detailed on technical questions · options with risk and priority ·
 step-by-step walkthroughs with real sample values>

## Non-obvious invariants
<the handful that a session breaks first — see below>

## Keeping this current
<self-update rule; what belongs here versus in a subfile>

## Context budget
<this file and any always-loaded rule are the only ones under budget; keep them declarative>

# Compact instructions
<what to preserve when compacting>
```

## The non-obvious invariants

The section that earns its place. Each is something a session would plausibly get wrong, with the
consequence stated so the reason is not lost:

- **Temperature and TemperatureDelta are different dimensions.** `20 °C + 30 °C` must be an error, not
  596 K ([`13`](../10-language/13-type-and-unit-system.md)).
- **A missing parameter follows its registry omission policy — normally sizing, or a visible decided
  default — and a stated one is always a constraint.** Absence, never null (`D-02`, `D-32`,
  [`15`](../10-language/15-semantic-model.md)).
- **`Print(Parse(x)) == x` byte for byte.** Canvas write-back depends on it; a printer that tidies
  breaks the feature ([`17`](../10-language/17-formatting-and-round-trip.md)).
- **The solver system is scaled before solving.** An unscaled residual norm measures the pressure
  equation and nothing else ([`36`](../30-solver/36-numerics-and-convergence.md)).
- **`EvaluateResiduals` allocates nothing** — it runs N+1 times per Newton iteration
  ([`22`](../20-core-domain/22-component-model.md)).
- **Humid-air enthalpy is per kg of dry air**, unlike every other substance. A few percent, and it
  looks like a modelling choice ([`21`](../20-core-domain/21-fluid-and-state.md)).
- **DN is a designation, not a diameter.** DN25 has a 27.3 mm bore
  ([`27`](../20-core-domain/27-component-catalog.md)).
- **Never scrape or redistribute paywalled standards.** Catalogue dimensions are independently
  sourced facts; every row carries two public manufacturer sources
  ([`27`](../20-core-domain/27-component-catalog.md)).
- **Editing never mutates or cancels an active transient.** A run owns an immutable source/version
  snapshot; isolation, worker, protocol, or integrity failure stops at the last verified frame
  (`D-22`, [`07`](../00-foundation/07-quality-attributes.md)).

Nine lines that prevent nine expensive mistakes.

## Size budget

**Under 150 lines**, matching both reference files (151 and 171). Enforcement is the update policy
rather than a check: anything that grows past it moves into a subfile behind a pointer.

The test: **could a session do good work in this repository having read only this file and the one
document its task routes to?** If yes, the file is right. If it needs three more, the routing table is
wrong. If it needs nothing else, the file is too big.

## Update policy

| Change | Where it goes |
|---|---|
| A new subsystem document | A row in the reference index |
| A new coding convention | The plugin's standards, or `04-engineering-standards` — never here |
| A new always-applicable rule | Here, in Non-negotiable rules |
| A new invariant a session would break | Here, in Non-obvious invariants |
| A new command | Here, if it is one of the few that matter; otherwise `63` |
| Anything task-specific | The document that owns it |

**Anything operational that lives only in `CLAUDE.md` is a finding, not a convenience** — it means the
document that should own it does not.

## When it is written

**At the end of planning, before M0 starts.** Writing it now would mean rewriting it after every plan
revision; writing it after M0 means the first implementation sessions run without it, which is exactly
when the invariants matter most.

The reference index points at `plan/` during M0–M5 and gains `/docs` rows as those pages appear. The
switch is a row-by-row migration, not a rewrite.

## Invariants

1. Under 150 lines.
2. Contains no coding standard, only a route to one.
3. Contains no physics, language, or solver detail — only routes.
4. Every reference-index row points at a file that exists.
5. Every non-obvious invariant names the document that owns it.
6. Nothing transient: no status, no in-progress notes.

## Worked example

The reference index, as planned for the end of M2:

| Topic | File |
|---|---|
| Why the project exists, what it will not do, milestones | `plan/00-foundation/01-vision-and-scope.md` |
| Decisions already settled, and why | `plan/00-foundation/06-decision-log.md` |
| Repository layout, build configuration | `plan/00-foundation/03-repository-layout.md` |
| Coding standards routing, XML docs, lint | `plan/00-foundation/04-engineering-standards.md` |
| The script language: grammar, units, expressions, binding | `plan/10-language/` |
| Fluids, components, topology, sizing, the catalogue | `plan/20-core-domain/` |
| Solvers, tolerances, convergence | `plan/30-solver/` |
| API and wire contracts | `plan/40-api/` |
| Frontend, canvas, design system | `plan/50-frontend/` |
| Testing tiers and validation cases | `plan/60-docs-and-devex/62-testing-strategy.md` |
| Documentation structure and the docs gate | `plan/60-docs-and-devex/61-documentation-plan.md` |

Eleven rows, most pointing at a directory rather than a file — a session working on the solver reads
`plan/30-solver/`'s two or three relevant documents, not all six. That granularity is the compromise
between a router that is too coarse to help and one that lists forty-three files.

## Acceptance criteria

- [ ] Under 150 lines.
- [ ] Every reference-index row resolves to an existing path.
- [ ] No coding standard, physics, or solver detail is restated.
- [ ] Every non-obvious invariant cites its owning document.
- [ ] A session given only `CLAUDE.md` and one routed document can complete a representative task —
      tested by actually trying it on a real task before M1.
- [ ] Nothing in it goes stale between milestones.

## Open questions

None. FluidScript adds no project-owned always-loaded `.claude/rules/` file; the plugin's router is the
only such rule. Project-local skills are named in the workflow paragraph/reference index so a fresh
agent can discover `/plan-review` without loading each skill body into every session (`D-30`).
