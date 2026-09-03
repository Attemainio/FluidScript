---
id: 00-foundation-defects
title: What implementing against the foundation found
tier: 00-foundation
owns: [defect and observation record for documents 01-08]
---

# What implementing against the foundation found

Defects, deferrals and observations from implementing against `01`–`08`. The rule and its reasoning
are in [`08-implementation-sequence`](08-implementation-sequence.md).

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| F-3 | [`07`](07-quality-attributes.md) | No latency budget is measured | `D-48`/`D-49` budget interactive latency end to end and derive the debounce, and nothing measures any of it yet. The first honest measurement needs a pipeline that binds, so P2.8 at the earliest; `05` ties the real gate to M3. |
| F-4 | [`03`](03-repository-layout.md) | **The nine-file list is a definition of "done" that no single package can satisfy**, and P3.3 satisfied four rows of it | P3.3 built six component kinds and produced the component type, the keyword registration, the unit tests and the `/docs` page. It did not produce the sizing rule or its tests (`P3.7` owns sizing and `src/FluidScript.Core/Sizing/` does not exist), the declarative symbol (`D-24` defers `Model/Symbols/` to M3), the renderer coverage (frontend, M3), or the per-component sample script. So the list is right about what a *finished* component has and wrong to read as a checklist for the package that introduces one — which is how it reads, and the document calls it "the definition of done for a component". It needs a column saying which package supplies each row. P3.3 also deviated on naming: the valve and pump share `ValveAndPumpTests.cs` rather than taking a file each, because the pump's curve and the valve's law are tested against the same regularisation. |
| F-5 | [`08`](08-implementation-sequence.md) | **P3.4 is sequenced before P3.5, and lowering cannot build a pipe without the catalogue** | `23`'s lowering step 1 turns every flow-carrying symbol into a component "with its stated parameters converted to SI", which is true of every kind but one: a pipe needs an inside diameter, a script states `dn`, and DN25 is a 27.3 mm bore rather than 25 mm. That mapping is `27`'s and ships in P3.5. `08`'s row for P3.4 gives its reason as "the no-syntax-type-in-`CircuitGraph` assertion goes live here" and does not mention geometry at all. P3.4a worked around it with an injected `IBoreLookup` and a test fixture, which is the right seam for an unrelated reason — P3.7's outer loop re-instantiates components as sizing chooses values, so lowering has to be re-runnable against changing geometry anyway. What is open is whether `08` should say so, or whether the two packages should swap. Recorded from the other side as `C-24`. |
| F-6 | [`07`](07-quality-attributes.md) | **The property-call cost the whole latency budget rests on is 20 000× what the M0 spike recorded** | The spike measured 0.003 µs per property read; the diagnostics harness measures ~63 µs for one water (T,p) property and ~204 µs for a seven-property `FluidState`. The spike was almost certainly measuring a cached value rather than a flash. Every downstream decision that assumed the small number is now suspect — `C-12`'s per-solve-cache decision most directly, and `07`'s own interactive budget, since a single Newton iteration on a small circuit costs tens of milliseconds in property evaluation alone (`S-2`). This is not a defect in `07`'s targets; it is a defect in the evidence they were set against, and it stays open until the budget is re-derived from measured numbers. |
| F-7 | [`03`](03-repository-layout.md) | **`global.json` was in the repository, load-bearing, and named by no plan document** | It is not in `03`'s target tree and has no subsection under Build configuration, although CI resolves its SDK through it (`setup-dotnet` with `global-json-file`) and its `rollForward` decides whether two installed SDKs can both claim the repository. That second consequence is the one nothing anywhere recorded: with `latestFeature` on a machine holding 10.0.111 and 10.0.301, the Roslyn tooling registered one band and the shell's `dotnet` resolved the other, both restored into one `ArtifactsPath`, and whichever ran last left an assets cache the other could not open — surfacing as a workspace that loaded with missing references and answered as though nothing were wrong. Narrowed to `latestPatch`, which names one band, and `03` now documents the file and the trade. Open only because the general form is unaddressed: **any tool that loads these projects outside `dotnet build` chooses its own SDK**, and nothing in `plan/` says that a repository's SDK selection is part of its contract with its tooling rather than a local convenience. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| F-14 | [`07`](07-quality-attributes.md) | **The water row's lower endpoint is not a state.** 0 °C is stated as a bound of the validated domain, and 0 °C is the melting line | The same shape as `F-13` at the other end, and missed by `F-13`'s own fix because that fix reasoned about vapour. On a phase boundary pressure and temperature are one constraint, so the backend refuses the pair — and before this it surfaced as `FS2004` "could not evaluate", which reads like a backend failure rather than an ill-posed question. `Water` now detects the melting line the way it already detected the boiling line and reports `FS2002`; `07` says the endpoints are limits, and [`fluid`](../../docs/functions/fluid.md) tells a user to move 0.01 °C off it. Found by a validation test that took the stated bound at its word. |
| F-13 | [`07`](07-quality-attributes.md) | **The water-properties row states a rectangle, and the rectangle contains steam.** 0–120 °C × 100–1000 kPa absolute reads as two independent bounds; they are only independent below the boiling line | Measured: saturation is 99.61 °C at 100 kPa absolute, 120.21 °C at 200 kPa, 179.88 °C at 1000 kPa. At 100 kPa absolute and 110 °C the backend returns `phase=Gas`, ρ = 0.573 kg/m³ — a factor of 1600 from liquid, silently, inside a range the substance calls valid. `07` now says "below the boiling line at that pressure" and `Water` rejects any state whose phase is not liquid, naming the boiling point as the upper bound in `FS2003`. Found by a question about how CoolProp fixes a state, not by reading the row. |
| F-11 | [`01`](01-vision-and-scope.md) | **The distribution header could not produce the flows `05` requires of it.** Both subcircuits declared three components and no `connections`, so there was no path from `supply` to `return` in either; and both declared a heat exchanger of kind `duty`, which does not exist | The same stale kind `L-23` fixed in the syntax tour, still here one document away. Kinds corrected, and `PU_AHU - HE_AHU - TV_AHU` added to each branch — open at both ends, which is what [`23`](../20-core-domain/23-topology-and-graph.md)'s lowering rule needs, since `supply` joins the *first unconnected inlet*. Found by transcribing the fixture into `samples/` and binding it, which is the first thing anything had done to it. |
| F-12 | [`01`](01-vision-and-scope.md), [`15`](../10-language/15-semantic-model.md) | `FS2107` warned that both ends of both headers were dead ends | `N4` and `N6` carry one written connection each and get their second from the subcircuit that attaches to them. `23` lowers `supply N4` to exactly that edge — one stage after step 10 runs — so the binder was counting a degree the model does not finally have. A node any circuit attaches to is now exempt. The narrower fix, synthesising the connection in the binder, was rejected: `SemanticModel.Connections` is "every connection, in source order", and `FS1507`/`FS1511` are deliberately judged on the connections the **user wrote**. |
| F-5 | [`08`](08-implementation-sequence.md) | The P2.1 row promised a shared stage-result type that nothing wanted | Recorded in `08` itself: what is genuinely shared is `Diagnostic` and the registry behind it. A generic `StageResult<T>` would have renamed `Root` to `Value` for no consumer. |
| F-6 | [`08`](08-implementation-sequence.md) | **The formatter had no work package at all.** `17` owns it and `52` binds it to `Shift+Alt+F`, but `08` scheduled only the printer (P2.5) | Assigned to P5.5, with the editor command that invokes it rather than beside the printer it must not become. Found in P2.5 only because the phase's scope was checked against the owning document. |
| F-7 | [`08`](08-implementation-sequence.md) | Invariant 3 — the unit tier under two seconds — was being violated at 3.1 s, and had been for some packages | Not the tests: 2.4 s of it was `RepositoryLayout` walking the whole repository, `node_modules` included, five times, filtering build output *after* enumerating it. Pruned during the walk and walked once: 1.0 s. |
| F-8 | [`06`](06-decision-log.md) | — | Six decisions were needed during P2.2–P2.6 (`D-51`…`D-56`), every one from cross-checking documents *before* writing the code they governed. Not a defect in `06`; recorded because the rate is information. |
| F-9 | [`05`](05-milestones-and-acceptance.md), [`01`](01-vision-and-scope.md) | The last four M1 exit criteria and the nine-diagnostic count were unasserted | P2.8 closed all five: `supply`/`return` bind and resolve a `ParentCircuit`, the `control` line binds its four named arguments (and rejects a bare `actuate=TV1` per `D-43`), a malformed line leaves every other statement bound, and `samples/m1-syntax-reference.fluid` produces exactly one `FS1507`, two `FS2107` and six `FS1510` — asserted from the sample file, not from a copy. |
| F-10 | [`08`](08-implementation-sequence.md) | **[`18-script-compatibility`](../10-language/18-script-compatibility.md) had no work package anywhere in the sequence** | The second occurrence of `F-6`'s defect, and found the same way: by checking a milestone's criteria against the packages meant to deliver them. M1's *first* exit criterion needs `FS1701`, and no row mentioned the document. `18` does not fit one package, so it was split — P2.9 ships `Inspect` and the gate, P3.5 owns `FS1703`, P5.9 owns the save-blocking half of `FS1701`, and migration is deferred with a stated trigger (a language major 2) rather than left unscheduled. |

## Observations

**The invariants in `08` are only worth what measures them.** F-7 sat unnoticed because nothing
checked invariant 3 at a package boundary; it was found by measuring at one. Invariant 4 (no P2
package references `Core.Fluids`, `.Components` or `.Solvers`) *is* asserted, by an architecture test,
and has never drifted. The difference between those two outcomes is the test.

**`ArtifactsPath` and MSBuild node reuse both need environment setup that no document owns.** Build
output lands in `~/.dotnet-artifacts`, and `MSBUILDDISABLENODEREUSE=1` is set in the Claude Code
settings because leftover worker nodes on WSL hold file locks. Neither belongs in `03`, but a session
that has not seen it will be confused by both.

**Measuring on WSL over `/mnt/c` distorts every timing.** A full-repository directory walk costs about
half a second there and nothing on a native filesystem. Any performance number in this repository
should say which it was measured on.

**The docs gate is enumerated from code, so a phase pays for it the moment it registers anything.**
P2.6 registered eight component kinds and immediately owed eight pages; it also owed thirteen more,
because the reserved-word half of the gate had been left dead since P2.4. A gate wired but returning
an empty collection is indistinguishable from a gate that passes.

**Invariant 3 held through P2.8 without being re-fixed.** The unit tier is 1.9 s over 551 tests after
adding three binder files and thirty-three tests, which says F-7's fix was the enumeration and not the
test count. It is still only measured by running it; see the observation above.

**Two documents have now turned out to be unscheduled, and both were found by the same question.**
`F-6` (the formatter) and `F-10` (script compatibility) were each caught by asking "which package
delivers this milestone criterion?" rather than by reading `08` for completeness. `08`'s package
tables are a good plan and a poor inventory: nothing in the document's own structure notices that a
tier-10 file is named by no row. A check that every `plan/` document is named by at least one work
package would be cheap, and would have found both.

**Adding `D-63` exercised the decision log's own process, and the process held.** `23` introduced flow
groups and assigned their declaration to the component; `22` owns the component contract and never
mentioned them. `06`'s rule — a change that alters a settled decision adds a new entry rather than
editing the old one — made the right call obvious, and the entry's mandatory "rejected alternatives"
section is what surfaced that a registry-data version could not work, because a tank's flow group holds
every materialized port and how many there are comes from the script. That argument did not exist
before the section demanded it.

**The `defects.md` rule was followed for three tiers and silently skipped for a fourth.** `P3.0`
through `P3.4a` appended to `20-core-domain` every time and to `10-language` when the binder was
touched, and never created `30-solver/defects.md` at all — although every one of those packages was
written against `31`'s and `36`'s contracts. The rule in `08` says "every `plan/` tier whose documents
it worked against", and the failure mode is specific: a tier with *no* defects file reads as a tier
nothing has touched, which is exactly the wrong signal. A tier's file should be created empty when the
tier is first read at implementation depth, not when it first has a defect.

