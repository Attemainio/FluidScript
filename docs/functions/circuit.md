# circuit

Opens a circuit, and everything that follows belongs to it until the next `circuit` line.

```fluidscript
fluidscript 1
circuit coolingLoop
circuit primary 200
```

## Rules

- `circuit <name>` is enough. A number is optional.
- Numbers you do not write are resolved in declaration order: 100, 200, 300. **A number you do write
  is kept verbatim**, and a number you did not write is never written into your file.
- The number is what makes equipment tags unique across circuits: `101PU01` and `201PU01` are pumps in
  different circuits, and neither collides with the other.
- A `circuit` line ends whatever section the previous circuit was in and opens the new one's
  declaration section.

## What the name says

The name is yours, but the diagram reads it. A name that matches one of these — or is close enough to
one, `radiators` for `radiator`, `chilled_water` for `cooling` — tells the layout which way heat flows
through the circuit, and that is used only where the topology has not already said:

| Name | Also written | Placed as |
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

Any other name is neutral, which is not an error: the circuit is placed by what it connects to. A
name that contradicts the circuit's stated duties is overruled by them, and `FS2403` says so — see
[How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md).

## See also

[`project`](project.md) · [`connections`](connections.md) · [`supply` and `return`](supply-return.md)
· [How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md)
