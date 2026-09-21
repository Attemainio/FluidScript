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
  so ([`FS1304`](diagnostics.md)), with the definition, and the head is sized as if the line were
  absent. Divide by a density and `g` (below) and it is a length, which a head accepts.
- **A line no pass can evaluate is reported, not ignored** ([`FS1410`](diagnostics.md)). Two
  exchangers each reading the other's leaving temperature is the usual case: neither has a design
  point until the other is rated, so neither ever is. The parameter is then chosen by its sizing rule.
  When the run fails at a pass the line was absent from, the report says that instead
  ([`FS1412`](diagnostics.md)): the line was still waiting when the pass failed, and its absence is
  often why. A value the seed can supply -- one read from a stated anchor -- is never in that
  position; it is stated before the first pass.
- **A value that keeps moving is reported with its last three values** ([`FS1405`](diagnostics.md))
  at the pass cap, and the last value stands. That happens when the value is anchored by nothing but
  the parameter it sets.
- **Only what the script states anchors the first pass.** A parameter that reads a solved value is
  absent from the first solve, so the circuit has to be well posed without it. An inlet whose only
  temperature is such a reference has nothing to solve from.

## Constants

Two names are reserved and can be read anywhere a value can:

| Name | Value | What it is |
|---|---|---|
| `pi` | 3.14159… | π |
| `g` | 9.80665 m/s² | standard gravity |

They are names, not units, so they take an operator: `2 * g` is twice gravity, while `2 g` is two
grams. A `let` of either name is refused ([`FS1411`](diagnostics.md)).

The use they were added for is a pump head from a pressure. A head is metres of the pumped fluid,
`dp / (rho * g)`, and a `head` parameter accepts any value in metres:

```fluidscript
fluidscript 1
HE1 heat_exchanger power=30 in.t=20 out.t=50
PU1 pump head=1.2*HE1.dp/(998 kg/m3*g)
```

Any length will do there, `head=12 m` or a `let` in metres, so check what you divided by: the
language converts nothing for you, and the density is yours to state.

## See also

[Units](units.md) · [The shape of a line](syntax.md)
