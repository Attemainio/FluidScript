---
id: 60-docs-and-devex-defects
title: What implementing against the docs and devex tier found
tier: 60-docs-and-devex
owns: [defect and observation record for documents 61-63]
---

# What implementing against the docs and devex tier found

Defects, deferrals and observations from implementing against `61`–`63`. The rule and its reasoning
are in [`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

**`61` and `62` have been implemented against since M0**; `63` only through the architecture tests
that assert its table. Like [`30-solver/defects.md`](../30-solver/defects.md), this file was created
late — the findings below were made across P2.6 through P3.4a and recorded, where they were recorded
at all, in the tier of the code rather than the tier of the document.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| T-1 | [`62`](62-testing-strategy.md) | **A seventh test category exists and the table names six** | `P3.1` added `Category=Diagnostic` for the property-performance harness: runs that write a timing report into `diagnostics/` rather than asserting anything, deliberately excluded from every gate because their duration is the thing being measured. `62`'s table has `Unit`, `Property`, `Validation`, `Golden`, `Api` and `Docs`, each with a budget and a cadence, and a category outside it has neither — so nothing says whether CI runs it, when, or what a regression in it means. It also matters that this one is *not* a test tier in the same sense: it has no pass criterion, and a table row saying so is what stops the next reader adding it to the pre-commit set. |
| T-2 | [`61`](61-documentation-plan.md) | **The docs gate compares a page against its generator, not against the registry, so an incomplete generator passes green** | `P3.3` registered a tank's indexed property families and the generated properties page did not change, because `RegistryPages.RenderProperties` walked `kind.Properties` and knew nothing about `kind.IndexedPropertyFamilies`. The page then said a tank has three readable properties when it has three plus one per layer and one per port, and the gate was green throughout — it asserts the file matches what the code generates, which was true and useless. The gate's shape is right for drift and blind to omission. What would catch it is asserting the generated region covers every *name a reference can resolve*, which is a different question from every name a dictionary holds. |
| T-3 | [`61`](61-documentation-plan.md) | **The gate knows about kinds, reserved words and codes, and a feature is none of those** | `P3.3` shipped eight diagnostics and four "What is checked" sections on component pages. The gate enforced the first set — every code has a row — and could not see the second: a parameter gaining a hard bound is a user-visible behaviour change with no registry entry to hang a gate on. The four sections were written by hand and would have been forgotten silently. This is the limit of a mechanical gate rather than a defect in it, but `61` presents the gate as *the* enforcement of "every feature ships with its page", and it enforces a proper subset. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| T-4 | [`62`](62-testing-strategy.md), [`22`](../20-core-domain/22-component-model.md) | **`62`'s worked example cannot evaluate the relation it claims to** | It builds `SolveContext.ForSingleComponent(FakeWater.Instance, massFlow: 0.2391)` — a substance and a flow, no port states — and calls `EvaluateResiduals` on a duty exchanger. `22`'s energy relation is `Q̇ = ṁ(h_out − h_in)` over the *solved* port enthalpies, and a context with no port states has no enthalpies to difference. The example's own constructor implied the other reading: a duty from stated terminal temperatures and a `cp`, which is a real relation and is the `FS2101` one. It shipped as `HeatExchanger.ImpliedFlow`, a reported property rather than a residual, and the test asserts both routes and asserts they agree. `SolveContext.ForSingleComponent`, added on the strength of the example, was removed the same day — nothing could use it. Recorded from the other side as `C-19`, because the finding is about both documents. |

## Observations

**Regenerating a page in place, and failing the test that did it, is the right shape for a generated
region.** Adding eight diagnostic codes was one test run: the gate rewrote
`docs/functions/diagnostics.md`, failed with "did not match what the code generates and has been
regenerated in place — review the change and run the tests again", and passed on the second run. The
diff is then reviewable as a diff, which a gate that only says "does not match" does not give you.
Worth naming because the instinct is to make a gate read-only.

**The `/docs` rule holds better than the gate does, and that is not a contradiction.** Everything
shipped in P3.0–P3.4a has its page, including the two — `advanced/how-a-script-becomes-a-circuit.md`
and the "What is checked" tables — that no gate would have asked for. What did the work was the rule
being unambiguous and stated where a session reads it, not the enforcement. `61` should say that the
gate is a floor and the rule is the requirement; presently the document leans on the gate.

**A test category with no pass criterion needs somewhere to put its output, and `diagnostics/` is
gitignored except for its README.** `P3.1`'s harness writes timing reports there. This works and is
worth recording only because the obvious alternative — asserting a threshold — would have been wrong:
property-evaluation cost varies by an order of magnitude across backends and machines, and a threshold
tight enough to catch a regression fails on somebody's laptop by lunchtime.

**Three tests guard the diagnostic codes and none of them asks whether a code ever fires.**
`CodeRangeOwnershipTests` checks that every registered code falls in a documented range and is
mentioned in the document that range names — both directions of the *documentation* claim, and
neither of the behavioural one. So `FS2202` and `FS2217` shipped in `P3.4b` implemented, reachable,
and with no test asserting either fires, while every other code in their range had one; the range gate
was green throughout. This is the same shape as `S-8`, where `FS2211` was implemented and
*un*reachable for a whole package, and it is worth stating that the two failures are one failure: the
suite has no notion of a code being exercised.

`DiagnosticCoverageTests` now closes it: every registered code must be named by a string literal in
some test, with comments stripped, fixture-constructed descriptors discounted, and the gate's own file
excluded from its own scan — without which an exemption row would name the code it exempts and cover
it. It found six more live codes nobody had tested (`FS1512`, `FS1513`, `FS1522`, `FS1526`, `FS2004`,
`FS2006`), all six now covered. Both directions were falsified before the gate was trusted, because a
drift check that has never failed is the thing this entry is about.

**The allow-list it was built around turned out to be unnecessary, and the reason is worth more than
the list would have been.** The claim above — that it would turn `L-1`, `L-21` and `C-23` into
machine-checked claims — was wrong. Every code those three name is *unregistered*: `FS1201` exists
only as a fixture literal, `FS1405` nowhere at all, and only ten of `C-23`'s sixteen `FS21xx` codes
have descriptors. `DiagnosticRegistry`'s remark says why, and it is the correct answer already —
registering a code before the stage that raises it would make its own "every entry is emitted"
assertion unsatisfiable, so each package registers its own. A code that cannot fire is therefore never
registered, and never reaches this gate. The list stays, empty, for the case that policy does not
cover; it is not the value.

**A text scan is a floor and the file says so.** A code named in a negative assertion counts, and so
does one in a test asserting something other than that it fires. Proving a code is *raised* needs a
collector every test routes through, and that is a different gate with a much worse cost. What this
one catches is precisely what happened three times: a live code no test mentions at all.
