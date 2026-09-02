# Reference

Every part of the script language, one page each. Start with the
[tutorial](../tutorial/) if you have not written a FluidScript circuit before.

## The language

| Page | What it covers |
|---|---|
| [The shape of a line](syntax.md) | Comments, names, numbers and units, text, reserved words |

## Directives

| Page | What it introduces |
|---|---|
| [`fluidscript`](fluidscript.md) | The version line every script opens with |
| [`project`](project.md) | The project name, and the default for how the file is solved |
| [`circuit`](circuit.md) | A circuit, and everything that follows until the next one |
| [`fluid`](fluid.md) | What a circuit carries, and how it is solved |
| [`catalog`](catalog.md) | Which catalogue sizes are chosen from |
| [`let`](let.md) | A name for a value you use more than once |
| [`spacing`](spacing.md) | How far apart components are drawn |
| [`style`](style.md) | How the following components are drawn |
| [`show`](show.md) | Which property the colour scale follows |

## Sections and statements

| Page | What it introduces |
|---|---|
| [`connections`](connections.md) | A circuit's topology |
| [`schedule`](schedule.md) | What changes, and when, during a run |
| [`supply` and `return`](supply-return.md) | Where a subcircuit joins its parent |
| [`control`](control.md) | Which controller drives what, measuring what |

## Components

| Page | What it is |
|---|---|
| [`node`](node.md) | A point with a state and no extent — the junction |
| [`pipe`](pipe.md) | A pressure drop between two nodes |
| [`heat_exchanger`](heat-exchanger.md) | Heat source, heat consumer, or a real two-sided exchanger |
| [`valve`](valve.md) | A controllable resistance |
| [`three_way_valve`](three-way-valve.md) | Mixing or diverting, on three ports |
| [`pump`](pump.md) | What makes the fluid move |
| [`tank`](tank.md) | A finite-volume, optionally stratified store |
| [`controller`](controller.md) | A PI or PID controller |

## Generated reference

| Page | What it lists |
|---|---|
| [Diagnostics](diagnostics.md) | Every message FluidScript can show, with its code and severity |
| [Units](units.md) | What a bare number means, and every unit you can write |
| [Properties](properties.md) | Every value you can read back off a component |
| [Equipment tags](tags.md) | Every kind's tag code, and the tag it produces |
