# Why a circuit has one answer

Before FluidScript solves anything it checks that there *is* something to solve: exactly one answer,
not none and not a family of them. This page is what that check does, and how to read what it says.

You do not have to know any of it to use the tool. You will want it the first time you see
**"This circuit is over-specified by 1"** and disagree.

## The two things called a pressure boundary

These look alike in a script and are not the same thing:

| | What it is | How many |
|---|---|---|
| **Pressure datum** | The arbitrary zero every pressure is measured from. Carries no engineering meaning | Exactly **one** per hydraulically connected part |
| **Pressure boundary** | A real constraint: something outside the model holds this node at this pressure, and lets mass in or out to do it | **Any number**, including none |

Writing `N1 node p=300` gives you a boundary condition. The first one in a connected part *also*
serves as its datum, so a circuit with a stated pressure needs nothing more.

A closed loop usually has none, and that is the common case — the whole tutorial circuit has none. So
the graph picks a node and tells you:

```
FS2201  Using 'N1' as the pressure datum. Pressures are relative to it.
```

That is information, not a warning. Every pressure in the result is then relative, and the diagram
shows them that way. The node chosen is the one with the most connections, so it does not move when
you edit an unrelated line.

**Two stated pressures are normal.** The cooling loop states `N1 p=300` and `N3 p=280`, and it must:
those two are what push water through its primary side. What is *not* normal is two pressures with
nothing between them that could make them differ — two nodes wired straight together, where the
second is not a boundary at all but a second, contradictory datum:

```
FS2212  'N1' and 'N2' both set a pressure on the same closed loop, with no path
        between them for flow to take. Remove one, or connect them.
```

## Counting

The check is a count: **how many numbers does the solver have to find, and how many equations does it
have to find them with?** They must be equal.

Most of the count is structural and you never influence it directly — one flow per branch, a pressure
and a temperature per node, a pressure relation per component, a mass balance per junction. Those
parts always balance. The parts that *don't* are the parts you write.

Here is the tutorial's cooling loop, counted in full:

| The solver must find | | It has | |
|---|---|---|---|
| Flow in each of 4 branches | 4 | Pressure relations across `PU1`, `HE1`, `P1`, `3WV` (two), and the `N1`–`N2` link | 6 |
| Pressure at each of 6 nodes | 6 | Mass balance at `N1`, `N2`, `N3` and `3WV` | 4 |
| Temperature at each of 6 nodes | 6 | Energy balance at each node | 6 |
| Mass entering at `N1` and leaving at `N3` | 2 | The two stated pressures | 2 |
| `PU1.head` | 1 | `HE1 out=50` | 1 |
| `3WV.position` | 1 | `HE1 in=20` | 1 |
| **Total** | **20** | **Total** | **20** |

Notice the last two rows on each side. `HE1 in=20` is not a fact about the exchanger — it is a
*demand* on the circuit: make the water arriving here 20 °C. Something has to move to meet it, and on
this circuit the only thing that can is how the three-way valve splits the flow. So stating it turns
`3WV.position` from a number the sizing step would have chosen into a number the solver has to find.

**The demand and the freedom arrive together, which is why the count stays balanced.** Delete
`in=20` and both rows disappear.

## Promotion, and what can absorb what

That trade has rules, because not everything can move everything:

| What you state | What moves to meet it | Why |
|---|---|---|
| An exchanger's `in` — a mixed inlet temperature | A three-way valve's `position` | Only the mixing split can change what arrives |
| An exchanger's `power` with `out` — which together fix a flow | The pump's `head` | Only the pump can change how much goes round the loop |
| The same, on one of several parallel branches | That branch's own valve `kv` | Parallel branches share their end-to-end pressure difference, so a branch's flow can only be changed by changing its own resistance. This is what a balancing valve is for |

**A demand with nothing to absorb it is the over-specification.** You have asked for something no
number in the model can deliver:

```
FS2210  This circuit is over-specified by 1. Remove one of: HE1.in.
```

The most common cause is a genuinely impossible circuit rather than a typo. A closed loop with a heat
source and no sink is the clearest one: the water comes back to the exchanger inlet at whatever
temperature it left, so asking for `in=20` while `out=50` cannot be satisfied by any valve, pump or
pipe — and the message names `HE1.in` because that is the statement to remove or the sink to add.

If you have three exchangers each demanding a mixed inlet temperature and only two mixing valves, you
get the same message for the same reason.

## When one circuit is really two

A rated heat exchanger joins two streams that never mix. A model containing one therefore has **more
than one hydraulically connected part**, and that is correct rather than an error — each gets its own
datum and its own mass balance, while the *energy* equations span both, because that is exactly what
the exchanger couples.

So two circuits sharing only an exchanger are not reported. Two circuits sharing nothing at all are:

```
FS2213  'HE_RAD, TV_RAD, PU_RAD' are not connected to the rest of the circuit.
```

## The rest of what it says

| Code | Means | What to do |
|---|---|---|
| `FS2201` | A datum was picked for you | Nothing. Read pressures as relative |
| `FS2202` | A port was left unconnected, and closed | Connect it, or leave the stub if it is deliberate |
| `FS2210` | More demands than freedoms | Remove one of the named statements, or add what could absorb it |
| `FS2211` | Fewer demands than freedoms | Add one of the named boundary conditions |
| `FS2212` | Two pressures forced equal, set differently | Remove one, or put something between them |
| `FS2213` | A part connected to nothing | Connect it, or move it to its own file |
| `FS2214` | A loop with no pump | Check whether a pump is on the wrong leg. The loop will carry no flow |
| `FS2215` | A stated temperature or pressure the fluid cannot be at | Correct the value, or change the fluid |
| `FS2216` | A two-sided component was tagged into a circuit arbitrarily | Nothing, unless the grouping on the diagram matters to you |
| `FS2217` | A subcircuit attached to itself | Point `supply` and `return` at the circuit it feeds from |

`FS2214` is a warning rather than information on purpose. A loop nothing drives simply carries no
flow, every temperature downstream of it is then wrong, and the result still looks like a solved
circuit.

## What is checked, and when

All of it runs **before** the solver, because every one of these produces a better message here than
it would inside the linear algebra. "Add a pressure to `N1`, `N2` or `N3`" is something you can act
on; a singular matrix is not.

None of it stops you typing. A script that is half-written fails these checks constantly, and the
diagram keeps drawing what you have so far.

## See also

- [How a script becomes a circuit](how-a-script-becomes-a-circuit.md) — the graph these checks run on
- [`node`](../functions/node.md) — `p`, `t` and `flow`, the three boundary conditions
- [`heat-exchanger`](../functions/heat-exchanger.md) — `in`, `out` and `power`, and which combinations fix a flow
