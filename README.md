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

**The language works. The hydraulic core is most of the way there. Nothing solves yet.**

| | |
|---|---|
| Lex, parse, bind, print, diagnose | working — `Print(Parse(x))` is byte-identical, and tested that way |
| Units, dimensions, quantities | working — `20 °C + 30 °C` is an error, not 596 K |
| Fluid properties | working — one SharpProp adapter, two fakes, validated against published values |
| Components and their residuals | working — six kinds, allocation-free evaluation |
| Lowering, the graph, well-posedness | working — a script becomes a circuit and is told whether it is square |
| Pipe catalogue | working — EN 10255 steel verified against two public sources per row; EN 1057 copper ships **refused** until it is |
| The solver | in progress — the tolerance table, the state vector's layout and its scaling. No solve yet |
| Sizing | not started — next after the solver |
| Editor, canvas, API | scaffold only |

So a script parses, binds, lowers to a circuit and is checked for a unique answer — and then stops,
because the thing that would compute one is being built. The seven scripts in [`samples/`](samples/)
go that far today.

The plan being implemented is in [`plan/`](plan/); the order the work happens in is
[`08-implementation-sequence`](plan/00-foundation/08-implementation-sequence.md), and what
implementing it actually found is in each tier's `defects.md`.

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
                  ╰──────── working ────────╯   ╰── being built ──╯   ╰─ scaffold ─╯
```

Everything left of the model contract is `FluidScript.Core`, which has no UI and no hosting
dependency and is usable as a library. Everything right of it computes geometry and formats numbers,
and never physics.

Two rules shape the inside of that pipeline more than anything else. **Nothing throws on user
input** — a script under editing is malformed most of the time, so malformed input is a return value
and every stage carries its diagnostics. And **everything is SI internally**; canonical units exist
only at the language boundary and on the wire.

## Documentation

[`docs/`](docs/) ships with the features it documents — Functions, Advanced Workflows, and a
generated reference for every diagnostic code, unit and catalogue row. A feature without its page is
incomplete, and CI enforces that rather than trusting it: the gate covers every component kind, every
statement-introducing reserved word, and every registered code.

## Contributing

[`CONTRIBUTING.md`](CONTRIBUTING.md). The one rule worth stating twice: a change that alters a
documented decision adds a new entry to
[`plan/00-foundation/06-decision-log.md`](plan/00-foundation/06-decision-log.md) rather than editing
the old one.

## Security

Report vulnerabilities through [`SECURITY.md`](SECURITY.md), never a public issue.

## Licence

MIT — see [`LICENSE`](LICENSE). The component catalogue is factual dimension data gathered from
public sources, with two independent sources recorded per row in
[`src/FluidScript.Core/Catalogs/SOURCES.md`](src/FluidScript.Core/Catalogs/SOURCES.md). No standard's
text or tables are reproduced.
