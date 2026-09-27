# The shape of a line

Everything below is about how FluidScript reads the characters you type — before it knows what a
`pump` is, or what `power = 30` means. If a line is not doing what you expect, this is usually why.

## One statement per line, or a block

There is no line terminator and no line continuation. A statement ends where the line ends.

A **block** is a head whose last character is `:` — a `project`, `circuit` or `run` line, a
component declaration, a `style:` — and every line after it indented deeper than the head:

```fluidscript
fluidscript 2

circuit "Heating":
  fluid = water

  HX1  exchanger:
    primary.out.t  = 45 C
    secondary.in.t = 40 C

  PU1  pump
```

The first line at the head's own indentation or less ends the block; blank lines and comment lines
never do. Blocks nest, a declaration inside a circuit, and two levels are the most a script needs.

- **The lines of one block are indented alike.** A line indented between two levels, or a block that
  mixes tabs and spaces, is [`FS1801`](diagnostics.md), and the line is read as belonging to the
  nearer level.
- **A curve's rows are the exception**: they are a table whose columns you align, so a row needs only
  to sit deeper than its header ([`curve`](curve.md)).
- A head without its `:` is [`FS1812`](diagnostics.md). A line that belongs in a block written outside
  it — `fluid = water` at the top level — is [`FS1802`](diagnostics.md), naming the block it belongs in.

Nothing else about whitespace means anything, and FluidScript keeps your indentation exactly as you
wrote it.

## `name = value`

A setting or a parameter is a name, `=`, and a value. Spaces around the `=` are free, and several
pairs may share a line, with no commas between them:

```fluidscript
fluidscript 2

circuit "Plant":
  fluid = water

  HE1  heat_exchanger  power = 30 kW * 1.1   in.t = 20   out.t = 50
```

A value runs to the next `name =` on the line, or to the line's end, so it may hold spaces and
operators. Commas separate the items of a list (`[winter, mild]`) and nothing else.

## Comments start with `#`

```fluidscript
fluidscript 2

let flow = 1.2 kg/s    # sized from the loop
# a whole line, too
```

A `#` makes the rest of its line a comment, wherever it appears. There is one exception, and it is the
one you will meet: **a `#` inside quotes is not a comment**, which is why a hex colour is written as
text.

```fluidscript expects=FS1203
fluidscript 2

project:
  style:
    colour = #2f6f9f     # NOT a colour: everything from the # is a comment
```

That `colour` has nothing after it. FluidScript warns about this particular shape, because it would
be silent otherwise: write `colour = "#2f6f9f"`.

## Names

A name is made of letters, digits and underscores. It **may start with a digit**, so `3WV` is a
perfectly good name for a three-way valve.

A name may **not** contain a hyphen. `-` joins components on a connection line and subtracts in an
expression, so it can never be part of a name:

```fluidscript expects=FS1108
fluidscript 2

circuit "Plant":
  fluid = water

  TV1  3-way-valve      # a hyphen is impossible — write 3_way_valve
```

You never have to learn a canonical spelling for a component *kind*: `3_way_valve`, `3WayValve` and
`threewayvalve` all find the same thing. The same holds for a parameter: `HEAD = 5`, `Head = 5` and
`head = 5` are one parameter. Only the hyphen is impossible. A component's own name is different:
`PU1` and `pu1` are two components, because a name is yours and the tool has no standing to merge two
of them. A merely similar spelling never binds; it only feeds the message's suggestion.

**One name is unavailable, and it is worth knowing why.** A name that reads as a number and a unit is
that number and that unit: `3K` is three kelvin, not a component called `3K`. FluidScript says so
([`FS1003`](diagnostics.md)) and suggests a name that works, such as `K3`.

A quoted string is a **title** — `circuit "District primary":` — and never a reference to anything.

## Numbers and units

A unit may be written against the number or separated from it by a space. Both are the same value:
`power = 30kW` and `power = 30 kW`.

A unit is only recognised **immediately after a number**. Everywhere else the same characters are
arithmetic, which is what lets these two lines mean different things:

```fluidscript
fluidscript 2

let q    = 30 kW
let dt   = 20 K
let cp   = 4.18 kJ/(kg*K)     # one unit: kilojoules per kilogram per kelvin
let flow = q / (cp * dt)      # three operators: divide, multiply
```

Compound units like `kJ/(kg*K)`, `m3/h` and `l/min` are single units, not little formulas — FluidScript
never reads inside one.

**A unit is never recognised before an `=`, a `[` or a `.name`.** This is what keeps a parameter
named after a unit working. `h` is an hour and an enthalpy, so in `flow = 5  h = 2000` the `h` is
the next parameter, not five hours.

You can write a number on its own. It picks up the unit the value expects, so `power = 30` is
30 kW and `length = 25` is 25 metres. [Units](units.md) lists what a bare number means for every
quantity, and why `K` is a temperature *difference*.

A decimal point must have a digit after it: `30.5` is one number, and `30..60` is a range from 30
to 60.

## Lists, ranges and dates

- **A list** is `[a, b, …]`, one value per case in the order the project's
  [`cases`](project.md#cases) names them. A unit after the closing bracket applies to every item that
  states none: `t = [85, 70] C`.
- **A range** is `a..b`, and a unit after it applies to both ends: `30..40 min`, `85..75 C`.
- **A date** is written unquoted: `2026-01-15`, `2026-01-15 06:00` or `2026-01-15 06:00:30`. No one
  means 2026 − 1 − 15, so the shape is read as a date. A clock time, `06:30`, is written in a run's
  events.

## A port's state

A component's ports have states, and a state is written on its port: the port, a dot, and the
quantity.

```fluidscript
fluidscript 2

circuit "Substation":
  fluid = water

  HX1  heat_exchanger  power = 150 kW  in.t = 40  out.t = 60  secondary.in.t = 85  secondary.out.t = 45
```

`in.t` is the temperature entering the exchanger's first side, `secondary.out.t` the temperature
leaving its second; `primary.in.t` is another spelling of `in.t`. Ports that come in families take an
index in square brackets — a tank's `in[2]`, `layer[3]`, `out[16]` — and the first member needs none:
`in[1]` and `in` are the same port, and `in` is how it is printed. The index is a whole number
touching its name on both sides; `in[ 2 ]` and `in[a]` are [`FS1119`](diagnostics.md).

The same spelling reads the state back in an expression and names the port on a connection line:

```fluidscript
fluidscript 2

let approach = HX1.secondary.in.t - HX1.out.t

circuit "Substation":
  fluid = water

  HX1  heat_exchanger  power = 150 kW  in.t = 40  out.t = 60  secondary.in.t = 85
  PP   pump

  HX1.secondary.out - PP - N2 - HX1.secondary.in
```

The quantities are the ones [`show`](show.md) and [Properties](properties.md) use: `t`, `p`, `flow`,
`vflow`, `h`, `rho`, `cp`, and the changes `dp`, `dt`, `dh`, with their long names —
`secondary.in.temperature` is `secondary.in.t`. A `d` in front of a quantity is always its change
across the component (`dp` a drop, `dt` and `dh` a rise); `dn` is a pipe size, not a change. Which of
them a port takes is the component's business: an exchanger's inlet takes `t`, and its second inlet
also `flow`, `dp` and `dt` for the whole side; a tank's takes `level`. A quantity the port does not
take is [`FS1538`](diagnostics.md), listing the ones it does — never a guess at the nearest one. A
node has one state and no ports, so `t =` and `p =` are written bare on it; `N1 node in.t = 50` is
[`FS1537`](diagnostics.md).

**Every port has a pressure, and it is the node's.** A component changes the state between its
ports; it does not own a pressure of its own. `V1 valve out.p = 100` is the pressure at the node
`V1.out` is wired to — named, or the one inserted between two components — and means exactly

```fluidscript
fluidscript 2

circuit "Plant":
  fluid = water

  V1  valve
  N1  node  p = 100

  V1 - N1
```

So `PU1 pump in.p = 100` pins the suction node the way `N1 node p = 100` would, and stating a node's
pressure twice — on the node and on a port, or on two ports that meet there — is
[`FS1539`](diagnostics.md), whichever line came second. `dp = 15` on the same valve constrains only
the difference across it and pins neither node; `out.p` pins one. A pressure pinned inside a closed
loop is that loop's datum; two pinned inside one loop are one more than the loop can satisfy, and
[`FS2210`](diagnostics.md) names both. `HX1.secondary.in.p` reads the same node's solved pressure
back. The two spellings name one point only across a bare connection: `N1 - PU1  10 m  DN25` puts a
pipe between them, and `PU1.in.p` is then the node at the pipe's far end — the pump's suction, 10 m
of friction below `N1` — not `N1` itself.

## Text

Double quotes, and a piece of text never spans a line: a title, `circuit "District primary":`, a
colour, `colour = "#2f6f9f"`, a date format, `format="dd/MM/yyyy"`.

There are no escape sequences. If you leave a quote off, FluidScript tells you rather than swallowing
the rest of the file.

## Statement words

These words open a statement at the start of a line, so a component cannot be named one of them
([`FS1004`](diagnostics.md)). Nothing is reserved anywhere else: before an `=` each is an ordinary
setting's name (a controller's `curve = heating`), and component kinds such as `pump`, `node` and
`pipe` are not reserved either, so a component called `pipe` is legal. `at` and `over` open a
statement only inside a run.

<!-- BEGIN GENERATED: statement-words -->
| Word | Opens |
|---|---|
| `fluidscript` | the version line every script opens with |
| `project` | the project block: its title, its cases, its catalogue and its presentation |
| `let` | a named value; a list makes it a driver, one value per case |
| `curve` | a named table read against its driver, interpolated between its rows |
| `circuit` | a circuit block: its fluid, its components and their connections |
| `run` | a run block: what happens to the plant in time |
| `at` | a step, inside a run only |
| `over` | a ramp, inside a run only |
<!-- END GENERATED: statement-words -->

`|` is not used for anything, and is deliberately kept free.

`sized_at` is not reserved either. `sized_at.outdoor = -5 C` is a setting on a component's
declaration, one per `let` it names — see
[A component's own sizing point](project.md#a-components-own-sizing-point).

## Your formatting is yours

FluidScript never reformats what you wrote. Spacing, alignment, blank lines, the column your comments
sit in, whether you wrote `power = 30kW` or `power = 30 kW`, a declaration on one line or as a block —
all of it is kept exactly, including when the diagram writes a change back into the script. Editing
a valve's size on the canvas changes that one value and nothing else on the line, and nothing at all
on any other line.

That is deliberate, and it has one visible consequence: after a write-back your comment columns can
end up misaligned, because realigning them would mean changing lines you did not touch. Tidying is a
separate command you run when you want it (`Shift+Alt+F`): it indents each block body two spaces and
leaves a curve's rows as written, and it is one undo step.

## When something is not read the way you meant

| You wrote | FluidScript read | Because |
|---|---|---|
| `colour = #2f6f9f` | a `colour` with nothing after it | `#` starts a comment |
| `3-way-valve` | an error, [`FS1108`](diagnostics.md) | a name cannot contain `-` |
| `3K pump` | an error, [`FS1003`](diagnostics.md) | the name reads as a quantity |
| `let x = 5 - 3` | subtraction | `-` is not a unit |
| `head = 15` | 15 m of the fluid being pumped | head is a length of the pumped fluid; see [Units](units.md) |
| `circuit "Heating"` | an error, [`FS1812`](diagnostics.md) | a block head ends with `:` |
| `in[ 2 ].t = 85` | an error, [`FS1119`](diagnostics.md) | an index touches its name and holds a whole number |
| `N1 node in.t = 50` | an error, [`FS1537`](diagnostics.md) | a node has one state and no ports: `t = 50` |
