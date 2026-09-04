# three_way_valve

A valve with three ports, used to mix two streams or to divert one.

```fluidscript
TV1 three_way_valve authority=0.5
```

## Ports

`a` is the common port, `b` the controlled one and `c` the bypass. `c` is optional — a three-way valve
used as a two-way leaves it open.

Leaving `c` open is not a cosmetic choice: the valve then **is** a two-way valve. It has two ports, one
Kv law, and no mixing to describe, so the circuit gets one equation from it rather than three. Which
one you wrote is reported as its mode — `three_way` or `two_way` — and it is read from the topology,
never declared.

**All three are bidirectional, and the arrangement comes from the topology rather than a
declaration.** A diverting valve takes one stream in at `a` and splits it between `b` and `c`; a
mixing valve — the commonest in hydronics — takes two streams in at `b` and `c` and delivers one at
`a`. Both are real, both are written the same way, and the port that carries flow toward the valve at
the design point is its inlet.

Ports are named in a connection with a dot:

```fluidscript
fluidscript 1
connections
N1 - TV1.a
TV1.b - N2
TV1.c - N3
```

## Parameters

The same as a [`valve`](valve.md): `kv`, `position`, `characteristic`, `authority`, `dp`.

`position` means the same in both: **1 is fully open between `a` and `b`**, whichever way the fluid
happens to run.

## Properties

`kv`, `dp`, `position`, `authority`, `flow`.

## Also written as

`3_way_valve`, `mixing_valve`, `diverting_valve`, `3wv`.

Write `3_way_valve`, not `3-way-valve`: a hyphen subtracts.

## Tag

`TV` — a three-way valve in circuit 400 is tagged `400TV01`.

## See also

[`valve`](valve.md) · [`control`](control.md) · [`connections`](connections.md)
