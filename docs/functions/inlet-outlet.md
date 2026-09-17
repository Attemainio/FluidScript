# inlet and outlet

Two words, two readings, and which one you get depends on where the word sits.

```fluidscript
S1 inlet t=5 flow=2.3 l/s     # a declaration: a boundary the fluid enters through
R1 outlet                      # and the one it leaves through

inlet N3                      # a statement: where this subcircuit draws from its parent
outlet N5                      # and where it gives the flow back
```

A **name first** is a declaration, so `inlet` and `outlet` are component kinds. A **name after** is
an attachment statement, so they say which parent node this circuit hangs off. Nothing else in the
language is written both ways, and the two never collide: a declaration always begins with the name
you chose.

## As a boundary — `S1 inlet …`

An `inlet` is where fluid enters the model, and an `outlet` is where it leaves. Everything between
them modifies what the inlet delivered.

An inlet must say what it delivers, because nothing downstream of it means anything otherwise:

| Kind | Must state | May state | What it means |
|---|---|---|---|
| `inlet` | `t`, and exactly one of `flow` or `p` | the other of `flow`/`p` is solved | Fluid enters here in this state |
| `outlet` | nothing | `t`, `p`, `flow` | Fluid leaves here, in whatever state the circuit delivers |

Either may also state `elevation`, the boundary's height in metres above the project datum — a
an outlet on the roof of the building is `N2 outlet p=150 elevation=10`. See [`node`](node.md#height).

The asymmetry is the point. An inlet is a boundary condition — a fact about the plant outside the
model — and a temperature nobody stated cannot be guessed. An outlet is where the answer comes out:
demanding a number there would be inventing the thing the solve is meant to produce.

```fluidscript
S1  inlet t=60 flow=0.12      # 60 °C at 0.12 kg/s
S2  inlet t=60 p=300          # 60 °C at 300 kPa, and the flow follows from the circuit
S3  inlet t=60                # error FS2118: how hot, but not how much
S4  inlet flow=0.12           # error FS2117: how much, but not how hot
S5  inlet t=60 p=300 flow=0.12  # error FS2101: state one, and the other follows
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
NB1 inlet t=6 p=300
NB2 outlet p=280

connections
NB1 - NJ1                     # the boundary's one connection
NJ1 - HE1 - NJ2               # the split is NJ1's, the merge NJ2's
NJ1 - TV1.a
NM1 - NJ2
NJ2 - NB2
```

An inlet or outlet wired to two pipes is `FS2205`.

### When you need them, and when you do not

- **An open circuit needs them.** District heating that arrives at 85 °C and leaves at 45 °C has a
  real inlet and a real outlet, and this is how you say so.
- **A closed circuit does not.** A loop that recirculates has no boundary; it needs a heat source and
  a heat sink whose duties sum to zero, a stated temperature somewhere to fix its level, and a stated
  pressure on one node to fix its static pressure -- an expansion vessel connection, written as
  `N1 node p=150`. Without one the solve picks a node and says so (`FS2201`, a warning): every
  pressure is then relative to an arbitrary zero. Never write an inlet for that purpose: an inlet
  passes mass, a plain node's pressure does not.
- **Two stated pressures are also a complete pair.** `N1 node p=300` at one end and `N3 node p=280`
  at the other is a legal open circuit written the older way. Declaring the roles reads better and
  lets the checker tell a finished boundary from an unfinished stub.

## As an attachment — `inlet N3`

Inside a subcircuit, `inlet` names the parent node this circuit draws from and `outlet` names the
one it feeds. Together they are what makes several circuits on one distribution header a model rather
than three unrelated drawings.

```fluidscript
circuit AHU 101

connections
PU_AHU - HE_AHU - TV_AHU

inlet N3
outlet N5
```

- One `inlet` and one `outlet` per circuit. A second of either is an error.
- Each names a node in the parent circuit, never one of this circuit's own (`FS2217`).
- Writing `in N3` instead is an error that names `inlet` — it is never read as a component called
  `in` of kind `N3`.

## See also

[`node`](node.md) · [`circuit`](circuit.md) · [`connections`](connections.md) ·
[Why a circuit has one answer](../advanced/why-a-circuit-has-one-answer.md)
