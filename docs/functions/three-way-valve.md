# three_way_valve

A valve with three ports, used to mix two streams or to divert one.

```fluidscript
TV1 three_way_valve authority=0.5
```

## Ports

`ab` is the common port; `a` and `b` are the two switched ones. These are the letters cast into the
valve body: a mixing valve is **A + B → AB** and a diverting valve is **AB → A + B**, so the common
port is the one written with both letters.

`b` is optional — a three-way valve used as a two-way leaves it open. That is not a cosmetic choice:
the valve then **is** a two-way valve. It has two ports, one Kv law, and no mixing to describe, so the
circuit gets one equation from it rather than three. Which one you wrote is reported as its mode —
`three_way` or `two_way` — and it is read from the topology, never declared.

**All three are bidirectional, and mixing or diverting comes from the topology rather than a
declaration.** A diverting valve takes one stream in at `ab` and splits it between `a` and `b`; a
mixing valve — the commonest in hydronics — takes two streams in at `a` and `b` and delivers one at
`ab`. Both are real, both are written the same way, and the port that carries flow toward the valve at
the design point is its inlet. Note that a valve body is built for one service or the other and they
are not interchangeable in the field; nothing here checks that yet.

**What leaves a mixing valve is the mass-weighted mix of what enters it**:
`h_ab = (ṁ_a·h_a + ṁ_b·h_b) / (ṁ_a + ṁ_b)`. So 0.19 kg/s of 60 °C water through `a` and 0.10 kg/s of
30 °C water through `b` deliver 50 °C at `ab`, and moving the position moves that temperature — which
is the whole reason a stated inlet on the coil downstream can be answered by this valve's position.
The mix is smooth through a reversal of either inlet, so a leg that turns round mid-solve does not
put a kink in the energy balance.

Ports are named in a connection with a dot:

```fluidscript
fluidscript 1
connections
N1 - TV1.ab
TV1.a - N2
TV1.b - N3
```

## Parameters

The same as a [`valve`](valve.md): `kv`, `position`, `characteristic`, `authority`, `dp`.

`position` means the same in both: **1 is fully open between `ab` and `a`**, whichever way the fluid
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

**Name the ports and you have said which leg that is.** `a` is the control path and `b` the bypass —
the A–AB and B–AB of the valve body — and that is also how the equations read them, so writing
`TV1.a - P1` and `TV1.b - N2` settles the question outright. This is the recommended way to write a
three-port valve you want sized.

**Leave them unnamed and the rule works it out from the shape**, because an unwritten letter is only
connection order. Ports take connections in the order you write them, so a bare `TV1 - N2` before
`TV1 - P1` makes `a` the *recirculation* leg — the opposite of what the letter means. The rule ignores
an inferred letter for exactly that reason and asks the circuit instead: the bypass is the leg that gets
back to where the common leg lands in the fewest components, since closing the valve's own loop is what
a bypass does, and the other leg is the one that varies.

That reading is right on every shape in the corpus, but it is a reading rather than a statement, and it
has two blind spots: a short tap off a header feeding a long secondary looks inverted to it, and an
injection circuit whose two switched legs land on the same header looks symmetric. Naming the ports is
the answer to both.

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

If the connections name no ports **and** the two switched legs are the same distance from the leg they
split, nothing says which of them recirculates and which varies when the valve strokes, and the rule
declines rather than guessing. Naming them — `a` for the leg it controls, `b` for the bypass — is the
fix, and the message says so.

The same happens when the drop is determined by the boundaries but the circuit does not state exactly
two pressures, so which pair drives this valve is open. In both cases the valve keeps a placeholder
`kv`, the report says it was never chosen, and you are asked to state one.

## Properties

`kv`, `dp`, `position`, `authority`, `flow`.

## Also written as

`3_way_valve`, `mixing_valve`, `diverting_valve`, `3wv`.

Write `3_way_valve`, not `3-way-valve`: a hyphen subtracts.

## Tag

`TV` — a three-way valve in circuit 400 is tagged `400TV01`.

## See also

[`valve`](valve.md) · [`control`](control.md) · [`connections`](connections.md)
