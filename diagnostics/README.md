# diagnostics

Measurement reports. **Nothing in here is committed except this file**, because every number in it is
bound to the machine, the filesystem and the build configuration that produced it — and committing a
timing invites it to be quoted without them.

## What writes here

| Report | Written by | Answers |
|---|---|---|
| `fluid-state-timings.md` | `StateTimingDiagnostics` | What it costs to fix a fluid state, per substance and per property pair — cold call, median, mean, standard deviation, min and max over 10 samples of 20 calls |

## Running them

```bash
dotnet test --filter-trait Category=Diagnostic                 # all of them
dotnet test --filter-class FluidScript.Core.Tests.Performance.StateTimingDiagnostics
```

They are traited `Diagnostic` so the unit tier stays under `08`'s two-second invariant. They are
ordinary tests otherwise, and `dotnet test` with no filter runs them.

Add `-c Release` for a number worth quoting. A debug build is materially slower, and the report says
so in its header rather than leaving you to remember.

## What these are not

**They assert no budget.** A wall-clock threshold fails on a loaded CI machine and passes on a fast
one while measuring neither. The reason to have them is comparative: the ratio between a real backend
call and a fake one is what justifies `ISubstance` existing, and the ratio between a property call and
a Newton iteration's call count is what decides whether `21`'s per-solve cache is worth building.
Absolute numbers are for the header, not the argument.

**They are not stable across environments.** `plan/00-foundation/defects.md` records that measuring on
WSL over `/mnt/c` distorts timings; a report generated there and one from a native filesystem are not
comparable, which is why the report names the OS.
