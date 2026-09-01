# FluidScript

Describe a hydronic system by writing it down, and see it sized, solved and drawn as a live P&I
diagram beside the text.

## What it does

- **You write the plant, not the numbers.** Every component parameter is optional: leave `head` off a
  pump and it is sized from the loop it sits in; state it and it becomes a constraint, never a seed.
- **The physics is real.** CoolProp-grade fluid properties and a genuine circuit solver, with every
  result carrying the basis it was computed from.
- **The diagram is the other half of the editor.** Change a number in the script and it redraws;
  change a valve on the diagram and the script updates — byte for byte, comments intact.

## Status

**M0 — scaffold.** Honest summary of what exists today:

| | |
|---|---|
| Builds, tests, CI scaffolding | working |
| Architecture guard rails | working, and deliberately ahead of the code they guard |
| The script language | not started — M1 |
| Sizing and the steady-state solve | not started — M2 |
| Editor, canvas, API | not started — M3 |

There is no script to run yet. The plan being implemented is in [`plan/`](plan/), and the order the
work happens in is [`plan/00-foundation/08-implementation-sequence.md`](plan/00-foundation/08-implementation-sequence.md).

## Building it

```bash
dotnet build                              # zero warnings, or it fails
dotnet test                               # everything
dotnet test --filter-trait Category=Unit  # the fast tier, under 2 s

cd frontend && npm install && npm run dev # Vite dev server on :5173
dotnet run --project src/FluidScript.Api  # API host on :5080
```

`.NET 10 SDK` and `Node 22` or newer.

## How it works

```
script (.fluid) → lex → parse → bind → lower → size → solve → model contract → canvas
```

Everything left of the model contract is `FluidScript.Core`, which has no UI and no hosting
dependency and is usable as a library. Everything right of it computes geometry and formats numbers,
and never physics.

## Documentation

`/docs` ships with the features it documents — Tutorial, Advanced Workflows, and Functions. It is
empty today because no user-visible feature has shipped yet; a feature without its page is
incomplete, and CI enforces that rather than trusting it.

## Contributing

[`CONTRIBUTING.md`](CONTRIBUTING.md). The one rule worth stating twice: a change that alters a
documented decision adds a new entry to
[`plan/00-foundation/06-decision-log.md`](plan/00-foundation/06-decision-log.md) rather than editing
the old one.

## Security

Report vulnerabilities through [`SECURITY.md`](SECURITY.md), never a public issue.

## Licence

MIT — see [`LICENSE`](LICENSE). The component catalogue is factual dimension data gathered from
public sources and is separately attributed in `SOURCES.md`.
