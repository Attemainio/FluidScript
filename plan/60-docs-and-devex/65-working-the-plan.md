---
id: 65-working-the-plan
title: Working the plan
tier: 60-docs-and-devex
status: draft
owns: [the session procedure for executing the plan — what to read, where to log, the commands, and the checklists for filing, reopening, closing, package close and phase close]
depends_on: [06-decision-log, 61-documentation-plan, 62-testing-strategy, 64-claude-md-plan]
traces_to: [R-28, R-30]
open_questions: 0
last_review_pass: 0
---

# Working the plan

## Purpose

`08` says why every package writes down what it found and what a register looks like; `CLAUDE.md`
says the loop in eleven lines; `09` says what it needs when a package closes. None of them says
*which command*, *which grep*, *in which order*. This document does. It is the runbook a session
executes, top to bottom, and it is written as checklists with the actual commands because the
failure it prevents is a session inventing a slightly different procedure each time — one that reads
the last row for an id, closes a row without rerunning its script, or commits a plan edit without
the check that would have caught it.

These are hard rules. A step that cannot be done is reported as not done, never skipped silently.

## Responsibilities

**Owns.** The procedure: the reads at session start, the commands, the grep lines, the order of edits
when a defect is filed, reopened or closed, when a package closes, and when a phase closes.

**Explicitly does not own.** The *reasons* — those are `08`'s (the register) and `CLAUDE.md`'s (the
contract). The vocabularies of the register columns — `08`. What `09` contains — `09`. The docs gate
— `61`. The test tiers and the experiment loop — `62`. This document links to each and restates
nothing; where it seems to, the other document wins and this one has drifted.

## Session start

Every session, before any edit, in this order:

1. `plan/08-implementation-sequence.md` — the map. Read the phase you are in whole; skim the rest.
2. `plan/09-project-state.md` — *Where the project stands*, the phase ladder, *What is next*, and
   the position notes of the phase in progress. This is where the previous session left the keys.
3. For every tier whose documents the work will touch: the plan documents named by the package, then
   that tier's `defects.md` — the **Open** table and the **Traps** list, whole. Nothing else in the
   register front to back; the rest is reached by id.
4. The mechanical check and the baseline, so that "did I break it" has an answer later:

```bash
python3 .claude/plan-review/check.py                       # must end "all structural checks pass"
                                                           # or list only the F-27 residual
dotnet build 2>&1 | tail -3                                # zero warnings
DOTNET_GCHeapHardLimit=0x100000000 timeout 900 \
  ~/.dotnet-artifacts/bin/FluidScript.Core.Tests/debug/FluidScript.Core.Tests | tail -3
timeout 600 ~/.dotnet-artifacts/bin/FluidScript.Api.Tests/debug/FluidScript.Api.Tests | tail -2
cd frontend && npx vitest run 2>&1 | tail -4               # only when the package touches frontend/
```

The counts go against `09`'s *Standing baselines*. A mismatch before any edit is the first finding of
the session, not something to explain away later. `dotnet test` does not run tests in this
environment; the binaries are run directly (`62`).

5. State, in the reply, the package about to be worked, the plan documents read, and the open ids
   that bear on it — then wait for approval unless the user has already given it for this package.

## Filing a defect

The moment a problem is understood and cannot be fixed in the same change:

1. **Grep first.** The subject may already have a row or a decision.

```bash
grep -n -i "<subject words>" plan/*/defects.md | cut -c1-160
grep -n -i "<subject words>" plan/00-foundation/06-decision-log.md | cut -c1-160
```

   - A **closed row** that describes it → reopen (below), do not refile.
   - An **open row** that describes it → add the new measurement to that row's *Why it is still open*.
   - A **`D-`** that settles it → the answer is the decision, unless the measurement disagrees; then
     file, cite the `D-`, and say what disagrees.
   - Nothing → file.

2. **Take the id** from the register's `**Next id:**` line and bump the line. Never read the last row.
3. **Write the row** in the Open table, top of the table, with every column:
   `| id | effort | risk | basis | documents | **one-sentence what** | why it is still open |` — the
   vocabularies are in `08` *The register's shape*. Basis is `measured` only if the numbers in the row
   come from a run and the mechanism is located in code; otherwise `hunch`.
4. **Cite the id** where the problem shows: a test's XML doc, a code comment at the workaround, the
   plan document's acceptance criterion it fails (`(open: C-108)`).
5. **`09`** only if the id changes what the next session should do — then in *What is next*, by id.
6. Run `python3 .claude/plan-review/check.py` before the commit that carries the row.

## Reopening a closed row

1. **Re-read the document the row cites**, at the clause it argues with. If the rule changed after the
   row closed, the reopening says so and cites the `D-` that changed it.
2. **Move the row** from Closed back to Open, id kept. Keep the closed prose as the first sentence of
   *Why it is still open* (`Closed <date> by …; reopened <date>:`), then the new measurement. Fill
   Effort / Risk / Basis afresh — the old Effort was the first fix's cost, not this one's.
3. If the row is reopening because the previous fix did not hold, that is the second hit: **promote a
   one-line trap** to the register's Traps list naming what the fix missed.
4. `09` *What is next* if it changes the order of work; `check.py`; commit.

A row whose numbers predate a rule change is not reopened: it stays open, gets a
`**Stale since D-nnn:**` sentence, and its Basis drops to `hunch` (`C-64` is the model).

## Closing a row

1. **Run the case that opened the row** — the script, the fixture, the ladder step — and read the
   whole report (`62`, *FluidScript experiment loop*). A fix that was not measured on that case is an
   edit, and the row stays open with a note.

```bash
# solver: put the script in diagnostics/scratch/, run the harness, read diagnostics/circuit-reports.md
DOTNET_GCHeapHardLimit=0x100000000 timeout 900 \
  ~/.dotnet-artifacts/bin/FluidScript.Core.Tests/debug/FluidScript.Core.Tests \
  -filter "/*/FluidScript.Core.Tests.Performance/CircuitDiagnostics/*"
# layout: the ladder writes diagnostics/layout-ladder/*.txt
... -filter "/*/FluidScript.Core.Tests.Layout/LayoutLadderTests/*"
```

2. **Move the row** to the top of the Closed table: `| id | effort-actual | documents | **what was
   wrong** | what changed, what was measured, which package |`. Effort is what it took, not what was
   estimated; the gap between the two is the calibration and is worth a clause.
3. **Fix the document** the row was about, if the fix amended it, and say so in the row — the document
   now reads as though it was always right.
4. **Promote to Traps** if closing it revealed a guard, an ordering or a sizer rule a session will get
   wrong again. One line, pointing at the row.
5. **Remove the open-marker citations** placed at filing (`(open: C-108)`); keep the id in any test
   that pins the fix.
6. `09`: the id in the closing package's row, ids only; `check.py`; commit.

## Package close

Not complete until every line is done, in the same commit series, the same session:

- [ ] The feature's `/docs` page is written or regenerated (`61`; the gate runs in the Core
      `Documentation` tests). No package defers it.
- [ ] Every register whose documents the package worked against is updated — closed rows moved with
      their actual effort, new rows filed, traps promoted — or the commit says *found nothing in
      tier NN*.
- [ ] The plan documents touched say what is now true; a `D-` is appended, never edited, if a settled
      decision moved (`06`), and `python3 .claude/plan-review/check.py --write-index` is run after
      it.
- [ ] `09`: the package's row in *What each phase delivered* (commit, date, ids closed); *What is
      next* rewritten so the next session starts from a sentence; *Standing baselines* if they moved;
      the open-questions table's counts if a row was filed or closed.
- [ ] The full check and the full suites, and the counts quoted in the commit:

```bash
python3 .claude/plan-review/check.py
dotnet build 2>&1 | tail -3
DOTNET_GCHeapHardLimit=0x100000000 timeout 900 ~/.dotnet-artifacts/bin/FluidScript.Core.Tests/debug/FluidScript.Core.Tests | tail -3
timeout 600 ~/.dotnet-artifacts/bin/FluidScript.Api.Tests/debug/FluidScript.Api.Tests | tail -2
cd frontend && npx tsc -b && npx vitest run 2>&1 | tail -4
```

- [ ] The commit message: first line names the package and the ids it closed; the body explains from
      the beginning (`CLAUDE.md`, *Response style*); the attribution lines the session was given.
      `.claude/dotnet-toolkit/`, `.claude/agent-memory/`, `.claude/settings.json` are never staged.
- [ ] The reply to the user states what was not done, if anything, before what was.

## Phase close

A phase closes when the user says so, on this evidence, in this order:

1. **`05`**: every criterion of the milestone ticked `- [x]` with the package or test that proves it
   in parentheses, or left `- [ ]` with the reason and the `D-` or id that defers it. A criterion
   ticked on the strength of prose is a criterion not ticked.
2. **`09`**: the phase ladder row (`State`, `Closed` date); *Where the project stands* rewritten to
   the new position, not appended to; the phase's section header gains `· complete <date> · <commit>`.
3. **`08` is not edited.** It is the plan as written; where the phase diverged, `09` says so and `08`
   keeps its future tense. A package that `08` did not foresee is added to `08` with the `D-` that
   created it, which is the one exception.
4. **Every register**: the Open table read once, whole, by the user and the session together — what
   stays open into the next phase is said in `09` *What is next*, by id, with its effort and risk.
5. **The baselines** re-measured and written, not carried forward.
6. `check.py` clean, or its residual filed.

## The sweep

Between packages, on request: the registers' columns answer the two questions directly.

```bash
# fixable now: tiny or small, measured
grep -hE "^\| [A-Z]-[0-9]+ \| (tiny|small) \| [a-z]+ \| measured \|" plan/*/defects.md | cut -c1-120
# largest: big or large, by risk
grep -hE "^\| [A-Z]-[0-9]+ \| (big|large) \| high \|" plan/*/defects.md | cut -c1-120
```

A sweep is run tier by tier, each tier's closures reported and approved before the next, quick fixes
before hard ones, and each closure follows *Closing a row* whole — a sweep is where "fixed" without
"measured" is most tempting, because the rows are small.

## Invariants

1. Every step in this document names a command or a file; a step that names neither is a reason, and
   reasons belong in `08` or `CLAUDE.md`.
2. An id is taken from a `Next id` line, never from the last row.
3. A row is closed only after the case that opened it was rerun and its report read.
4. `check.py` runs before every commit that touches `plan/`, and a crash is a failure.
5. `08` is not edited at phase close; `09` is rewritten, not appended.
6. This document and `CLAUDE.md` *Working a plan* agree; when they do not, this one is wrong and is
   fixed, because `CLAUDE.md` is the contract and this is its procedure.

## Acceptance criteria

- [ ] A session that has read only `CLAUDE.md` and this document files, reopens and closes a row in the
      shape `check.py` accepts, without consulting the transcript of an earlier session.
- [ ] Every command in this document runs in this environment as written.
- [ ] A package closed by this checklist leaves `09`'s counts equal to the registers' rows.

## Open questions

None.
