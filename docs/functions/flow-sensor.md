# flow_sensor

A mass flow sensor. It sits on a node and reports what is there.

```fluidscript
FE1 flow_sensor at N2
```

## Why you place one

FluidScript does not read a mass flow off a pipe for you. If a control loop needs one, put an instrument
where the instrument really is — the same decision you make on a drawing.

That distinction matters more than it looks:

```fluidscript
HX1 heat_exchanger in=50 out=30      # what the design asks for
FE1 flow_sensor at N2                      # what the model produced
```

The first is a **specification**. The second is a **measurement**. They are different claims about
the plant, and a sensor is how you ask for the second.

## What it does to the model

Nothing. A sensor has no ports, no pressure drop and no heat transfer; it does not sit in the flow
path and adds no work to the solve. It reads the node it is attached to, and that is all.

An instrument with dynamics — a lag, an offset, an error band — is not in v1.

## Reading it

```fluidscript
control TV1 with FE1 by PID1 setpoint=0.24
```

Just `FE1`. A mass flow sensor measures one thing, so there is nothing to disambiguate. Writing
`FE1.flow` is also fine.

Its reading is in kg/s, and it is the flow **entering** the node — everything arriving, added up.

On a node with one pipe in and one out, that is simply the flow through it and there is nothing to
think about. On a tee it matters: a node where two branches merge has no single "the flow", and the
sum of what arrives is the one number that is well defined and matches what a meter in the combined
line would show. If you want a branch flow, place the instrument on a node in that branch.

## Its tag

`FE`, as an instrument index writes it — `400FE01` for the first one in circuit 400.

## See also

[`control`](control.md) · [`node`](node.md) · [`controller`](controller.md)
