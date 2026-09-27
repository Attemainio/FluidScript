# t_sensor

A temperature sensor. It sits on a node and reports what is there.

```fluidscript
circuit "Heating":
  fluid = water
  PU1  pump
  RAD  radiator  power = 30  dt = 20
  TE1  t_sensor              # sits in the chain below

  PU1 - TE1 - RAD - PU1
```

## Why you place one

FluidScript does not read a temperature off a pipe for you. If a control loop needs one, put an instrument
where the instrument really is — the same decision you make on a drawing.

That distinction matters more than it looks:

```fluidscript
circuit "Heating":
  fluid = water
  PU1  pump
  HX1  heat_exchanger  in.t = 50  out.t = 30   # what the design asks for
  TE1  t_sensor  at N2                      # what the model produced

  PU1 - HX1 - N2 - PU1
```

The first is a **specification**. The second is a **measurement**. They are different claims about
the plant, and a sensor is how you ask for the second.

## Where it can sit

**In a chain**, between the two components it measures between: `PU1 - TE1 - RAD` puts a node between
the pump and the radiator, named `TE1_node`, and the sensor reads it. **Or on a named node**, with
`at`: `TE1 t_sensor at N2`. Placing one both ways is [`FS1814`](diagnostics.md), and the chain's
placement is kept.

Either way it sits on a node with **one or two connections** — the end of a line, or a point on one
pipe. Never on a junction where three or more pipes meet: the streams arriving there are not yet mixed,
an instrument on any one of the pipes reads only its own, and which pipe you mean is yours to say. A
sensor on a junction is [`FS1548`](diagnostics.md), an error. Put a node on the pipe you mean and read
that one:

```fluidscript
circuit "Mixing loop":
  fluid = water
  S1   inlet  t = 70  p = 300
  R1   outlet  p = 280
  PU1  pump
  HE1  load  power = 30  dt = 20
  TE1  t_sensor  at NS

  S1 - N2
  N2 - NS - PU1         # NS is a point on the mixed pipe past the junction N2
  PU1 - HE1 - N3
  N3 - N2
  N3 - R1
```

A mixing loop's supply sensor goes on the mixed pipe past the junction, where the streams have
combined, which is where an installer puts it.

## What it does to the model

Nothing. A sensor has no ports, no pressure drop and no heat transfer; it does not sit in the flow
path and adds no work to the solve. It reads the node it is attached to, and that is all. Written in a
chain, it is that node.

An instrument with dynamics — a lag, an offset, an error band — is not modelled.

## Reading it

```fluidscript
circuit "Loop":
  fluid = water
  PU1  pump
  TV1  valve
  HE1  load  power = 30  dt = 20
  TE1  t_sensor
  TC1  controller:
    moves    = TV1
    reads    = TE1
    setpoint = 20.4

  PU1 - TV1 - TE1 - HE1 - PU1
```

Just `TE1`. A temperature sensor measures one thing, so there is nothing to disambiguate. Writing
`TE1.t` is also fine.

Its reading is in °C.

## Also written as

`temperature_sensor`, `te`.

## Its tag

`TE`, as an instrument index writes it — `400TE01` for the first one in circuit 400.

## See also

[`controller`](controller.md) · [`node`](node.md) · [`circuit`](circuit.md)
