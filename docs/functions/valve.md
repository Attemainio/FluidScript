# valve

A controllable resistance between two points.

```fluidscript
V1 valve kv=6.3
V2 valve authority=0.5 characteristic=equal_percentage
```

## Ports

`in` and `out`.

## Parameters

| Parameter | A bare number means | Meaning | If you omit it |
|---|---|---|---|
| `kv` | m³/h at 1 bar | Flow coefficient | Sized from the design drop |
| `position` | — | Opening, 0 to 1 | Sized, or driven by a controller |
| `characteristic` | — | `linear`, `equal_percentage` or `quick_open` | `equal_percentage` |
| `authority` | — | Target authority for sizing | Sized |
| `dp` | kPa | Design pressure drop, an alternative to `kv` | Sized |

`kv` is defined as m³/h of water at 1 bar differential, so a bare `kv=6.3` is in those units and
nothing else.

### What is checked

| If you write | You get |
|---|---|
| `position` below 0 or above 1 | [`FS2105`](diagnostics.md) |
| Both `kv` and `dp` | [`FS2103`](diagnostics.md), a warning: the `kv` is used |

`kv` and `dp` do not contradict each other — the drop a valve makes follows from its `kv` and the
flow through it — so stating both is a design intention written beside its own consequence. The
solve reports the drop it actually produces, which is how you find out whether the two agreed.

## Properties

`kv`, `dp`, `position`, `authority`, `flow`.

## Also written as

`control_valve`, `balancing_valve`, `two_way_valve`, `2_way_valve`.

## Tag

`V` — a valve in circuit 400 is tagged `400V01`.

## See also

[`three_way_valve`](three-way-valve.md) · [`control`](control.md)
