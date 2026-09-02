# pipe

A pressure drop between two nodes, optionally discretized so that transport delay can be seen.

```fluidscript
P1 pipe length=45 dn=50
P2 pipe length=12 dn=32 elevation=-3
```

## Ports

`in` and `out`. Neither is optional.

## Parameters

| Parameter | A bare number means | Meaning | If you omit it |
|---|---|---|---|
| `length` | m | Length along the run | Sized |
| `dn` | — | Nominal-diameter **designation**, not a diameter. DN25 steel pipe has a 27.3 mm bore | Sized from velocity |
| `roughness` | m | Absolute wall roughness. Usually written `roughness=0.045 mm` | 0.045 mm, commercial steel |
| `nodes` | — | Internal discretization count. Transport storage is opt-in: set it to 1 or more to resolve travel time along the pipe | 0 — no internal nodes |
| `elevation` | m | Outlet height minus inlet height | 0 m, no elevation stated |
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

## Also written as

`tube`.

## See also

[`node`](node.md) · [Units](units.md)
