# controller

A PI or PID controller. Declaring one is separate from wiring it up: this page is the declaration, and
[`control`](control.md) is the line that says what it drives and what it measures.

```fluidscript
TC1 controller kp=0.03 ki=0.0005
```

## Ports

None. A controller carries no flow and is not part of the hydraulic graph.

## Parameters

| Parameter | Meaning | If you omit it |
|---|---|---|
| `kp` | Proportional gain, in actuator units per unit of measurement | Computed from a measured process gain, and reported |
| `ki` | Integral gain, per second | Computed as above |
| `kd` | Derivative gain | Absent — the controller is PI |

**Which algorithm you get follows from the gains, not from the spelling.** `kd` absent means PI, and
`kd` stated means PID. Writing `pi` instead of `controller` changes nothing about the algorithm, which
is why `TC1 pi kd=3` cannot mean two contradictory things.

Omitted gains are computed from a measured process gain and reported in the log, so a controller that
tunes itself still tells you what it chose.

## Properties

None yet.

## Also written as

`pi`, `pid`, `p`, `thermostat`.

`control` is **not** an alias: it is a reserved word introducing the binding line, so it can never
appear where a kind name belongs.

## Tag

`PID` — a controller in circuit 400 is tagged `400PID01`.

## See also

[`control`](control.md) · [`schedule`](schedule.md)
