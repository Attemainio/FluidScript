# inlet and outlet

Where fluid enters the model and where it leaves. Both are component kinds, declared in a circuit like
any other, and each is a boundary of the model.

```fluidscript
circuit "Chilled water":
  fluid = water
  S1   inlet  t = 5  flow = 2.3       # a boundary the fluid enters through
  HE1  heat_exchanger  power = 40  dt = 5
  R1   outlet                         # and the one it leaves through

  S1 - HE1 - R1
```

## As a boundary

An `inlet` is where fluid enters the model, and an `outlet` is where it leaves. Everything between
them modifies what the inlet delivered.

An inlet must say what it delivers, because nothing downstream of it means anything otherwise:

| Kind | Must state | May state | What it means |
|---|---|---|---|
| `inlet` | `t`, and exactly one of `flow` or `p` | the other of `flow`/`p` is solved | Fluid enters here in this state |
| `outlet` | nothing | `t`, `p`, `flow` | Fluid leaves here, in whatever state the circuit delivers |

Either may also state `elevation`, the boundary's height in metres above the project datum — an
outlet on the roof of the building is `N2 outlet p = 150 elevation = 10`. See [`node`](node.md#height).

The asymmetry is the point. An inlet is a boundary condition — a fact about the plant outside the
model — and a temperature nobody stated cannot be guessed. An outlet is where the answer comes out:
demanding a number there would be inventing the thing the solve is meant to produce.

```fluidscript
circuit "Boundaries":
  fluid = water
  S1  inlet  t = 60  flow = 0.12          # 60 °C at 0.12 kg/s
  S2  inlet  t = 60  p = 300              # 60 °C at 300 kPa, and the flow follows from the circuit
  S3  inlet  t = 60                       # error FS2118: how hot, but not how much
  S4  inlet  flow = 0.12                  # error FS2117: how much, but not how hot
  S5  inlet  t = 60  p = 300  flow = 0.12 # error FS2101: state one, and the other follows
```

**Fluid must both enter and leave, or neither.** A circuit with an `inlet` and nowhere for the fluid
to go is `FS2204`. A closed loop needs neither word — it recirculates, and the model already knows
that.

An `inlet` and an `outlet` are otherwise ordinary [nodes](node.md): the same properties and the same
place in the diagram, with one difference: **a boundary has exactly one connection.** It is the pipe
the fluid arrives or leaves by. A flow that splits after the inlet, or merges before the outlet, does
so at a node you write, so that the junction is a junction and the boundary states one stream's
condition:

```fluidscript
circuit "Chilled water":
  fluid = water
  NB1  inlet  t = 6  p = 300
  NB2  outlet  p = 280
  HE1  heat_exchanger  power = 20  dt = 5
  HE2  heat_exchanger  power = 10  dt = 5

  NB1 - NJ1                     # the boundary's one connection
  NJ1 - HE1 - NJ2               # the split is NJ1's, the merge NJ2's
  NJ1 - HE2 - NJ2
  NJ2 - NB2
```

An inlet or outlet wired to two pipes is `FS2205`.

### When you need them, and when you do not

- **An open circuit needs them.** District heating that arrives at 85 °C and leaves at 45 °C has a
  real inlet and a real outlet, and this is how you say so.
- **A closed circuit does not.** A loop that recirculates has no boundary; it needs a heat source and
  a heat sink whose duties sum to zero, a stated temperature somewhere to fix its level, and a stated
  pressure on one node to fix its static pressure -- an expansion vessel connection, written as
  `N1 node p = 150`. Without one the solve picks a node and says so (`FS2201`, a warning): every
  pressure is then relative to an arbitrary zero. Never write an inlet for that purpose: an inlet
  passes mass, a plain node's pressure does not.
- **Two stated pressures on plain nodes are not a pair.** `N1 node p = 300` at one end and
  `N3 node p = 280` at the other are two dead ends: each is [`FS2107`](diagnostics.md), because a
  node's pressure states a level and passes no mass. Where fluid crosses, declare an `inlet` and an
  `outlet`.

Two circuits never join through a boundary: they join through a component both name, since a
connection line in one circuit may name a component declared in another ([`circuit`](circuit.md)).

## See also

[`node`](node.md) · [`circuit`](circuit.md) ·
[Why a circuit has one answer](../advanced/why-a-circuit-has-one-answer.md)
