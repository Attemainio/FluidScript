# pipe

A pressure drop between two nodes, optionally discretized so that transport delay can be seen.

```fluidscript
P1 pipe length=45 dn=50
P2 pipe length=12 dn=32 minor_loss=2.5
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
PU1    pump
HE_AHU load power=24 in=50 out=30 elevation=32
P1     pipe length=35 dn=25
P2     pipe length=35 dn=25

connections
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

[`node`](node.md) · [Units](units.md)
