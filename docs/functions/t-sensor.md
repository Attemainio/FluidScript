# t_sensor

A temperature sensor. It sits on a node and reports what is there.

```fluidscript
TE1 t_sensor at N2
```

## Why you place one

FluidScript does not read a temperature off a pipe for you. If a control loop needs one, put an instrument
where the instrument really is — the same decision you make on a drawing.

That distinction matters more than it looks:

```fluidscript
HX1 heat_exchanger in.t=50 out.t=30      # what the design asks for
TE1 t_sensor at N2                      # what the model produced
```

The first is a **specification**. The second is a **measurement**. They are different claims about
the plant, and a sensor is how you ask for the second.

## Where it can sit

On a node with **one or two connections** — the end of a line, or a point on one pipe. Never on a
junction where three or more pipes meet: the streams arriving there are not yet mixed, an instrument on
any one of the pipes reads only its own, and which pipe you mean is yours to say. A sensor on a
junction is [`FS1548`](diagnostics.md), an error. Put a node on the pipe you mean and read
that one:

```fluidscript
TE1 t_sensor at NS

connections
N2 - NS             # NS is a point on the mixed pipe past the junction N2
NS - PU1
```

A mixing loop's supply sensor goes on the mixed pipe past the junction, where the streams have
combined, which is where an installer puts it.

## What it does to the model

Nothing. A sensor has no ports, no pressure drop and no heat transfer; it does not sit in the flow
path and adds no work to the solve. It reads the node it is attached to, and that is all.

An instrument with dynamics — a lag, an offset, an error band — is not in v1.

## Reading it

```fluidscript
control TV1 with TE1 by PID1 setpoint=20.4
```

Just `TE1`. A temperature sensor measures one thing, so there is nothing to disambiguate. Writing
`TE1.t` is also fine.

Its reading is in °C.

## Its tag

`TE`, as an instrument index writes it — `400TE01` for the first one in circuit 400.

## See also

[`control`](control.md) · [`node`](node.md) · [`controller`](controller.md)
