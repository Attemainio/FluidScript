# Plan review rubric

The authority on what counts as a finding when auditing `plan/`. Read by every `plan-reviewer` agent
and by the orchestrating session before it partitions a tier.

## What this is not

**Not a code review.** No code exists. Nothing here is about naming, style, performance, or idiom.

**Not a prose review.** Wording, tone, and length are not findings unless they cause ambiguity a
reader would resolve wrongly. "This paragraph could be shorter" is not a finding. "This paragraph can
be read two ways, and the two readings produce different implementations" is.

**Not a design debate.** A settled `D-` entry in `plan/00-foundation/06-decision-log.md` is binding.
Disagreeing with one is legitimate, but it is a **proposal to supersede**, labelled as such, with the
new information that changed the picture — not a finding that the plan is wrong.

## The question every finding must answer

> **Would a competent fresh session, given only these documents, build the wrong thing — or be unable
> to decide what to build?**

If neither, it is not a finding. That test is deliberately strict: a review that reports twenty
observations buries the two that matter.

## Severity

| | Meaning | Test |
|---|---|---|
| 🔴 **Blocking** | The plan cannot be implemented as written | A fresh session would stop, or would build something that contradicts another document |
| 🟡 **Should fix** | Implementable, but a session would have to guess | A reasonable implementer could make a choice the plan did not intend |
| 🔵 **Nit** | Neither | Recorded in the file, not raised in conversation |

**Only 🔴 and 🟡 gate convergence.** A tree with fifty nits and no 🔴/🟡 is converged.

## The twelve axes

Every reviewer assesses every axis over its scope and says which ones it could not assess. An
untriggered axis is reported **not assessed**, never clean.

### 1 · Completeness

Every responsibility named somewhere has exactly one owner, and nothing is left implied.

| Finding | Severity |
|---|---|
| A responsibility named in `01-vision-and-scope` that no document `owns` | 🔴 |
| A document's Contracts section omits a type its own prose depends on | 🔴 |
| A `TBD` or "to be decided" with no matching entry under Open questions | 🟡 |
| An Error cases table missing a failure the document's own prose describes | 🟡 |
| A `depends_on` on a document that does not define what is being depended on | 🟡 |

**An open question is not incompleteness.** A recorded unknown with a stated blocker and a resolution
path is the plan working correctly. An *unrecorded* one is the finding.

### 2 · Consistency

Two documents describing the same thing differently. **The highest-value axis**, and the only one a
reviewer with a multi-document scope can assess well — which is why scopes are clusters, not files.

| Finding | Severity |
|---|---|
| Two documents specify the same contract differently | 🔴 |
| The same concept under two names, or one name for two concepts | 🔴 |
| Two documents claim the same `owns` entry | 🔴 |
| A worked example's numbers contradict another document's for the same case | 🔴 |
| A term used that `02-glossary` bans, or a term absent from it | 🟡 |
| A cross-reference pointing at a document that does not cover the cited topic | 🟡 |

### 3 · Contract precision

`D-04`: responsibilities, signatures, invariants, error cases, worked examples, acceptance criteria —
no method bodies.

| Finding | Severity |
|---|---|
| A key type described in prose with no signature | 🔴 |
| A dimensioned member with no stated unit | 🔴 |
| A public method with no stated behaviour on failure | 🟡 |
| An invariant that is not falsifiable ("the graph is well formed") | 🟡 |
| A signature with unexplained nullability | 🟡 |
| Method bodies or pseudocode where a signature would do (over-specification) | 🔵 |

**Missing units on a dimensioned value is 🔴, always.** It is the single most consequential omission
this plan can contain, and the resulting bug is invisible.

### 4 · Testability

| Finding | Severity |
|---|---|
| An acceptance criterion that cannot be answered yes/no | 🟡 |
| A criterion depending on an undefined tolerance | 🟡 |
| A document with no worked example | 🟡 |
| A worked example with no numbers, or with numbers that are not computed | 🔴 |
| A stated behaviour with no criterion covering it | 🟡 |

**Check the arithmetic in every worked example.** This is the most valuable single thing a reviewer
does. A specification can read perfectly and not compute, and the error propagates into the tests
written from it. Recompute each step; a discrepancy is 🔴.

### 5 · Feasibility

| Finding | Severity |
|---|---|
| Relies on an API that does not exist as described | 🔴 |
| Physics or mathematics that is wrong | 🔴 |
| A numerical method that will not converge as specified | 🔴 |
| A performance claim contradicted by the design | 🟡 |
| An unverified external dependency presented as fact rather than as an open question | 🟡 |

Where a document *flags* an assumption as unverified, that is honest and not a finding. Where it
asserts one, it is.

### 6 · Traceability

| Finding | Severity |
|---|---|
| A requirement (`R-`) with no owning document | 🔴 |
| A document whose `traces_to` is empty | 🟡 |
| A `traces_to` id that does not exist | 🟡 |
| A document specifying work no requirement asks for | 🟡 |
| A `depends_on` pointing up-tier | 🔴 |

The last one is structural: an up-tier dependency means a foundational decision was made inside a leaf,
and it will be discovered when the leaf changes.

### 7 · Scope discipline

| Finding | Severity |
|---|---|
| A Phase-1 document depending on deferred work | 🔴 |
| A milestone criterion requiring something outside that milestone | 🔴 |
| A document contradicting a stated non-goal | 🔴 |
| Scope creep — specifying beyond what the requirement asks | 🟡 |

### 8 · LLM readability

`R-29`. Applies fully to `61-documentation-plan` and to anything specifying `/docs` or a machine-readable
surface; applies weakly elsewhere.

| Finding | Severity |
|---|---|
| A machine-readable surface with no stable identifier scheme | 🔴 |
| A documented structure an agent could not navigate without prose inference | 🟡 |
| An example that is a fragment rather than a complete script | 🟡 |
| An enumerable set given as prose rather than a table | 🔵 |

### 9 · Terminology and behavioral wording

Wording is a finding when it changes what gets built or what passes acceptance, not merely when an
editor could make it prettier.

| Finding | Severity |
|---|---|
| One term names two behavioral concepts, or one concept has two normative names | 🔴 |
| A requirement or criterion has two implementation-relevant readings | 🟡 |
| A repeated term is absent from or contradicts the glossary | 🟡 |
| A sentence claims an output the stated input cannot produce | 🔴 |
| Pure tone, grammar, or brevity preference | 🔵 |

### 10 · Rationale and decision readiness

The plan embeds the reasoning an implementer needs at decision boundaries. It does not narrate every
mechanical step.

| Finding | Severity |
|---|---|
| A binding choice has no reason and a reasonable alternative changes architecture or behavior | 🟡 |
| A decision is accepted but not propagated to every constrained contract | 🔴 |
| An external assumption is asserted without verification, a spike, or a fallback | 🟡 |
| Obvious mechanics lack a "why" paragraph | Not a finding |

### 11 · Structure and phasing

| Finding | Severity |
|---|---|
| A milestone criterion contradicts the document that owns the behavior | 🔴 |
| A prerequisite is scheduled after the feature that needs it | 🔴 |
| Requirements have document owners but no milestone and executable criterion | 🟡 |
| Active questions mix current blockers, closed history, and deferred ideas without status | 🟡 |
| A large vertical slice hides two independently verifiable risk boundaries | 🟡 |

### 12 · Necessity and operational completeness

Review both directions: unnecessary scope and missing prerequisites.

| Finding | Severity |
|---|---|
| The next user outcome cannot be completed or preserved (for example, no save lifecycle) | 🔴 or 🟡 by milestone |
| Scale, latency, compatibility, persistence, deployment, or verification assumptions affect design but have no owner | 🟡 |
| A feature adds a subsystem/public contract/docs surface without serving a requirement or near milestone | 🟡 |
| A deferred feature is described at implementation-contract detail long before evidence can choose it | 🟡 |

## Cross-cutting checks worth running every pass

1. **Recompute every worked example.** See axis 4.
2. **Check every unit and sign convention.** A pressure drop positive in one document and negative in
   another is 🔴 and is easy to miss.
3. **Follow every cross-reference** and confirm the target covers what is cited.
4. **Check the `owns`/`does not own` pairs**: A says B owns X; does B's `owns` list X?
5. **Read the Open questions for contradictions** — two documents deferring the same decision to each
   other is a deadlock, and it is 🔴.
6. **Map every accepted decision to its constrained documents.** A decision-log-only feature is 🔴.
7. **Map every requirement to a milestone criterion.** Frontmatter ownership does not prove delivery.
8. **Separate open-question states.** Closed entries leave the section; deferred ideas move to the
   roadmap; current blockers name an owner and resolution gate.
9. **Look for missing operational contracts:** persistence, versioning/migration, supported scale,
   deployment/platform, determinism, security boundary, accessibility, and independent validation.
10. **Challenge scope explicitly.** State what should be kept, simplified, deferred, or deleted and
    what user outcome or dependency justifies that recommendation.

Check 5 catches a specific and common failure: two documents each saying "resolved in the other".

## Writing a finding

```
🔴 F-3 · 22-component-model · consistency

**Finding.** The pump's sign convention (pressure rise as a negative drop) contradicts
16-diagnostics' FS4007 example, which reports a positive rise.

**Why it blocks.** An implementer following 22 writes a negative residual; one following 16
writes a positive one. Loop closure then fails by twice the pump head, and the symptom —
a solver that does not converge — points at the solver rather than at the sign.

**Suggested fix.** 16-diagnostics' example adopts 22's convention; add a one-line note to
FS4007 stating that a pump's drop is negative.
```

Four parts: what, where, what breaks, what to do. A finding without "why it blocks" cannot be
triaged, and one without a suggested fix makes the user do the thinking twice.

**Never report:** wording preferences, a document being long, formatting, missing content a document
explicitly says it does not own, or disagreement with a settled `D-` entry (unless labelled as a
supersession proposal with new information).
