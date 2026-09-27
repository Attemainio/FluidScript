# Reference

Every part of the script language, one page each. Start with the
[tutorial](../tutorial/) if you have not written a FluidScript circuit before.

## The language

| Page | What it covers |
|---|---|
| [The shape of a line](syntax.md) | Blocks and indentation, `name = value`, comments, names, numbers and units, lists and dates, statement words |

## Statements

| Page | What it opens |
|---|---|
| [`fluidscript`](fluidscript.md) | The version line every script opens with |
| [`project`](project.md) | The project block: its title, its cases, what the plant is sized for, and a component's own sizing point |
| [`let`](let.md) | A named value, or a driver with one value per case |
| [`curve`](curve.md) | A named table read against its driver, interpolated between its rows |
| [`circuit`](circuit.md) | A circuit block: its fluid, its components, and the connection lines that join them |
| [`run`](run.md) | What happens to the plant in time: where it starts, its overrides and its events (`at`, `over`) |

## Settings

| Page | Setting of | What it says |
|---|---|---|
| [`catalog`](catalog.md) | the project | Which catalogue sizes are chosen from |
| [`show`](show.md) | the project | Which property the colour scale follows, and its `scale` |
| [`spacing`](spacing.md) | the project | How far apart components are drawn |
| [`style`](style.md) | the project, a circuit | How the plant is drawn: colour, width, corners, line |
| [`fluid`](fluid.md) | a circuit | What the circuit carries |

## Components

| Page | What it is |
|---|---|
| [`node`](node.md) | A point with a state and no extent — the junction |
| [`inlet` and `outlet`](inlet-outlet.md) | Where fluid enters and leaves the model |
| [`pipe`](pipe.md) | A pressure drop between two nodes |
| [`heat_exchanger`](heat-exchanger.md) | Heat source, heat consumer, or a real two-sided exchanger |
| [`valve`](valve.md) | A controllable resistance |
| [`three_way_valve`](three-way-valve.md) | Mixing or diverting, on three ports |
| [`pump`](pump.md) | What makes the fluid move |
| [`tank`](tank.md) | A finite-volume, optionally stratified store |
| [`t_sensor`](t-sensor.md) | A temperature sensor |
| [`p_sensor`](p-sensor.md) | A pressure sensor |
| [`flow_sensor`](flow-sensor.md) | A flow sensor |
| [`controller`](controller.md) | A control loop in one declaration: what it moves, what it reads, its setpoint and tuning |

## Generated reference

| Page | What it lists |
|---|---|
| [Diagnostics](diagnostics.md) | Every message FluidScript can show, with its code and severity |
| [Units](units.md) | What a bare number means, and every unit you can write |
| [Properties](properties.md) | Every value you can read back off a component |
| [Equipment tags](tags.md) | Every kind's tag code, and the tag it produces |
| [The model contract](model-contract.md) | The one JSON document every consumer receives, field by field |
