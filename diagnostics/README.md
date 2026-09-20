# diagnostics

Measurement reports. **Nothing in here is committed except this file**, because every number in it is
bound to the machine, the filesystem and the build configuration that produced it — and committing a
timing invites it to be quoted without them.

## What writes here

| Report | Written by | Answers |
|---|---|---|
| `fluid-state-timings.md` | `StateTimingDiagnostics` | What it costs to fix a fluid state through `ISubstance`, per substance and per property pair — cold call, median, mean, standard deviation, min and max over 10 samples of 20 calls |
| `backend-pair-matrix.md` | `BackendPairDiagnostics` | Which of the ten (T, p, h, s, d) input pairs each fluid *family* supports — pure, pseudo-pure, incompressible substance, incompressible solution, HEOS mixture and humid air — the backend's refusal message for the rest, and what each supported pair costs |
| `backend-pair-log.md` | `BackendPairDiagnostics` | Its running log, appended and flushed before and after every call |
| `circuit-reports.md` | `CircuitDiagnostics` | What a circuit actually is, per script: the counting table, the hydraulic partition, what answers each constraint, the Newton trajectory iteration by iteration, every unknown seeded (with its seed basis) against solved, every node in °C and kPa and every branch against its written direction, the heat balance per hydraulic, each pump's and valve's operating point, every residual, what sizing chose, exchanger ratings, rank and conditioning at the solution and at the seed, and the iterations of each sizing pass. Covers `samples/` plus anything dropped in `scratch/` |
| `solver-scale.md` | `SolverScaleDiagnostics` | How the solver scales with unknown count — the distribution header with 2, 8, 15, 30 and 61 pumped consumers (35 to 861 unknowns): prepare and solve time, iterations, passes, managed allocation and working set. `FLUIDSCRIPT_SCALE_SIZES=2,8` picks the sizes; run a suspect size under `DOTNET_GCHeapHardLimit` |
| `memory-footprint.md` | `MemoryFootprintDiagnostics` | Where the memory goes per sample — managed allocation for bind, prepare and solve, what the process still holds after ten repeated solves and that as a per-solve slope, working set against GC heap. A `held` far above `allocated` is native memory (`C-76`); the test fails above 8 MB kept per solve |
| `pipeline-timings.md` | `PipelineTimingDiagnostics` | Where the time goes between a script and a solved circuit — parse, bind, lower and solve per sample, and inside one Newton step the cost of a residual evaluation, the `N+1` of them a finite-difference Jacobian needs, and the dense LU of the same order |

## Running them

```bash
dotnet test --filter-trait Category=Diagnostic                 # all of them
dotnet test --filter-class FluidScript.Core.Tests.Performance.StateTimingDiagnostics
```

They are traited `Diagnostic` so the unit tier stays under `08`'s two-second invariant. They are
ordinary tests otherwise, and `dotnet test` with no filter runs them.

**A test whose memory is in doubt runs under a cap**, so that a runaway fails in-process with a
stack trace instead of the kernel killing whatever launched it (`C-76` took 31 GB and three agent
sessions before anyone saw a number): `DOTNET_GCHeapHardLimit=0x40000000 timeout 120 <binary> -filter ...`.
The cap is on the managed heap; native growth shows as a working set far above it, which is the
signature to look for.

Add `-c Release` for a number worth quoting. A debug build is materially slower, and the report says
so in its header rather than leaving you to remember.

## Diagnosing a variant

`CircuitDiagnostics` reports on every script in `samples/` **and on every `.fluid` file in
`diagnostics/scratch/`**. That folder is the point of it: what a diagnosis usually needs is not a corpus
sample but *this script with one line changed* — a stated outlet, an added datum, a different plant
arrangement — and comparing the two reports side by side.

```bash
cp samples/m2-distribution-header.fluid diagnostics/scratch/variant.fluid
# edit the one line
dotnet test --filter-class FluidScript.Core.Tests.Performance.CircuitDiagnostics
```

**This replaces writing a throwaway test to print the number you want.** `SolveExplanation` already
answered every question these reports answer and its only caller was a test asserting the sections
exist, so diagnosing anything meant a scratch `.cs` file, a build, a deliberate `Assert.Fail` to get the
text out, and a delete. Four of those were written in one session before this existed.

## When one never finishes

`BackendPairDiagnostics` writes `backend-pair-log.md` as it goes, one line before each call and one
after, flushed every time. That is not tidiness: **some CoolProp flashes never return.** `(h, s)` on a
water-ethanol mixture iterates without converging and without a limit of its own, and a run that never
finishes writes no report — the log is the only thing that names the call it is stuck in.

Each call now runs under a five-second cut-off on a background thread. The call itself cannot be
cancelled, being native with no token to pass it; what the cut-off does is stop *waiting*, record the
timeout, and move on.

If a run does hang anyway, read the log: **a line with no result under it is the culprit.**

To kill one, match the apphost by name:

```bash
pkill -9 -f 'FluidScript\.Core\.Tests'      # the test assembly runs as an apphost
pgrep -af 'FluidScript|dotnet test'          # verify: no output means clear
```

**Not `testhost`, and not `*.dll`.** Microsoft.Testing.Platform runs the assembly as an apphost called
`FluidScript.Core.Tests` — there is no `testhost` process and no `.dll` in the command line, so both
of the patterns a VSTest habit reaches for match nothing and report success. One stuck run survived
two such kills and burned a core for **7 h 53 m** before anyone looked at `ps` rather than at the exit
code. That run is also the proof behind the note above: `(h, s)` on a mixture does not return, in the
literal sense.

## What these are not

**They assert no budget.** A wall-clock threshold fails on a loaded CI machine and passes on a fast
one while measuring neither. The reason to have them is comparative: the ratio between a real backend
call and a fake one is what justifies `ISubstance` existing, and the ratio between a property call and
a Newton iteration's call count is what decides whether `21`'s per-solve cache is worth building.
Absolute numbers are for the header, not the argument.

**They are not stable across environments.** `plan/00-foundation/defects.md` records that measuring on
WSL over `/mnt/c` distorts timings; a report generated there and one from a native filesystem are not
comparable, which is why the report names the OS.
