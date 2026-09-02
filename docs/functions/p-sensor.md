# p_sensor

A pressure sensor. It sits on a node and reports what is there.

```fluidscript
PE1 p_sensor at N2
```

## Why you place one

FluidScript does not read a pressure off a pipe for you. If a control loop needs one, put an instrument
where the instrument really is — the same decision you make on a drawing.

That distinction matters more than it looks:

```fluidscript
HX1 heat_exchanger in=50 out=30      # what the design asks for
PE1 p_sensor at N2                      # what the model produced
```

The first is a **specification**. The second is a **measurement**. They are different claims about
the plant, and a sensor is how you ask for the second.

## What it does to the model

Nothing. A sensor has no ports, no pressure drop and no heat transfer; it does not sit in the flow
path and adds no work to the solve. It reads the node it is attached to, and that is all.

An instrument with dynamics — a lag, an offset, an error band — is not in v1.

## Reading it

```fluidscript
control TV1 with PE1 by PID1 setpoint=312
```

Just `PE1`. A pressure sensor measures one thing, so there is nothing to disambiguate. Writing
`PE1.p` is also fine.

Its reading is in kPa.

## Its tag

`PE`, as an instrument index writes it — `400PE01` for the first one in circuit 400.

## See also

[`control`](control.md) · [`node`](node.md) · [`controller`](controller.md)
