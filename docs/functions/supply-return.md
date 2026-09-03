# supply and return

Two words, two readings, and which one you get depends on where the word sits.

```fluidscript
S1 supply t=5 flow=2.3 l/s     # a declaration: a boundary the fluid enters through
R1 return                      # and the one it leaves through

supply N3                      # a statement: where this subcircuit draws from its parent
return N5                      # and where it gives the flow back
```

A **name first** is a declaration, so `supply` and `return` are component kinds. A **name after** is
an attachment statement, so they say which parent node this circuit hangs off. Nothing else in the
language is written both ways, and the two never collide: a declaration always begins with the name
you chose.

## As a boundary — `S1 supply …`

A `supply` is where fluid enters the model, and a `return` is where it leaves. Everything between
them modifies what the supply delivered.

A supply must say what it delivers, because nothing downstream of it means anything otherwise:

| Kind | Must state | May state | What it means |
|---|---|---|---|
| `supply` | `t`, and exactly one of `flow` or `p` | the other of `flow`/`p` is solved | Fluid enters here in this state |
| `return` | nothing | `t`, `p`, `flow` | Fluid leaves here, in whatever state the circuit delivers |

The asymmetry is the point. A supply is a boundary condition — a fact about the plant outside the
model — and a temperature nobody stated cannot be guessed. A return is where the answer comes out:
demanding a number there would be inventing the thing the solve is meant to produce.

```fluidscript
S1  supply t=60 flow=0.12      # 60 °C at 0.12 kg/s
S2  supply t=60 p=300          # 60 °C at 300 kPa, and the flow follows from the circuit
S3  supply t=60                # error FS2118: how hot, but not how much
S4  supply flow=0.12           # error FS2117: how much, but not how hot
S5  supply t=60 p=300 flow=0.12  # error FS2101: state one, and the other follows
```

**Fluid must both enter and leave, or neither.** A circuit with a `supply` and nowhere for the fluid
to go is `FS2204`. A closed loop needs neither word — it recirculates, and the model already knows
that.

A `supply` and a `return` are otherwise ordinary [nodes](node.md): same unlimited unnamed ports, same
properties, same place in the diagram.

### When you need them, and when you do not

- **An open circuit needs them.** District heating that arrives at 85 °C and leaves at 45 °C has a
  real inlet and a real outlet, and this is how you say so.
- **A closed circuit does not.** A loop that recirculates has no boundary; it needs a heat source and
  a heat sink whose duties sum to zero, and a stated temperature somewhere to fix its level.
- **Two stated pressures are also a complete pair.** `N1 node p=300` at one end and `N3 node p=280`
  at the other is a legal open circuit written the older way. Declaring the roles reads better and
  lets the checker tell a finished boundary from an unfinished stub.

## As an attachment — `supply N3`

Inside a subcircuit, `supply` names the parent node this circuit draws from and `return` names the
one it feeds. Together they are what makes several circuits on one distribution header a model rather
than three unrelated drawings.

```fluidscript
circuit AHU 101

connections
PU_AHU - HE_AHU - TV_AHU

supply N3
return N5
```

- One `supply` and one `return` per circuit. A second of either is an error.
- Each names a node in the parent circuit, never one of this circuit's own (`FS2217`).
- Writing `in N3` instead is an error that names `supply` — it is never read as a component called
  `in` of kind `N3`.

## See also

[`node`](node.md) · [`circuit`](circuit.md) · [`connections`](connections.md) ·
[Why a circuit has one answer](../advanced/why-a-circuit-has-one-answer.md)
