---
id: 60-docs-and-devex-defects
title: What implementing against the docs and devex tier found
tier: 60-docs-and-devex
owns: [defect and observation record for documents 61-63]
---

# What implementing against the docs and devex tier found

Defects, deferrals and observations from implementing against `61`–`63`. The rule and its reasoning
are in [`08-implementation-sequence`](../08-implementation-sequence.md).

**`61` and `62` have been implemented against since M0**; `63` only through the architecture tests
that assert its table. Like [`30-solver/defects.md`](../30-solver/defects.md), this file was created
late — the findings below were made across P2.6 through P3.4a and recorded, where they were recorded
at all, in the tier of the code rather than the tier of the document.

**Next id: `T-6`.** The columns, their vocabularies, and the rule for filing, reopening and closing
are in [`08`](../08-implementation-sequence.md) under *Every package writes down what it found*.

## Open

| # | Effort | Risk | Basis | Document | What | Why it is still open |
|---|---|---|---|---|---|---|
| T-2 | medium | low | measured | [`61`](61-documentation-plan.md) | **The docs gate compares a page against its generator, not against the registry, so an incomplete generator passes green** | `P3.3` registered a tank's indexed property families and the generated properties page did not change, because `RegistryPages.RenderProperties` walked `kind.Properties` and knew nothing about `kind.IndexedPropertyFamilies`. The page then said a tank has three readable properties when it has three plus one per layer and one per port, and the gate was green throughout — it asserts the file matches what the code generates, which was true and useless. The gate's shape is right for drift and blind to omission. What would catch it is asserting the generated region covers every *name a reference can resolve*, which is a different question from every name a dictionary holds. |
| T-3 | medium | med | hunch | [`61`](61-documentation-plan.md) | **The gate knows about kinds, reserved words and codes, and a feature is none of those** | `P3.3` shipped eight diagnostics and four "What is checked" sections on component pages. The gate enforced the first set — every code has a row — and could not see the second: a parameter gaining a hard bound is a user-visible behaviour change with no registry entry to hang a gate on. The four sections were written by hand and would have been forgotten silently. This is the limit of a mechanical gate rather than a defect in it, but `61` presents the gate as *the* enforcement of "every feature ships with its page", and it enforces a proper subset. |

## Traps

What a session working against these documents gets wrong first. Promoted from *Observations*
or from a closed entry when a session hits it a second time; read before working in this tier.

_None promoted yet._

## Closed

| # | Effort | Document | What was wrong | What changed |
|---|---|---|---|---|
| T-5 | — | [`63`](63-ci-and-repo-hygiene.md) | **The frontend's log feature was never committed: the Visual Studio `.gitignore` template's `[Ll]og/` swallowed `frontend/src/features/log/`** | What the ignore file meant: keep build output (`bin/`, `obj/`, `log/`) out of the tree, the way the Visual Studio template does. What it did: `[Ll]og/` matches any directory of that name anywhere, so the console-log feature `P5.4` added under `frontend/src/features/log/` -- `LogPane.tsx`, `logModel.ts`, its test -- was silently untracked from 2026-09-14 to 2026-09-19 while `Shell.tsx` imported it. A clean checkout does not build the frontend; `ci.yml` runs `npm run build` there, so CI has been red on every push since, or has not run. Found by accident: a `git diff --stat` after editing `LogPane.tsx` did not list it. | **Closed 2026-09-19** in the same change. `!frontend/src/**/log/` after the template's rule, with a comment naming this entry; the three files committed. The trap remains for every other name in that template -- `bin`, `obj`, `out`, `logs`, `debug`, `release` -- as a source folder; `63` should say so, and a check that every file `tsc -b` compiles is tracked would have caught it the day it happened. |
| T-1 | — | [`62`](62-testing-strategy.md) | **A seventh test category exists and the table names six** | **Closed.** `62`'s table gains a `Category=Diagnostic` row with no budget, because the thing it measures *is* its duration, and the text says why that is not a contradiction: a run whose duration is the result cannot also fail when it is slow. It is excluded from every gate deliberately. The consequence is recorded beside it --- **a regression there is invisible to CI by design**, so it is found by reading the reports, and `F-19` is what it looks like when nobody does: a property-call cost 20 000x the figure the latency budget was set against, sitting in `diagnostics/` and contradicting `07`. |
| T-4 | — | [`62`](62-testing-strategy.md), [`22`](../20-core-domain/22-component-model.md) | **`62`'s worked example cannot evaluate the relation it claims to** | It builds `SolveContext.ForSingleComponent(FakeWater.Instance, massFlow: 0.2391)` — a substance and a flow, no port states — and calls `EvaluateResiduals` on a duty exchanger. `22`'s energy relation is `Q̇ = ṁ(h_out − h_in)` over the *solved* port enthalpies, and a context with no port states has no enthalpies to difference. The example's own constructor implied the other reading: a duty from stated terminal temperatures and a `cp`, which is a real relation and is the `FS2101` one. It shipped as `HeatExchanger.ImpliedFlow`, a reported property rather than a residual, and the test asserts both routes and asserts they agree. `SolveContext.ForSingleComponent`, added on the strength of the example, was removed the same day — nothing could use it. Recorded from the other side as `C-19`, because the finding is about both documents. |

## Observations

**Two environment traps that each cost most of a session, recorded here because
[`62`](62-testing-strategy.md) and [`63`](63-ci-and-repo-hygiene.md) describe how the tests and the
build are *meant* to run and neither describes what this one actually does.** Until 2026-09-09 they
lived only in an agent's session-local memory, which meant a second agent, a fresh clone or a human
got neither. This is the authoritative account. A short hook survives in the agent memory as well,
deliberately: the value of these two is that they fire *before* the mistake, and a note nobody loads
until they go looking has already lost. The hook carries the symptom, the one-command proof and the
fix, and nothing else — anything longer there would become a second account and drift from this one.

**`dotnet test` discovers nothing in this WSL2 environment, and the tests are fine.** It exits with
"Zero tests ran, error: 2" in about 80 ms without discovering anything. The built binaries run
normally: `~/.dotnet-artifacts/bin/FluidScript.Core.Tests/debug/FluidScript.Core.Tests` finds and runs
the whole suite in about ten seconds, and `FluidScript.Api.Tests` has its own. Filter with
`-filter "/*/<namespace>/<class>/<method>"`, and strip ANSI with `sed 's/\x1b\[[0-9;]*m//g'` before
grepping. The failure is in the runner wrapper, not in discovery and not in the code — do not spend a
session bisecting the test project. `CLAUDE.md`'s `Commands` block still names `dotnet test` because
that is the documented interface and it works elsewhere.

**Hundreds of `CS0246: The type or namespace name 'Fact' could not be found` means Windows touched
`obj/`, not that a package reference is wrong.** The repository lives on `/mnt/c/`, so WSL `dotnet`
and Windows Visual Studio share one `obj/`. A running — or hung — `devenv.exe` restores the solution
in the background and rewrites `obj/*.nuget.g.props` with a `C:\Users\...\.nuget\packages\` path
that WSL cannot read. Deleting `obj/` is what wakes Visual Studio up, so cleaning makes it worse, and
`dotnet build` can succeed while `dotnet test` fails because the two race it differently.

*Proof before diagnosis:* read `restore.packagesPath` out of
`tests/FluidScript.Core.Tests/obj/project.assets.json`. A `C:\` path proves it is environmental; a
`/home/` path proves it is not, and the cause is elsewhere. `tasklist.exe | grep -i devenv` finds the
culprit.

*The standing fix is `ArtifactsPath=/home/<user>/.dotnet-artifacts`*, which moves the WSL intermediates
out of the repository so the two toolchains stop sharing a directory. It has to be set where the agent
tool's shell will see it — a non-interactive, non-login `-c` shell reads neither `.bashrc` nor
`.profile`, so an export added to a shell profile reaches tool invocations only by inheritance from
the terminal that launched the agent, which is to say usually not at all. `MSBUILDDISABLENODEREUSE=1`
belongs beside it: a plain `dotnet build` otherwise leaves nine Linux MSBuild worker nodes and a
`VBCSCompiler` alive per build, `dotnet build-server shutdown` reports success without reaping them,
and only `pkill -f 'MSBuild.dll.*nodemode:1'` does.


**A runaway test takes the whole WSL instance's memory with it, and the session dies as
collateral** (2026-09-14). `SolverScaleDiagnostics` ended with exit 137 and took Claude Code down;
relaunched detached, the same; in a plain WSL shell, `Killed`. It was recorded here first as an
environment trap and in the agent's memory as a rule never to run the test --- both wrong, and the
kernel log said so: `Out of memory: Killed process (FluidScript.Cor) anon-rss:31511772kB`. The test
process itself had taken all 31 GB, and the OOM killer takes the largest tree, which is whichever
one launched it. The cause was `C-76`, a native-memory leak per property read. What is worth keeping:
**run any test whose memory is in doubt under `DOTNET_GCHeapHardLimit`** (`0x40000000` for 1 GB) and
`timeout` --- the runtime then throws `OutOfMemoryException` inside the process, with a stack, and
the kernel is never involved. Two probes at `n = 2` and `n = 4` under that cap found the leak in ten
minutes, where three sessions had died without a clue. The harness's `FLUIDSCRIPT_SCALE_SIZES` and
its allocation and working-set columns exist for the same reason. The Claude Code issue that was cited
(anthropics/claude-code#84935) describes the symptom, not this cause.

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
