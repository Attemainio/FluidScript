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
- An expression may nest up to 64 levels of parentheses, function arguments and minus signs. Deeper
  than that, the line is reported as unreadable. A chain of bindings each reading the next has no
  such limit.

## Reading a value the solve produces

A binding or a parameter may read a value that exists only after the solve: a node's temperature, an
exchanger's leaving temperature, a solved drop.

```fluidscript
fluidscript 1
HE1 heat_exchanger power=30 in.t=20 out.t=50 in[2].t=85 in[2].flow=0.4
HE2 heat_exchanger power=20 in[2].t=HE1.out[2].t in[2].flow=0.4
```

`HE2` is fed with the water `HE1` leaves. Such a line is *deferred*: the run first solves without it,
evaluates it against that solution, writes the result in as the parameter's stated value, and solves
again, until two passes agree. The [solve report](../advanced/reading-the-solve-report.md#values-chosen-by-a-sizing-rule)
lists what was written in and on which pass: `HE2.in[2].t 67.146 °C from \`HE1.out[2].t\` at pass 1`.

- **A stated value reads as stated.** `HX1.in[2].t` on an exchanger whose profile states
  `in[2].t=85` is 85 °C whatever the solve did. Leave the profile point out to read the solved inlet.
- **The dimension is checked when the value exists.** `PU1 pump head=1.2*HE1.dp` writes a pressure
  into metres of fluid, and nothing converts one to the other. The first pass that evaluates it says
  so ([`FS1304`](diagnostics.md)), and the head is sized as if the line were absent.
- **A line no pass can evaluate is reported, not ignored** ([`FS1410`](diagnostics.md)). Two
  exchangers each reading the other's leaving temperature is the usual case: neither has a design
  point until the other is rated, so neither ever is. The parameter is then chosen by its sizing rule.
- **A value that keeps moving is reported with its last three values** ([`FS1405`](diagnostics.md))
  at the pass cap, and the last value stands. That happens when the value is anchored by nothing but
  the parameter it sets.
- **Only what the script states anchors the first pass.** A parameter that reads a solved value is
  absent from the first solve, so the circuit has to be well posed without it. An inlet whose only
  temperature is such a reference has nothing to solve from.
- **A node read by name is a node you declared.** `N3.t` needs an `N3 node` line; a node that exists
  only on a connection line is not a name an expression sees.

## See also

[Units](units.md) · [The shape of a line](syntax.md)
