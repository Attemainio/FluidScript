# let

Names a value you use more than once, or one that differs between the plant's cases.

```fluidscript
fluidscript 2

let dt_design = 20 K
let q_total   = 120 kW
let flow      = q_total / (4.18 kJ/(kg*K) * dt_design)
```

A `let` is a value, not a component. It can be used anywhere a value can, including inside another
`let`, and it sits at the top level of the file, outside any block.

## Rules

- The value may be a number, a quantity, an expression, a list, or a reference to a component's
  property.
- Units are checked: `20 C + 30 C` is an error, because two temperatures do not add. A temperature
  and a temperature *difference* do, and `K` is a difference: `20 C + 30 K` is 50 °C.
- A `let` that refers to itself, directly or through a chain, is reported once and names both ends
  rather than looping.
- An expression may nest up to 64 levels of parentheses, function arguments and minus signs. Deeper
  than that, the line is reported as unreadable. A chain of `let`s each reading the next has no such
  limit.

## One value per case

A `let` whose value is a list has one value per case, in the order the project's
[`cases`](project.md#cases) names them. That is a **driver**: a value the plant's cases differ by.

```fluidscript
fluidscript 2

project "Substation 12":
  cases = [winter, mild]

let outdoor = [-26, 5] C              # −26 °C in winter, 5 °C in mild
let supply  = outdoor + 2 K           # reads a driver, so it varies per case too

curve heat_demand: outdoor
  -26   150
   18     0

circuit "Heating":
  fluid = water

  RAD  radiator  power = heat_demand  # 150 kW in winter, 44.3 kW in mild
```

- The unit after the closing bracket applies to every item that states none, so `[-26, 5] C` is
  −26 °C and 5 °C.
- A driver may be of any quantity — an outdoor temperature, a production rate, a network pressure.
  Nothing about `outdoor` is built in; a plant with no driver writes none.
- Anything that reads a driver varies with it — another `let`, a [`curve`](curve.md) driven by it, a
  parameter — and is evaluated once per case.
- A list that does not have one value per case is [`FS1540`](diagnostics.md), and a list in a file
  that declares no cases is [`FS1541`](diagnostics.md).
- A [`run`](run.md) may hand a driver another value, or a curve of time, for that run only
  (`outdoor = weather_jan`).

A component that should be sized at another value of a driver than the cases give it — the heat pump
of a bivalent pair — names the `let` in a `sized_at.` setting: see
[A component's own sizing point](project.md#a-components-own-sizing-point).

## Reading a value the solve produces

A `let` or a parameter may read a value that exists only after the solve: a node's temperature, an
exchanger's leaving temperature, a solved drop.

```fluidscript
fluidscript 2

circuit "Plant":
  fluid = water

  HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50  secondary.in.t = 85  secondary.in.flow = 0.4
  HE2  heat_exchanger  power = 20  secondary.in.t = HE1.secondary.out.t  secondary.in.flow = 0.4
```

`HE2` is fed with the water `HE1` leaves. Such a line is *deferred*: the run first solves without it,
evaluates it against that solution, writes the result in as the parameter's stated value, and solves
again, until two passes agree. The [solve report](../advanced/reading-the-solve-report.md#values-chosen-by-a-sizing-rule)
lists what was written in and on which pass.

- **A stated value reads as stated.** `HX1.secondary.in.t` on an exchanger whose profile states
  `secondary.in.t = 85` is 85 °C whatever the solve did. Leave the profile point out to read the
  solved inlet.
- **The dimension is checked when the value exists.** `PU1 pump head = 1.2 * HE1.dp` writes a
  pressure into metres of fluid, and nothing converts one to the other. The first pass that evaluates
  it says so ([`FS1304`](diagnostics.md)), with the definition, and the head is sized as if the line
  were absent. Divide by a density and `g` (below) and it is a length, which a head accepts.
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
fluidscript 2

circuit "Plant":
  fluid = water

  HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50
  PU1  pump  head = 1.2 * HE1.dp / (998 kg/m3 * g)
```

Any length will do there, `head = 12 m` or a `let` in metres, so check what you divided by: the
language converts nothing for you, and the density is yours to state.

## See also

[`project`](project.md) · [`curve`](curve.md) · [`run`](run.md) · [Units](units.md) ·
[The shape of a line](syntax.md)
