# show

Which property the diagram's colour scale follows.

```fluidscript
fluidscript 1
show temperature
show temperature pressure 0..40
```

Name one or more properties, and optionally the scale's range as `min..max`. With no range the scale
fits the solved values.

## What it resolves to

The first `show` line is used. Each property can be written long or short — the same spellings a
[port's state](syntax.md#a-ports-state) and a [property](properties.md) use:

| Long | Short | Also | Where it is read |
|---|---|---|---|
| `temperature` | `t` | `temp` | Every node |
| `pressure` | `p` | | Every node, gauge |
| `flow` | `flow` | `mdot`, `mflow`, `mass_flow` | Every connection |
| `volume_flow` | `vflow` | `q` | Every connection, at the solved density |
| `pressure_drop` | `dp` | | Components, with zero at the scale's middle |
| `enthalpy` | `h` | | Every node |
| `density` | `rho` | | Every node |

With no `show` at all the diagram follows `temperature`. The scale's range is the solved minimum and
maximum, rounded outward to legend ticks; a stated `min..max` is used as written and fixes the first
property's range. Before a solve the range is empty and everything draws in the neutral colour.

The properties you list are the ones the legend's switcher offers, after which `temperature`,
`pressure` and `flow` always follow; every listed property travels with its own range, so switching
in the diagram needs no recompile. What the diagram receives is the `visualization` block of the
[model contract](model-contract.md).

## What it says when something is wrong

None of these stops the diagram: a `show` that cannot be followed falls back to what can.

| Code | When |
|---|---|
| `FS1210` | A name the scale does not know: `show speed`. The name is skipped; the message lists what is available. |
| `FS1213` | The same property twice in one line. The second is ignored. |
| `FS1214` | A second `show` line. Only the first is read. |

Which parts of a plant are coloured, how a pipe's gradient is drawn and what the legend does are in
[the canvas](../advanced/the-canvas.md#the-colour-scale).

## See also

[`style`](style.md) · [`spacing`](spacing.md)
