# show

Which property the diagram's colour scale follows: a setting of the [`project`](project.md) block,
with `scale` beside it for the scale's range.

```fluidscript
fluidscript 2

project "Plant room":
  show  = [temperature, pressure]
  scale = 0..40 C
```

Name one property, or several as a list. `scale` fixes the range of the first one as `min..max`, the
unit after it applying to both ends and matching what the property is measured in (`0..40 C` on a
temperature is [`FS1514`](diagnostics.md) on a pressure drop). With no `scale` the range fits the
solved values.

## What it resolves to

Each property can be written long or short — the same spellings a
[port's state](syntax.md#a-ports-state) and a [property](properties.md) use:

| Long | Short | Also | Where it is read |
|---|---|---|---|
| `temperature` | `t` | `temp` | Every node |
| `pressure` | `p` | | Every node, gauge |
| `flow` | `flow` | `mdot`, `mflow`, `mass_flow` | Every connection |
| `volume_flow` | `vflow` | `q` | Every connection, at the solved density |
| `enthalpy` | `h` | | Every node |
| `density` | `rho` | | Every node |
| `specific_heat` | `cp` | | Every node |
| `pressure_drop` | `dp` | | Components: inlet less outlet, zero at the scale's middle; negative across a pump |
| `temperature_change` | `dt` | | Components: outlet less inlet, zero at the middle; negative across a cooler |
| `enthalpy_change` | `dh` | | Components: outlet less inlet, the duty per kilogram |

**A `d` in front of a quantity is its change across the component.** `dp` is a drop (in − out, the
datasheet's number), `dt` and `dh` are rises (out − in). A node has no change and is left off those
scales.

With no `show` at all the diagram follows `temperature`. The scale's range is the solved minimum and
maximum, rounded outward to legend ticks; the ends are first settled to the legend's own precision, so
an inlet stated at 45 °C that solves a hair under 45 does not open an empty band down to 40, and a plant
sitting on its pressure datum reads `all 0 kPa`. A stated `scale` is used as written and fixes the
first property's range. Before a solve the range is empty and everything draws in the neutral colour.

The properties you list are the ones the legend's switcher offers, after which `temperature`,
`pressure` and `flow` always follow; every listed property travels with its own range, so switching
in the diagram needs no recompile. What the diagram receives is the `visualization` block of the
[model contract](model-contract.md).

## What it says when something is wrong

None of these stops the diagram: a `show` that cannot be followed falls back to what can.

| Code | When |
|---|---|
| `FS1210` | A name the scale does not know: `show = speed`. The name is skipped; the message lists what is available. |
| `FS1213` | The same property twice in one list. The second is ignored. |
| `FS1214` | A second `show` setting. Only the first is read. |

Which parts of a plant are coloured, how a pipe's gradient is drawn and what the legend does are in
[the canvas](../advanced/the-canvas.md#the-colour-scale).

## See also

[`style`](style.md) · [`spacing`](spacing.md)
