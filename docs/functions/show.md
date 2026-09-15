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

The first `show` line is used. Each property can be written long or short:

| Long | Short | Where it is read |
|---|---|---|
| `temperature` | `t` | Every node |
| `pressure` | `p` | Every node, gauge |
| `flow` | `mdot` | Every connection |
| `pressure_drop` | `dp` | Components, with zero at the scale's middle |
| `enthalpy` | `h` | Every node |
| `density` | `rho` | Every node |

With no `show` at all the diagram follows `temperature`. The scale's range is the solved minimum and
maximum, rounded outward to legend ticks; a stated `min..max` is used as written. Before a solve the
range is empty and everything draws in the neutral colour. What the diagram receives is the
`visualization` block of the [model contract](model-contract.md).

## See also

[`style`](style.md) · [`spacing`](spacing.md)
