# circuit

A circuit block: its fluid, its components, and the connection lines that join them.

```fluidscript
fluidscript 2

circuit "Heating":
  fluid  = water
  number = 200
  role   = heating

  BLR  heater    power = 150  out.t = 70 C
  SP   pump
  RAD  radiator  power = 150  out.t = 40 C
  TV1  valve3

  BLR - TV1 - SP - NS - RAD - NR
  NR - TV1
  NR - BLR                   6 m  DN32
```

Everything indented under the head belongs to the circuit. Its settings, declarations and connection
lines go in any order, so write them in whatever order reads best.

## Settings

| Setting | What it says | When it is left out |
|---|---|---|
| `fluid` | What the circuit carries — see [`fluid`](fluid.md) | Water |
| `number` | The circuit's number, the prefix of its equipment tags | The next free hundred: 100, 200, 300 |
| `role` | Where the drawing places the circuit — see [below](#the-role) | Placed by what it connects to |
| `style:` | How this circuit is drawn, over the project's style — see [`style`](style.md) | The project's style |

Anything else is [`FS1503`](diagnostics.md), listing these four.

- **The title is only a name.** It is quoted, it may contain spaces, and it is how a run's `steady`
  names the circuit. A circuit written without one, `circuit:`, is named `circuit 1`, `circuit 2`, …
  in file order.
- **A number you write is kept verbatim**, and one you did not write is never written into your file.
  Numbers you leave out take the next free hundred in file order: with `number = 400` on the second
  of three circuits, the others are 100 and 200.
- The number is what makes equipment tags unique across circuits: `101PU01` and `201PU01` are pumps
  in different circuits, and neither collides with the other ([Equipment tags](tags.md)).

## Declarations

A component is declared inside a circuit, its name first and its kind second, then its parameters on
the same line or in an indented block:

```fluidscript
fluidscript 2

circuit "Heating":
  fluid = water

  RAD  radiator  power = 150                  # one line

  HX1  exchanger:                             # a block
    primary.out.t   = 45 C
    secondary.in.t  = 40 C
    secondary.out.t = 60 C
```

The two forms are one grammar, and the printer keeps whichever you wrote. Names are global to the
file, so a connection line in one circuit may name a component declared in another; that is how two
circuits are joined, through an exchanger both name. Each component's page lists its parameters.

## Connection lines

A connection line joins components. **It reads in the direction of flow**: `A - B - C` is water
leaving `A`, passing `B` and entering `C`.

- `A - B - C` is one line and two connections, and it stays one line in your file.
- **An endpoint no declaration names is a node.** `NS` and `NR` above are nodes nobody declared; a
  node that needs a state is declared like any component (`N1 node t = 6 p = 300`, see
  [`node`](node.md)).
- Connecting two components directly creates the node between them that their states need, named
  after what it joins (`TV1__SP`).

### Which port a connection takes

**Ports are inferred from the direction of flow, and written only to be explicit.** After every line
of the file is read:

1. **A two-port component** — a pump, a valve, a pipe, a one-sided exchanger: the connection flowing
   in takes its inlet, the one flowing out its outlet.
2. **A three-way valve** counts its connections. Two in and one out is a **mixing** valve: the out is
   `ab`, the ins `a` and `b`. One in and two out is **diverting**: the in is `ab`, the outs `a` and
   `b`. `a` is the control path and `b` the bypass, read from the plant: the bypass is the leg that
   gets back to the common port's far end crossing the fewest components. Above, `NR - TV1` closes
   the valve's own loop through `RAD`, so it is `b`. See [`three_way_valve`](three-way-valve.md).
3. **A two-sided exchanger**: the side wired inside the circuit that declares it is `primary`; the
   side wired from any other circuit is `secondary`. When both sides are wired in one circuit, the
   first pass written is primary. See [`heat_exchanger`](heat-exchanger.md).
4. **A tank's** inflows take `in`, `in[2]`, … and its outflows `out`, `out[2]`, … in the order
   written. See [`tank`](tank.md).
5. **A port you write always wins**: `HX1.secondary.out - TV1.a`.
6. **What the rule cannot settle is an error, never a guess** — a valve with three inflows, an
   exchanger side with two inlets — [`FS1804`](diagnostics.md), naming the ports to write.

Where the rule chose between ports it says so once, as information ([`FS1815`](diagnostics.md):
*'TV1' is wired as a mixing valve: a from BLR, ab to SP, b from NR.*), and the canvas labels the
ports. A two-port's inlet and outlet are not a choice and are not reported.

### A sensor in a chain

A sensor may sit in a chain, `SP - TE1 - RAD`: it becomes a node between `SP` and `RAD`, named
`TE1_node`, with the sensor reading it. A sensor may instead be placed on a named node with `at`
(`TE1 temperature_sensor at NS`); placed both ways it is [`FS1814`](diagnostics.md), and the chain's
placement is kept.

### A pipe on a connection

The line on the drawing already is the pipe, so a pipe's properties go at the end of the connection
that draws it: a length with its unit and a `DN` designation, each recognised by its form, then any
other pipe parameter by name.

```fluidscript
fluidscript 2

circuit "Loop":
  fluid = water

  PU1  pump
  HE1  heater  power = 30  out.t = 50 C
  HE2  load    power = 30  dt = 20 K

  PU1 - HE1 - HE2 - N3
  N3 - PU1                   12 m  DN25  roughness = 0.05 mm
```

- A line that carries pipe properties has **one link**; on a chain they are
  [`FS1803`](diagnostics.md), which asks which link you meant.
- The pipe is named after its ends, `N3__PU1` here, and answers to that name in results and
  expressions.
- A word after the link that is not a size, `12 m NPS1`, is [`FS1813`](diagnostics.md); the pipe
  keeps its length and is sized.

[`pipe`](pipe.md) has the rest: what a length or a `DN` left out means, and when to declare a pipe
instead.

## The role

`role` tells the layout which way heat flows through the circuit, and it is used only where the
topology has not already said:

| Role | Also written | Placed as |
|---|---|---|
| `district` | `district_heating`, `district_loop` | Source |
| `solar` | `solar_collector`, `solar_loop` | Source |
| `ground_loop` | `ground_source`, `borehole`, `brine` | Source |
| `heat_pump` | `heatpump`, `hp` | Conversion |
| `storage` | `buffer`, `accumulator`, `storage_circuit` | Storage |
| `ahu` | `air_handling_unit`, `ventilation`, `air_handler` | Consumer |
| `radiator` | `radiators`, `radiator_circuit` | Consumer |
| `underfloor` | `floor_heating`, `ufh`, `underfloor_heating` | Consumer |
| `hot_water` | `dhw`, `domestic_hot_water`, `tap_water` | Consumer |
| `heating` | `heating_circuit`, `secondary` | Neutral |
| `cooling` | `chilled_water`, `cooling_circuit`, `cooling_loop` | Neutral |
| `distribution` | `primary`, `header`, `distribution_header` | Neutral |

Any other word is placed neutrally and [`FS1519`](diagnostics.md) lists the roles; the title is never
read as a role. A role that contradicts the circuit's stated duties is overruled by them, and
[`FS2403`](diagnostics.md) says so — see
[How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md).

## See also

[`project`](project.md) · [`fluid`](fluid.md) · [`node`](node.md) · [`pipe`](pipe.md) ·
[`inlet` and `outlet`](inlet-outlet.md) · [How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md)
