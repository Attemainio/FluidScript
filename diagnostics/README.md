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

## Running them

```bash
dotnet test --filter-trait Category=Diagnostic                 # all of them
dotnet test --filter-class FluidScript.Core.Tests.Performance.StateTimingDiagnostics
```

They are traited `Diagnostic` so the unit tier stays under `08`'s two-second invariant. They are
ordinary tests otherwise, and `dotnet test` with no filter runs them.

Add `-c Release` for a number worth quoting. A debug build is materially slower, and the report says
so in its header rather than leaving you to remember.

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
