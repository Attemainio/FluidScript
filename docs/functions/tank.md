# tank

A finite-volume liquid store. In steady state it is a mixed junction; in a transient it is a stack of
equal-volume, perfectly mixed layers, indexed from the bottom up.

```fluidscript
T1 tank volume=500 dm3 layers=8
```

## Ports

Indexed: `in`, `in[2]`…`in[16]` and `out`, `out[2]`…`out[16]`, all bidirectional. `in` and `out`
always exist (`in[1]` is another way of writing `in`); the higher ones appear when a connection names
them or a level parameter mentions them. With several ports, name them explicitly:

```fluidscript
fluidscript 1
connections
T1.in[2] - N4
T1.out - N5
```

Which way fluid actually moves through a port is decided by the solve — an `in` port with reverse flow
draws from its layer.

## Parameters

| Parameter | A bare number means | Meaning | If you omit it |
|---|---|---|---|
| `volume` | dm³ | Total liquid volume | 300 dm³, a domestic buffer vessel |
| `layers` | — | Equal-volume layers, bottom to top | 5 |
| `t` | °C | One initial temperature for every layer | The mixed steady solution |
| `layer[1].t`…`layer[N].t` | °C | The complete bottom-to-top initial profile | As above |
| `in.level`, `in[2].level`…`in[16].level` | — | Normalized inlet height, 0 at the bottom and 1 at the top | 0.5, mid height |
| `out.level`, `out[2].level`…`out[16].level` | — | Normalized outlet height | 0.5, mid height |
| `elevation` | m | Height above the project datum, for the vessel and every port on it; see [`node`](node.md#height) | Wherever it is wired to, else 0 m |

A port's level is written on the port, the way every port state is ([syntax](syntax.md#a-ports-state)):
`in[3].level=0.9` places the third inlet near the top. The old `in3_level=` and `t3=` spellings still
bind and are pointed at the new one ([`FS1536`](diagnostics.md)).

**`t` and the indexed `layer[1].t`…`layer[N].t` are mutually exclusive**, and if you use the indexed form you must
state every layer. Half a profile is an error rather than a guess — the layers you left out have no
value, and no default that would not be an invention. Either mistake is
[`FS2113`](diagnostics.md), and it counts against the `layers` you stated, or against the five you
get by default if you stated none.

A port's level picks its layer by `min(floor(level × layers) + 1, layers)`: 0 is layer 1, 30% of a
five-layer tank is layer 2, and 1 is the top layer. A level is a fraction of the vessel, not metres:
it chooses which layer the port talks to and forms no pressure. The vessel's one `elevation` is where
every port sits hydraulically; the pipes reaching the tank carry the rise to and from it.

### What is checked

| If you write | You get |
|---|---|
| `t` beside any `layer[N].t`, or only some of them | [`FS2113`](diagnostics.md) |
| `layers` fractional, below 1, or above 100 | [`FS2114`](diagnostics.md) |
| A level below 0 or above 1 | [`FS2115`](diagnostics.md) |

0 and 1 are both inside the range: a port sitting on the floor or at the very top is an ordinary
design, not an edge case.

## Properties

`volume`, `layers`, `stored_energy`, `layer[1].t`…`layer[N].t`, and `in[N].t` / `out[N].t` for
every port that exists.

`layer[N].t` is the solved temperature of layer N, counted from the bottom, and `in[N].t` /
`out[N].t` are the temperatures at the ports. All three are readable in any expression —
`let top = T1.layer[5].t`, `let supply = T1.out.t` — and are
available after the solve. Reading a layer above the tank's `layers` is not an error at bind time;
you get no value for it.

## Also written as

`container`, and `v` for `volume`. Both are kept exactly as you wrote them; nothing rewrites `v` into
`volume` behind your back.

## Tag

`S` — a tank in circuit 400 is tagged `400S01`.

## In a run

Each layer is a state the solver integrates, and a port delivers the layer its level picks rather
than a mixed average. After every step the layers are put back in density order if the flow left them
inverted, pooling only the smallest block that is out of order and conserving its energy exactly. A
stated profile is a starting disturbance, so the vessel evolves from t = 0 with nothing in the
schedule. A layer stated at a temperature the fluid does not reach at the vessel's pressure is
[`FS3108`](diagnostics.md), naming the tank and the layer.

→ [Stratified storage](../advanced/stratified-storage.md)

## See also

[`node`](node.md) · [`schedule`](schedule.md) · [Units](units.md)
