# pipe

A pressure drop between two nodes, optionally discretized so that transport delay can be seen.

```fluidscript
circuit "Loop":
  fluid = water
  PU1  pump
  HE1  load  power = 30  dt = 20
  P2   pipe  length = 12  dn = 32  minor_loss = 2.5

  PU1 - HE1 - P2 - N4
  N4 - PU1   45 m  DN50
```

## On the connection line

The line on the drawing already is the pipe, so its properties go on the connection that draws it.
Write them after the last name on the line: a length with its unit and a `DN` designation, each
recognised by its form and needing no name, then any other pipe parameter as `name = value`:

```fluidscript
circuit "Loop":
  fluid = water
  PU1  pump
  HE1  load  power = 30  dt = 20
  CV1  valve

  N1 - PU1 - N2 - HE1 - N3 - CV1 - N5
  N5 - N1   25 m
  N3 - N6   12 m  DN25  roughness = 0.05 mm
  N6 - N1
```

- A line that carries pipe properties has **one link**. On a chain, `N1 - N2 - N3  DN25`, it is
  [`FS1803`](diagnostics.md), which asks which link you meant: put the pipe on a line of its own.
- The pipe is named after its ends, `N5__N1` here, and answers to that name in results and
  expressions (`N5__N1.dp`). Between two components rather than nodes, `HE1 - PU1  DN25`, the nodes
  put beside the pipe are `HE1__PU1__in` and `HE1__PU1__out`.
- A `length` you do not write is **zero**: `DN25` alone sets the drawing and the bore and drops
  nothing until you give the run a length. A length without a `DN` is sized like a declared pipe's.
- A line with no properties is a joint that drops nothing.

A declared `P2 pipe ...` is a pipe too, and the drawing shows both the same way. Declare one when you
want to name it yourself, or to put it in a chain: `PU1 - P2 - HE1` carries a pipe in the middle of a
line, where properties written after the last name could not say which link they belong to.

## Ports

`in` and `out`. Neither is optional.

## Parameters

| Parameter | A bare number means | Meaning | If you omit it |
|---|---|---|---|
| `length` | m | Length along the run | Sized for a declared pipe; **0** for one written on the connection line |
| `dn` | — | Nominal-diameter **designation**, not a diameter. DN25 steel pipe has a 27.3 mm bore. On a connection line it is written `DN25` | Sized from velocity |
| `material` | — | The catalogue `dn` is read in: `steel_en10255`, `steel_en10220` or `copper_en1057` ([`catalog`](catalog.md)) | The project's `catalog` setting, or the shipped default |
| `roughness` | m | Absolute wall roughness. Written when the pipe is not new, `roughness = 0.3 mm` | New pipe of the pipe's series: 0.045 mm for steel, 0.0015 mm for drawn copper ([`catalog`](catalog.md)) |
| `nodes` | — | Internal discretization count. Transport storage is opt-in: set it to 1 or more to resolve travel time along the pipe. Each cell holds its share of the pipe's volume and is a state a run integrates; more cells give a sharper front and a shorter allowed step ([Discretized pipes and the run in time](../advanced/discretized-pipes.md)) | 0 — no internal nodes |
| `minor_loss` | — | Sum of fitting loss coefficients K | 0, no fittings stated |

## Properties

| Property | Unit | Available |
|---|---|---|
| `dp` | kPa | after the solve |
| `velocity` | m/s | after the solve |
| `re` | — | after the solve |
| `dn` | — | after sizing |
| `diameter` | m | after sizing |
| `flow` | kg/s | after the solve |
| `volume` | m³ | after sizing |

**`dn` and `diameter` are different things.** `dn` reads back the designation; `diameter` reads back
the catalogue inside diameter in metres, and it is the one to use in an expression.

## Height

A pipe has no `elevation` of its own: it is the one thing that runs between two heights, so its rise
is the difference between what it connects. Put the height on the equipment — the air handling unit
on the roof, the pump in the plant room — and every pipe between them climbs or falls by the
difference:

```fluidscript
circuit "Roof AHU":
  fluid = water
  PU1     pump
  HE_AHU  load  power = 24  in.t = 50  out.t = 30  elevation = 32
  P1      pipe  length = 35  dn = 25
  P2      pipe  length = 35  dn = 25

  N1 - PU1 - N2 - P1 - HE_AHU - P2 - N1
```

`P1` rises 32 m and `P2` falls 32 m, the two hydrostatic terms cancel round the loop, and the pump
sees friction alone — which is what makes stating heights safe: there is no way to write a riser
without its return. The water gives up 314 J/kg on the way up and gets it back on the way down; its
temperature does not change, because the loss is pressure, not heat. See
[`node`](node.md#height) for how a height spreads to everything wired to it.

## Also written as

`tube`.

## See also

[`node`](node.md) · [`circuit`](circuit.md) · [Units](units.md)
