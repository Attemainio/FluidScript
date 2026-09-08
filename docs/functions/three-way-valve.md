# three_way_valve

A valve with three ports, used to mix two streams or to divert one.

```fluidscript
TV1 three_way_valve authority=0.5
```

## Ports

`a` is the common port, `b` the controlled one and `c` the bypass. `c` is optional — a three-way valve
used as a two-way leaves it open.

Leaving `c` open is not a cosmetic choice: the valve then **is** a two-way valve. It has two ports, one
Kv law, and no mixing to describe, so the circuit gets one equation from it rather than three. Which
one you wrote is reported as its mode — `three_way` or `two_way` — and it is read from the topology,
never declared.

**All three are bidirectional, and the arrangement comes from the topology rather than a
declaration.** A diverting valve takes one stream in at `a` and splits it between `b` and `c`; a
mixing valve — the commonest in hydronics — takes two streams in at `b` and `c` and delivers one at
`a`. Both are real, both are written the same way, and the port that carries flow toward the valve at
the design point is its inlet.

Ports are named in a connection with a dot:

```fluidscript
fluidscript 1
connections
N1 - TV1.a
TV1.b - N2
TV1.c - N3
```

## Parameters

The same as a [`valve`](valve.md): `kv`, `position`, `characteristic`, `authority`, `dp`.

`position` means the same in both: **1 is fully open between `a` and `b`**, whichever way the fluid
happens to run.

## How the Kv is chosen

A three-way valve you do not give a `kv` is sized like a [`valve`](valve.md) — for **authority**, from
the same R5 catalogue — but two things about a three-port valve change which numbers go into that.

**It is sized on the leg that varies, not on the flow through it.** A three-port valve is a
constant-flow device: whether it mixes or diverts, the total crossing it does not change, and only the
split does. So the design flow is the **controlled** leg's, which is not the common port's. In the
cooling loop the common port carries 0.239 kg/s round the secondary while the controlled leg draws
0.163 from the primary; sizing on the larger number would size the valve for a flow it never has to
control.

**Which leg that is comes from the circuit, not from the port letters.** Ports take connections in the
order you write them, so `b` is not reliably the controlled one — write `TV1 - N2` before `TV1 - P1`
and it is the recirculation leg. The leg that varies is the one reaching a stated pressure rather than
closing back into the valve's own loop, and that is what the rule looks for.

**Whether the drop is chosen or determined depends on what drives the circuit.** With a pump on the
path whose head you have not stated, the driving pressure is free, the valve's drop is a choice, and
the authority target makes it — rounding **down** as for a two-way valve. With no such pump the
boundary pressures fix the driving pressure, the valve takes whatever the rest of the path leaves, and
the selection rounds **up** instead: at a fixed differential a coefficient below the required one
cannot pass the design flow at any position, so rounding down there would make the design point
unreachable rather than safe. Which of the two was used is written into the reported basis.

```
3WV  kv         4      sized   Kv 4 (R5 preferred numbers) — authority 0.66 at 0.165 l/s, 2.2 kPa
                               — chosen against the leg's own resistance, which a free pump absorbs
```

### The drop it is sized for is not always the drop it runs at

Both legs share one `kv` and one `position`, and their coefficients are complementary — so the
position that satisfies the controlled leg also fixes the bypass leg. On a circuit where the bypass leg
is what a pump has to push against, the valve can end up dropping far more than it was sized for. The
sizing is still pointing the right way: a larger `kv` lowers the head the pump needs. But read the
solved drop rather than the design one when the pump head looks high, and remember that one catalogue
step is 1.6× in Kv and 2.56× in drop.

### The bypass leg usually needs a balancing valve

Standard practice for this arrangement is a balancing valve in the bypass leg, set so that with the
valve in the bypass position the drop is similar to the path it bypasses. Without one the bypass is a
short circuit: the supply-to-return differential falls and other consumers on the same pair can be
starved. Nothing here sizes that valve for you — give it an explicit `kv` — and one coefficient could
not do the job anyway, because the two legs carry different flows.

### When it cannot be sized

If neither controlled leg reaches a stated pressure, nothing says which path varies when the valve
strokes, and the rule declines rather than guessing. The same happens when the drop is determined by
the boundaries but the circuit does not state exactly two pressures, so which pair drives this valve is
open. In both cases the valve keeps a placeholder `kv`, the report says it was never chosen, and you
are asked to state one.

## Properties

`kv`, `dp`, `position`, `authority`, `flow`.

## Also written as

`3_way_valve`, `mixing_valve`, `diverting_valve`, `3wv`.

Write `3_way_valve`, not `3-way-valve`: a hyphen subtracts.

## Tag

`TV` — a three-way valve in circuit 400 is tagged `400TV01`.

## See also

[`valve`](valve.md) · [`control`](control.md) · [`connections`](connections.md)
