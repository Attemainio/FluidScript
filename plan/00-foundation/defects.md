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
| F-1 | [`05`](05-milestones-and-acceptance.md) | Four M1 exit criteria remain unasserted | P2.7 closed the rest: `let dT = 30 dK` with `out=20C+dT` gives 323.15 K, `power=30`/`30 kW`/`30000 W` are one quantity, the cyclic reference is one diagnostic naming both, three circuits number 100/200/300, and `3WV three_way_valve` binds with zero parameters. What is left needs topology: `supply`/`return` binding, the `control` line's four arguments, recovery leaving a bound model for every unaffected statement, and the nine-diagnostic count. P2.8. |
| F-2 | [`01`](01-vision-and-scope.md) | The nine-diagnostic count on the syntax reference is unasserted | `08` says P2 closes with it. Same dependency as F-1. See also `10-language/defects.md` L-5. |
| F-3 | [`07`](07-quality-attributes.md) | No latency budget is measured | `D-48`/`D-49` budget interactive latency end to end and derive the debounce, and nothing measures any of it yet. The first honest measurement needs a pipeline that binds, so P2.8 at the earliest; `05` ties the real gate to M3. |
| F-4 | [`03`](03-repository-layout.md) | The nine-file list for a component is unexercised | No component exists. P3.3 is the first package that has to satisfy it, and it is the moment to check the list is still right. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| F-5 | [`08`](08-implementation-sequence.md) | The P2.1 row promised a shared stage-result type that nothing wanted | Recorded in `08` itself: what is genuinely shared is `Diagnostic` and the registry behind it. A generic `StageResult<T>` would have renamed `Root` to `Value` for no consumer. |
| F-6 | [`08`](08-implementation-sequence.md) | **The formatter had no work package at all.** `17` owns it and `52` binds it to `Shift+Alt+F`, but `08` scheduled only the printer (P2.5) | Assigned to P5.5, with the editor command that invokes it rather than beside the printer it must not become. Found in P2.5 only because the phase's scope was checked against the owning document. |
| F-7 | [`08`](08-implementation-sequence.md) | Invariant 3 — the unit tier under two seconds — was being violated at 3.1 s, and had been for some packages | Not the tests: 2.4 s of it was `RepositoryLayout` walking the whole repository, `node_modules` included, five times, filtering build output *after* enumerating it. Pruned during the walk and walked once: 1.0 s. |
| F-8 | [`06`](06-decision-log.md) | — | Six decisions were needed during P2.2–P2.6 (`D-51`…`D-56`), every one from cross-checking documents *before* writing the code they governed. Not a defect in `06`; recorded because the rate is information. |

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
