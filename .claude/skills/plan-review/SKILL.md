---
name: plan-review
description: Use when auditing, reviewing, or converging the FluidScript planning documents under plan/ — "review the plan", "/plan-review", "check tier 20", "what is missing", "is the plan ready to implement", terminology, rationale, scope cuts, structure, or phasing. Runs mechanical checks, reviews contract correctness and operational completeness, writes a dated findings file, and reports changes for user acceptance before applying them. Not for C# or TypeScript code review.
---

# Reviewing the FluidScript plan

`plan/` is a contract-level specification that future sessions implement from. This skill audits it.

**A plan review is not a code review.** The failure mode is a session that defaults to reviewing prose
style, structure, and tone. What matters is whether a fresh session could *build* from these documents
without guessing: are the contracts precise, do the documents contradict each other, does every
requirement have an owner, and are the numbers right. `rubric.md` in this directory is the authority on
what counts as a finding; read it before reviewing anything. It covers twelve axes, including wording,
rationale, phasing, missing operational features, and scope economy.

**Reviewers report; you apply on approval** (`D-05`). Reviewer agents are read-only. You present their
findings, the user accepts or rejects each, and you make the edits. Never let an agent edit `plan/`.

## Step 0 — read the state

```bash
cat .claude/plan-review/state.json
```

It tells you the pass number, which tier is next, and the convergence streak. If it is missing or
malformed, initialise it from the template at the end of this file and say so.

**Which tier to review**, in priority order:

1. The tier the user named, if they named one.
2. `state.nextTier`.
3. If `state.nextTier` is null, a sweep just completed — start a new sweep at `00-foundation`.

## Step 1 — mechanical checks, before spending any agent

```bash
python3 .claude/plan-review/check.py
```

Cheap, exact, and it catches the errors an LLM reviewer is worst at. Run it first over the **whole
tree** — cross-tier breakage is exactly what it finds. Exit code 0 means clean. Report failures and
continue the semantic review unless the corpus cannot be mapped at all; a mechanical failure is a
finding, not a reason to omit the rest of the requested review.

It checks: frontmatter presence and required keys · `id` and `tier` matching the path · README index
row count and targets against the documents on disk · every `depends_on` resolving, and **never
pointing up-tier** · no concept `owns`-ed by two documents · declared `open_questions` matching the
entries · every internal link resolving (code fences excluded) · every template section present ·
requirement traceability in **both** directions — no unclaimed `R-` id, no empty `traces_to` · no
reference to an undeclared `R-` id · no closed question retained in the active question count.

Report its failures directly. They need no agent's opinion, and several of them — an up-tier
dependency, a doubly-owned concept, an unclaimed requirement — are blocking findings in their own
right.

If the script itself is missing or errors, say so and fall back to reading; do not skip the checks.

## Step 2 — review the tier

Review directly by default. If the user explicitly requested delegated or parallel review, fan out
read-only reviewers as below. Direct review uses the same rubric, evidence requirements, and report
format; it is not a reduced mode.

Before writing findings, build five cross-document maps: requirement -> owner -> milestone criterion;
decision -> constrained contract; glossary term -> uses; reference example -> restatements; open
question -> status/owner/resolution gate.

### Optional reviewer fan-out

Launch one `plan-reviewer` agent per document cluster, **in parallel, in a single message**. Scopes
must be disjoint: the same document in two scopes produces duplicate, possibly-conflicting findings.

**If `plan-reviewer` is not an available agent type**, the harness registered its agent list at session
start and `.claude/agents/plan-reviewer.md` was created or changed since. It will resolve in the next
session. Until then, launch `general-purpose` agents with the same brief plus a first instruction to
read `.claude/agents/plan-reviewer.md` and follow it exactly — same review, one extra file read.

| Tier size | Partition |
|---|---|
| ≤ 3 documents | One agent, the whole tier |
| 4–7 documents | Two or three agents, grouped by adjacency (`12`+`13`+`14` together — they interlock) |
| More | One agent per 2–3 closely-related documents |

Aim for **two to four agents per tier**, not one per document. A reviewer that sees a whole coherent
slice catches contradictions between its documents; one that sees a single file can only check it
against itself.

### What every reviewer needs

Each starts cold. Anything you state is context it does not have to re-derive, and re-derivation is the
expensive part of a parallel run.

- **Scope**: exact file paths it owns. Never "the rest".
- **The rubric path**: `.claude/skills/plan-review/rubric.md`. It must read this.
- **The decision log**: `plan/00-foundation/06-decision-log.md`. A finding that contradicts a settled
  `D-` entry is not a finding; it is a proposal to supersede it, and must be labelled as such.
- **The requirement register**: `plan/00-foundation/01-vision-and-scope.md`.
- **The `owns`/`depends_on` index for the tier**, so it can check cross-document consistency without
  reading every file.
- **The pass number**, and any findings from the previous pass on those documents — so it does not
  re-report something already accepted and applied, or already rejected.

## Step 3 — write the findings file

`.claude/plan-review/findings/<UTC-date>-<HHmmss>-<tier>.md`. Create the directory if absent.

```markdown
# Plan review — <tier> — pass <n>

Reviewed: <file list>
Reviewers: <n> · Mechanical checks: pass | <failures>

## 🔴 Blocking (<count>)
### F-<n> · <document> · <axis>
**Finding.** One sentence.
**Why it blocks.** What a fresh session would get wrong.
**Suggested fix.** Concrete.

## 🟡 Should fix (<count>)
## 🔵 Nits (<count>)

## Coverage
| Axis | Assessed | Notes |
Axes not assessed are reported as **not assessed**, never as clean.
```

Also include: essential missing operational contracts; open questions grouped as decide-now, spike,
decide-by-milestone, deferred, and closed; terminology/source-of-truth issues; recommended phasing;
and a keep/simplify/defer/delete table for scope.

Answer inline as well. The file is the durable record, not a substitute for telling the user.

## Step 4 — present, then apply what is accepted

Present 🔴 and 🟡 grouped by document, most severe first. Nits go in the file, not the conversation,
unless the user asks.

For each accepted finding: make the edit, and if it changes a documented decision, **add a new `D-`
entry** to `06-decision-log.md` rather than editing the old one. Bump `last_review_pass` in the
frontmatter of every document you touched, and update its `status` to `reviewed`.

Never apply a finding the user did not accept. Never apply a 🔵 silently.

## Step 5 — update the state

```jsonc
{
  "pass": 3,                            // increment every time this skill runs
  "sweep": 1,                           // increments when a full tier cycle completes
  "nextTier": "30-solver",              // null when a sweep just finished
  "cleanSweeps": 0,                     // consecutive sweeps with no 🔴 and no NEW 🟡
  "tiers": {
    "00-foundation": { "lastPass": 3, "blocking": 0, "shouldFix": 2, "reviewedAt": "2026-08-29T09:14:00Z" }
  },
  "history": [ { "pass": 3, "tier": "00-foundation", "blocking": 0, "shouldFix": 2, "applied": 2 } ]
}
```

**The convergence gate.** The loop stops when `cleanSweeps >= 2` — a *complete* sweep of every tier
producing no blocking findings and no new should-fix findings, twice consecutively.

The "twice" is not caution for its own sake. A single clean sweep most often happens on the pass right
after a large edit, which is precisely when the plan is least settled and reviewers are most likely to
be agreeing with text they just saw applied. Reset `cleanSweeps` to 0 whenever a sweep produces any
🔴 or any new 🟡.

When `cleanSweeps` reaches 2, say so plainly, state what the remaining open questions are (they do not
block convergence — an open question is a *recorded* unknown, which is the plan working correctly), and
stop scheduling further passes.

## Running as a loop

`/loop /plan-review` sweeps tier by tier across sessions. Each firing does one tier and updates the
state, so a new session picks up mid-sweep with the history intact.

Between passes there is nothing to poll — the work is synchronous. If pacing a dynamic loop, a long
interval is right; the loop's purpose is to survive session boundaries, not to run continuously.

**Stop the loop** when `cleanSweeps >= 2`, or when the user says so, or when a pass produces a finding
that needs a decision only the user can make — do not spin on a question no agent can answer.

## Initial state file

```json
{ "pass": 0, "sweep": 0, "nextTier": "00-foundation", "cleanSweeps": 0, "tiers": {}, "history": [] }
```

Tier order: `00-foundation`, `10-language`, `20-core-domain`, `30-solver`, `40-api`, `50-frontend`,
`60-docs-and-devex`, `70-future`.
