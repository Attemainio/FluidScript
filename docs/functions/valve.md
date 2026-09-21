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
| `characteristic` | — | `linear`, `equal_percentage` or `quick_open` | `equal_percentage` (a [`three_way_valve`](three-way-valve.md) defaults to `linear`) |
| `authority` | — | Target authority for sizing | Sized |
| `dp` | kPa | Design drop at the design flow: the Kv is the next catalogue row above the one that takes it | Sized |
| `elevation` | m | Height above the project datum; see [`node`](node.md#height) | Wherever it is wired to, else 0 m |

`kv` is defined as m³/h of water at 1 bar differential, so a bare `kv=6.3` is in those units and
nothing else.

## How the Kv is chosen

A valve you do not give a `kv` is sized for **authority** — the share of its branch's total pressure
drop that the valve itself takes. Authority is what makes the travel mean something: a valve taking
half the branch's drop keeps roughly the characteristic you asked for, while one taking a tenth is a
switch with a handle, because the branch's own resistance dominates until the valve is nearly shut and
then the flow collapses over the last few percent.

The rule asks what drop gives the target share, works out the coefficient that produces it at the
design flow, and takes the nearest catalogue value **at or below** it. Rounding down means a slightly
smaller valve, which drops slightly more, so the authority you get is always at or above the one you
asked for — never below. The reported value is the one achieved:

```
CV1  kv         4      sized   Kv 4 (R5 preferred numbers) — authority 0.65 at 0.24 l/s, 4.6 kPa
                               — chosen against the branch's own resistance, which a free pump
                               absorbs, so the selection rounds down
CV1  authority  0.65   sized   0.65 achieved against a target of 0.5 — Kv 4 drops 4.6 of 7.1 kPa
```

### On a circuit the boundaries drive, it rounds the other way

That holds where a **pump with a head you have not stated** sits on a circuit through the valve: the
driving pressure is free, so the valve's drop is a choice and the pump takes up whatever it costs.

With no such pump — both ends of the path stating a pressure, which is how a district-heating primary
or any bounded connection is written — nothing is free to absorb anything. The driving pressure is
fixed, the valve takes **whatever the rest of the path leaves**, and there is no target left to aim at.
The selection then rounds **up**, which is the opposite direction and for a specific reason: at a fixed
differential a valve whose Kv is below what the balance requires cannot pass the design flow *however
far it opens*. Rounding down there would not make the valve safer, it would put the design point out of
reach and say nothing. Rounding up leaves it a little below fully open at design, which is the headroom
a control valve is meant to have.

The achieved authority may then land **below** your target, and that is not the rule failing — on a
bounded circuit the drop was never yours to choose. Which of the two shapes applied is written into the
basis, because the same catalogue row means different things under each:

```
CV1  kv         2.5    sized   Kv 2.5 (R5 preferred numbers) — authority 0.31 at 0.24 l/s, 20.0 kPa
                               — determined by the 20.0 kPa the boundaries offer, so the selection
                               rounds up
```

Write `authority=0.7` to change the target. The default is 0.5.

The sizes come from the **R5 preferred numbers** — 1.0, 1.6, 2.5, 4.0, 6.3 and the same digits in
every decade, which is what most manufacturers step their Kvs on. The step is 1.6× in Kv and therefore
2.56× in pressure drop, so the achieved authority can land well above the target: that is the
catalogue being coarse, not the rule being wrong.

### When it cannot be sized

If the rest of the branch drops almost nothing, there is no honest answer — a valve taking half of
almost nothing would sit inside the range where the solver smooths the flow law, and its authority
would not be what the arithmetic says. You are told so and asked to add resistance to the branch or to
state a `kv` yourself.

If the achieved authority is below 0.25 you get [`FS4006`](diagnostics.md): the valve will behave as a
switch. Raising authority means a smaller valve, and the smallest one in the catalogue may still be
larger than the branch needs, so the fix is usually to the branch rather than to the valve.

### Balancing valves

Authority is a **control**-valve criterion. A balancing valve is sized for a measurable drop at design
flow, typically 3–10 kPa, and the two are not the same job.

A valve with no `kv` becomes a balancing valve the moment the circuit needs one. If something else
on its loop fixes the pressure — a stated `head` on the pump, say, or a second branch's fixed flow
sharing the same pressure difference — the valve's `kv` is **solved** instead of sized: it closes
until it has absorbed exactly what the circuit has to spare. You see it in the solve report as a
solved value with no basis and no authority, because no rule chose it and no catalogue row was
taken. On the simple loop with `PU1 pump head=15`, that is Kv 0.77 dropping 124 kPa where an
unconstrained loop would have chosen Kv 1.6 and 29 kPa. Balancing a whole set of parallel branches
against one another still needs the branches' own drops stated or an explicit `kv` on each.

### A stated drop

`CV1 valve dp=30` asks for a valve that drops 30 kPa at the branch's design flow. The Kv that does so
exactly is computed from the Kv law, and the catalogue row chosen is the **next larger** one, so the
valve drops no more than you asked at that flow. That is the manufacturers' own rule for a calculated
Kv between two Kvs values: Belimo's planning notes take a calculated 4.5 m³/h to the 6.3 m³/h row.
The basis line says both numbers:

```
CV1.kv   Kv 1.6 (R5 preferred numbers) — the stated 30 kPa at 0.24 l/s asks Kv 1.57; the next larger row drops 29 kPa
```

Authority is then reported, not targeted. A three-way valve with a stated `dp` is sized the same way,
on its common-port flow, in place of the drop band.

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
