# tank

A finite-volume liquid store. In steady state it is a mixed junction; in a transient it is a stack of
equal-volume, perfectly mixed layers, indexed from the bottom up.

```fluidscript
T1 tank volume=500 dm3 layers=8
```

## Ports

Indexed: `in1`…`in16` and `out1`…`out16`, all bidirectional. `in1` and `out1` always exist; the
higher ones appear when a connection names them or an elevation parameter mentions them. With several
ports, name them explicitly:

```fluidscript
fluidscript 1
connections
T1.in2 - N4
T1.out1 - N5
```

Which way fluid actually moves through a port is decided by the solve — an `in` port with reverse flow
draws from its layer.

## Parameters

| Parameter | A bare number means | Meaning | If you omit it |
|---|---|---|---|
| `volume` | dm³ | Total liquid volume | 300 dm³, a domestic buffer vessel |
| `layers` | — | Equal-volume layers, bottom to top | 5 |
| `t` | °C | One initial temperature for every layer | The mixed steady solution |
| `t1`…`tN` | °C | The complete bottom-to-top initial profile | As above |
| `in1_elevation`…`in16_elevation` | — | Normalized inlet height, 0 at the bottom and 1 at the top | 0.5, mid height |
| `out1_elevation`…`out16_elevation` | — | Normalized outlet height | 0.5, mid height |

**`t` and the indexed `t1`…`tN` are mutually exclusive**, and if you use the indexed form you must
state every layer. Half a profile is an error rather than a guess — the layers you left out have no
value, and no default that would not be an invention. Either mistake is
[`FS2113`](diagnostics.md), and it counts against the `layers` you stated, or against the five you
get by default if you stated none.

A port's elevation picks its layer by `min(floor(elevation × layers) + 1, layers)`: 0 is layer 1, 30%
of a five-layer tank is layer 2, and 1 is the top layer.

### What is checked

| If you write | You get |
|---|---|
| `t` beside any `t1`…`tN`, or only some of them | [`FS2113`](diagnostics.md) |
| `layers` fractional, below 1, or above 100 | [`FS2114`](diagnostics.md) |
| An elevation below 0 or above 1 | [`FS2115`](diagnostics.md) |

0 and 1 are both inside the range: a port sitting on the floor or at the very top is an ordinary
design, not an edge case.

## Properties

`volume`, `layers`, `stored_energy`, `t1`…`tN`, and `inN_t` / `outN_t` for every port that exists.

`tN` is the solved temperature of layer N, counted from the bottom, and `inN_t` / `outN_t` are the
temperatures at the ports. All three are readable in any expression — `let top = T1.t5` — and are
available after the solve. Reading a layer above the tank's `layers` is not an error at bind time;
you get no value for it.

## Also written as

`container`, and `v` for `volume`. Both are kept exactly as you wrote them; nothing rewrites `v` into
`volume` behind your back.

## Tag

`S` — a tank in circuit 400 is tagged `400S01`.

## See also

[`node`](node.md) · [`schedule`](schedule.md) · [Units](units.md)
