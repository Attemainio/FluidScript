---
name: plan-review
description: Review specification and planning documents for implementation readiness. Use for "review the plan", "audit plan/", "what is missing", "is this ready to implement", plan consistency, open-question triage, terminology, scope cuts, rationale, structure, or phasing. Reports evidence-backed findings and does not edit the specifications until the user accepts fixes. Not for source-code review.
---

# Review the FluidScript plan

Audit `plan/` as the contract future implementers receive. The governing question is:

> Could a competent fresh agent implement the intended system from these documents without guessing,
> reconciling contradictions, inventing rationale, or discovering a missing prerequisite in code?

Default to the whole tree unless the user names a narrower tier or document. A narrow review still
checks its decisions, requirements, dependencies, and cross-references outside the scope.

## Repository protocol

Before reviewing, read:

1. `CLAUDE.md` or `AGENTS.md`, if present, for repository rules.
2. `plan/README.md` and `plan/_template.md`.
3. `plan/00-foundation/01-vision-and-scope.md` (requirements and phase boundaries).
4. `plan/00-foundation/06-decision-log.md` (binding decisions and rationale).
5. `plan/00-foundation/02-glossary.md` and `05-milestones-and-acceptance.md`.
6. `.claude/skills/plan-review/rubric.md`, the shared Codex/Claude finding rubric.

The repository already stores review state, mechanical checks, and durable findings under
`.claude/plan-review/`. Use those paths from Codex too; do not start a second state history.

Reviewers report. Do not change `plan/` merely because a finding seems obvious. Present proposed
changes and wait for acceptance. Writing the requested review report and maintaining review state are
part of the review, not acceptance of specification edits.

## Mechanical pass

Run from the repository root:

```bash
python3 .claude/plan-review/check.py
```

Report every failure. Continue the semantic review unless the corpus cannot be mapped at all; a
mechanical failure is evidence, not a reason to skip the rest of the user's requested review.

Also map these relationships, using tables or small scripts when useful:

- every `R-` requirement -> owning documents -> milestone -> executable acceptance criterion;
- every accepted `D-` decision -> constrained documents -> actual contract text and tests;
- every canonical term -> glossary entry -> consistent use;
- every reference circuit/example -> all restatements and expected values;
- every open question -> status, blocking milestone, decision owner, and resolution method.

An accepted decision that exists only in the decision log is a finding. A requirement that has a
document owner but no milestone acceptance criterion is also a finding.

## Semantic pass

Assess every axis in the shared rubric. In particular:

- recompute worked examples and check units, sign conventions, tolerances, orders of magnitude, and
  whether the stated input actually reaches the stated output;
- distinguish a genuine recorded unknown from a missing contract. An unresolved choice that changes
  a public shape, physics equation, milestone criterion, or source-of-truth rule blocks implementation;
- find duplicated authorities. Repetition for readability must point to one normative owner and may
  not restate mutable numbers or contracts independently;
- identify missing operational prerequisites: persistence, versioning/migration, supported scale,
  deployment/platform assumptions, cancellation/lifetime, reproducibility, security boundaries,
  accessibility where relevant, and verification data;
- challenge scope. Mark a feature for deletion or deferral when it adds a new subsystem, contract, or
  documentation surface without being necessary for the next milestone's user outcome;
- require rationale at the decision boundary. Do not demand a "why" paragraph for obvious mechanics,
  but do require one where a reasonable implementer could choose differently;
- treat unclear wording as a finding only when different readings yield different implementations or
  acceptance results. Give replacement wording or the missing definition.

For facts about external APIs, packages, standards, or current platform behavior, verify against a
primary source when feasible. Label an unverified assumption rather than presenting memory as fact.

## Open-question triage

Do not present one flat list. Classify each unresolved question:

| Class | Meaning | Required action |
|---|---|---|
| Decide now | Changes an M0/M1 contract or the next milestone | User/architect decision before implementation |
| Spike first | Depends on external API, benchmark, or measured behavior | Named spike with output and deadline |
| Decide by milestone | Safe now but blocks a later milestone | Record owner and decision gate |
| Deferred | Not needed in the planned release | Move to roadmap; remove from active open-question count |
| Closed | Already settled by a `D-` entry or normative text | Remove from Open questions; retain history in the decision log |

An open question is well formed only when it says what it blocks and how/when it will be resolved.

## Findings and severity

- **Blocking**: the plan cannot be implemented as written, two authorities conflict, a criterion is
  impossible, essential behavior is absent, or the next milestone depends on an unresolved contract.
- **Should fix**: implementation is possible only after a reasonable guess, the structure obscures
  authority, rationale is missing at a real decision, or unnecessary scope threatens delivery.
- **Nit**: wording or organization does not change behavior. Keep these out of the conversation unless
  requested, but record them in the report.
- **Supersession proposal**: a settled `D-` decision may be reconsidered only under this label, with
  new evidence or a newly visible cost. It is not a contradiction finding.

Each blocking or should-fix finding contains:

1. exact file and line evidence;
2. the conflicting or missing contract;
3. what an implementer would get wrong or have to guess;
4. a concrete correction and its blast radius.

## Deliverable

Write `.claude/plan-review/findings/<UTC-date>-<HHmmss>-<scope>.md` and answer with the important
findings inline. Include:

1. readiness verdict and reviewed scope;
2. mechanical-check result;
3. blocking findings, then should-fix findings;
4. essential missing capabilities/non-functional contracts;
5. open questions grouped by the triage classes above;
6. terminology/wording and source-of-truth issues;
7. structure and recommended phase changes;
8. features to retain, defer, simplify, or delete, with reasons;
9. aspects assessed and any not assessed;
10. a short ordered action plan.

Do not hide findings in the durable file. The final response must stand alone and link to it.

If the user explicitly asks for parallel or delegated review and subagents are available, partition
by coherent document clusters with disjoint ownership and synthesize once. Otherwise review directly.

