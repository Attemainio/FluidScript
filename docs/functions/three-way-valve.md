# three_way_valve

A valve with three ports, used to mix two streams or to divert one.

```fluidscript
circuit "Mixing loop":
  fluid = water
  S1   inlet  t = 60  p = 300
  R1   outlet  p = 280
  TV1  three_way_valve
  PU1  pump
  HE1  load  in.t = 50  out.t = 30  power = 24 kW

  S1 - TV1                      # supply water enters
  NM - TV1                      # the coil's return recirculates
  TV1 - PU1 - HE1 - NM
  NM - R1
```

## Ports

`ab` is the common port; `a` and `b` are the two switched ones. These are the letters cast into the
valve body: a mixing valve is **A + B → AB** and a diverting valve is **AB → A + B**, so the common
port is the one written with both letters.

On the diagram the valve is drawn the way the body is built: `a` and `ab` are in line and `b` is
the side port. That is how Belimo and Siemens cast them -- the straight run is A to AB, B is the
angle port -- so a mixing valve on a branch reads as it is piped: supply in at `a`, mixed water on to
the pump from `ab`, the bypass into `b` from the side.

`b` is optional — a three-way valve used as a two-way leaves it open. That is not a cosmetic choice:
the valve then **is** a two-way valve. It has two ports, one Kv law, and no mixing to describe, so the
circuit gets one equation from it rather than three. Which one you wrote is reported as its mode —
`three_way` or `two_way` — and it is read from the topology, never declared.

**All three are bidirectional, and mixing or diverting comes from the topology rather than a
declaration.** A diverting valve takes one stream in at `ab` and splits it between `a` and `b`; a
mixing valve — the commonest in hydronics — takes two streams in at `a` and `b` and delivers one at
`ab`. Both are real, both are written the same way, and the port that carries flow toward the valve at
the design point is its inlet. Note that a valve body is built for one service or the other and they
are not interchangeable in the field; a bare `three_way_valve` is not checked for it, and the two
spellings that name a service are ([Also written as](#also-written-as)).

**What leaves a mixing valve is the mass-weighted mix of what enters it**:
`h_ab = (ṁ_a·h_a + ṁ_b·h_b) / (ṁ_a + ṁ_b)`. So 0.19 kg/s of 60 °C water through `a` and 0.10 kg/s of
30 °C water through `b` deliver 50 °C at `ab`, and moving the position moves that temperature — which
is the whole reason a stated inlet on the coil downstream can be answered by this valve's position.
The mix is smooth through a reversal of either inlet, so a leg that turns round mid-solve does not
put a kink in the energy balance.

**You need not write the ports: the connections name them.** Two connections flowing in and one
flowing out make a mixing valve, whose out is `ab` and whose ins are `a` and `b`; one in and two out
make a diverting valve, whose in is `ab` and whose outs are `a` and `b`. Which switched leg is `a` is
read from the plant, as [below](#which-leg-varies-for-the-authority-rule-and-the-reported-figure): `a`
is the control path and `b` the bypass. The compiler says once what it chose, as information
([`FS1815`](diagnostics.md)) — for the circuit above, *'TV1' is wired as a mixing valve: a from S1, b
from NM, ab to PU1.* What the connections cannot settle, such as three flowing in, is
[`FS1804`](diagnostics.md), naming the ports to write.

A port written with a dot always wins:

```fluidscript
circuit "Diverting":
  fluid = water
  S1   inlet  t = 20  p = 300
  R1   outlet  p = 280
  R2   outlet  p = 280
  TV1  three_way_valve

  S1 - TV1.ab
  TV1.a - R1
  TV1.b - R2
```

## Parameters

The same as a [`valve`](valve.md): `kv`, `position`, `characteristic`, `authority`, `dp`, `stroke`, and
`elevation` — one height for all three ports; see [`node`](node.md#height) — plus one of its own,
`leakage`: the fraction of `kv` a leg still passes at its stop.

One default differs. A three-way valve's `characteristic` is **`linear`** where a two-way valve's is
`equal_percentage`: its two legs open complementarily, so a linear pair keeps the total flow through
the valve constant over the stroke — which is what a mixing valve is for — while an equal-percentage
pair passes only 28 % of it at mid-travel. Write `characteristic = equal_percentage` for a valve built
that way.

### A shut leg still leaks, and how much is the body's

No three-port body shuts a leg completely, and what it passes at the stop is a catalogue figure:
Belimo's characterised three-way valves rate the bypass B–AB at leakage class I, 1–2 % of Kvs (EN
1349 / IEC 60534-4), with the control path bubble-tight; ESBE's VRG130 rotary mixing valves are
under 0.05 % mixing and 0.02 % diverting. `leakage` is that figure, as a fraction of `kv`, and both
legs pass it at their stops. **The default is 2 %**, the leakier published body; a rotary valve is
written `leakage = 0.05%`. It is a small number with a visible effect only where a leg is shut: a
consumer that is off passes its trickle through the valve, 0.0062 kg/s at the default and 0.0002 at a
rotary body's rating, and the position the solve reports at the stop does not change.

Below 0.01 % — FCI 70-2 class IV, the tightest a metal seat is ordinarily built to — the model
holds the trickle at 0.01 % whatever you state, `leakage = 0` included. A stopped branch has no flow
but its water still has a temperature, and the trickle is how the model finds it: at zero the
solve is singular with nothing determining that branch's temperatures.

An `equal_percentage` leg is its own characteristic at the stop, 2 %, whatever the body is rated;
`leakage` is the linear leg's number.

`position` means the same in both: **1 is fully open between `ab` and `a`**, whichever way the fluid
happens to run.

## How the Kv is chosen

A three-way valve you do not give a `kv` is sized the way a rotary mixing valve is selected from a
catalogue: on the **flow through its common port**, to a **pressure drop of 3–15 kPa** fully open,
taking the smallest coefficient in the R5 series that drops less than 15 kPa. That is the rule
ESBE prints on its mixing-valve data sheets — start from the heat demand at the circuit's Δt, move
into the 3–15 kPa band, take the smaller Kvs — and the flow it uses is what the two legs mix or
split, not the primary draw alone.

```
TV_RAD  kv         6.3    sized   Kv 6.3 (R5 preferred numbers) — 7.6 kPa at 0.484 l/s through the
                                  common port, inside the 3–15 kPa a mixing valve is sized to;
                                  authority 0.54 against the variable circuit
TV_RAD  authority  0.54   sized   0.54 against the variable circuit, fully open — Kv 6.3 drops 1.9 kPa
                                  of its 3.5 kPa; reported, not targeted: a mixing valve is sized to
                                  its drop band
```

The authority is still worked out and reported — the leg that varies, fully open, against the circuit
whose flow it changes, exactly as a [`valve`](valve.md) reports it — but it is not what chose the
coefficient. It is there so that the two rules read alike, and [`FS4006`](diagnostics.md) still
tells you when it comes out below 0.25.

**Why not authority.** A control valve is sized fully open at its design flow because that is where
it runs at design, mixing only at part load. A mixing valve whose stated inlet lies between what
feeds it and its own return runs *at* the mixing point at design — 60 and 40 to 50 is half and half
— and a coefficient chosen for authority fully open is then far too small at the position the valve
actually sits at: on one series header the authority rule chose Kv 1.6 and the pump was asked for
15 bar. The band rule chose 6.3, and the pump for 5.7 m.

**If you want the authority rule, ask for it.** Stating `authority = 0.5` sizes the valve as a
[`valve`](valve.md) is sized, on the leg that varies. Then everything below about which leg that is
applies.

### Which leg varies, for the authority rule and the reported figure

**Name the ports and you have said which leg that is.** `a` is the control path and `b` the bypass —
the A–AB and B–AB of the valve body — and that is also how the equations read them, so writing
`TV1.a - P1` and `TV1.b - N2` settles the question outright.

**Leave them unnamed and the plant names them**: the bypass is the leg that gets back to where the
common leg lands in the fewest components, the valve itself barred, since closing the valve's own loop
is what a bypass does, and the other leg is `a`, the one that varies. The letter the equations open
with the position and the leg the sizing measures are therefore the same leg, and `FS1815` says which
leg took which letter.

That reading is right on every shape in the corpus, but it is a reading rather than a statement, and it
has two blind spots: a short tap off a header feeding a long secondary looks inverted to it, and an
injection circuit whose two switched legs land on the same header is symmetric to it, so the order you
wrote them decides, the first `a`. Naming the ports is the answer to both.

**Under the authority rule, whether the drop is chosen or determined depends on what drives the
circuit.** With a pump on the path whose head you have not stated, the driving pressure is free, the
valve's drop is a choice, and the authority target makes it — rounding **down** as for a two-way
valve. With no such pump the boundary pressures fix the driving pressure, the valve takes whatever
the rest of the path leaves, and the selection rounds **up** instead: at a fixed differential a
coefficient below the required one cannot pass the design flow at any position, so rounding down
there would make the design point unreachable rather than safe. Which of the two was used is written
into the reported basis.

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
starved. The three-way valve's own `kv` cannot do that job — one coefficient serves two legs that
carry different flows — but a [`valve`](valve.md) on the bypass connection can, and one with no `kv`
is set for you: each pass sets it to the drop that brings the bypass level with the primary path,
and the three-way valve then sits at the position its ratio implies. Write it as any other component
on the connection:

```fluidscript
circuit "radiators":
  fluid = water
  N4      inlet  t = 60  p = 300       # the header's supply and return, as boundaries here
  N6      outlet  p = 280
  HE_RAD  load  in.t = 50  out.t = 30  power = 30 kW
  TV_RAD  three_way_valve
  PU_RAD  pump
  BV_RAD  valve

  N4 - TV_RAD   18 m  DN25
  NM_RAD - BV_RAD - TV_RAD.b
  TV_RAD - PU_RAD - HE_RAD - NM_RAD
  NM_RAD - N6   18 m  DN25
```

**The solve tells you when it is missing.** A three-way valve's two legs share one `position`, so
the position its mixing ratio implies — half and half is 0.5 — is only reached when both legs see
the same pressure at their far ends. When one path is easier than the other, the valve has to
throttle that leg to make the flows come out, and it leaves its mixing position to do it. After the
solve, [`FS4011`](diagnostics.md) is raised on any three-way valve whose legs differ by more than
the valve's own full-open drop at the flow it carries (never less than 3 kPa), and it names the
balancing valve that would level them:

```
FS4011  'TV_RAD' throttles its b leg by 24.7 kPa at position 0.74: that path is 21.3 kPa easier
        than the a path, more than the 7.6 kPa the valve drops fully open. A balancing valve
        between NM_RAD and TV_RAD.b dropping 21.3 kPa at 0.239 kg/s (Kv 1.88) would level the
        legs and leave the valve its travel.
```

That is a series header whose primary ring has no pump of its own: the radiators' pump drives the
ring, pays its 21 kPa on the way to `a`, and the bare bypass returns to the mixing node 21 kPa
higher. Two things the warning does not say, because they are easy to misread. The solved state is
right — the valve really would sit there — and a balancing valve does **not** lower the pump head:
it dissipates the same 21 kPa the three-way valve was dissipating, and the pump head stays what the
ring costs. What it buys is the valve's travel: at its mixing position with its whole stroke
available for control, instead of near one end where a small movement is a large change in flow.

One more thing to know about that particular plant: with no pump of its own on the primary ring,
*which* block's pump pays the ring's cost is not something the script decides, so the kilopascals in
that message are one answer among many the equations allow. State a primary pump, or a duty on one
of the block pumps, and the number is yours rather than the solver's.

Add the balancing valve and the warning goes: on the one-branch ring, `TV_AHU` moves from 0.79 to
0.674, the two thirds of its flow the primary supplies, with 6.5 kPa across each leg, and `BV_AHU`
is set to Kv 0.75 dropping 21.5 kPa. A diverting valve reads the same way with the signs turned: the
cooling loop's `3WV` with a valve on its return leg sits at 0.31, its recirculation share, with
11.5 kPa across each leg.

### A consumer that is off still passes a trickle

A consumer at `power = 0` does not shut its valve. Both header legs stay open at the position the solve
finds, so the supply-to-return differential drives a small flow in at `a` and out through `b` across
the chamber while the common port carries next to nothing. The report shows it as the two legs equal
and opposite. That is a three-port body doing what its geometry allows, not a leak in the model; a
consumer that must be isolated needs a shut-off valve of its own.

### When it cannot be sized

If the drop is determined by the boundaries but the circuit does not state exactly two pressures,
which pair drives this valve is open, and the rule declines rather than guessing. The valve keeps a
placeholder `kv`, the report says it was never chosen, and you are asked to state one.

## Properties

`kv`, `dp`, `position`, `authority`, `flow`.

## Also written as

`3_way_valve`, `mixing_valve`, `diverting_valve`, `3wv`, `valve3`.

**Two of those spellings say something.** A seat body is built for one service — Siemens' VXG44 is
"to be used only as a mixing valve" — and which one a plant needs is decided by how the ports are
wired, not by the valve. `mixing_valve` and `diverting_valve` name the body you intend to buy. A body
whose connections say the other function — two in where `diverting_valve` expects one — is
[`FS1805`](diagnostics.md) before anything is solved; beyond that, the solve finds which way the water
actually runs (two streams in at `a` and `b` is mixing, one in at
`ab` is diverting), and when the two disagree [`FS4012`](diagnostics.md) says so:

```
FS4012  '3WV' is written as a mixing valve and the solve runs it diverting: 0.239 kg/s enters at
        ab and leaves 0.076 kg/s by a and 0.163 kg/s by b. A body built for one service must
        not be used for the other. Write it as three_way_valve if the arrangement is open, or
        wire the ports for mixing.
```

A bare `three_way_valve`, `3_way_valve`, `3wv` or `valve3` claims nothing and is never reported. Rotary
mixing valves such as ESBE's VRG series serve both functions, and are written bare.

Write `3_way_valve`, not `3-way-valve`: a name cannot contain a hyphen, and `3-way-valve` is
[`FS1108`](diagnostics.md).

## Tag

`TV` — a three-way valve in circuit 400 is tagged `400TV01`.

## See also

[`valve`](valve.md) · [`controller`](controller.md) · [`circuit`](circuit.md)
