# let

Names a value you use more than once.

```fluidscript
fluidscript 1
let dTdesign = 20 K
let Qtotal   = 120 kW
let mdot     = Qtotal / (4.18 kJ/(kg*K) * dTdesign)
```

A binding is a value, not a component. It can be used anywhere a value can, including inside another
binding.

## Rules

- The value may be a number, a quantity, an expression, or a reference to another component's
  property.
- Units are checked: `20 °C + 30 °C` is an error, because two temperatures do not add. A temperature
  and a temperature *difference* do.
- A binding that refers to itself, directly or through a chain, is reported once and names both ends
  rather than looping.

## See also

[Units](units.md) · [The shape of a line](syntax.md)
