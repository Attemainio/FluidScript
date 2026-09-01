# The shape of a line

Everything below is about how FluidScript reads the characters you type — before it knows what a
`pump` is, or what `power=30` means. If a line is not doing what you expect, this is usually why.

## One statement per line

There is no line terminator and no line continuation. A statement ends where the line ends, and
indentation means nothing — indent to make a block readable, and FluidScript will keep your
indentation exactly as you wrote it.

## Comments start with `#`

```fluidscript
HE1 heat_exchanger power=30    # 30 kW, sized from the loop
# a whole line, too
```

A `#` makes the rest of its line a comment, wherever it appears. There is one exception, and it is the
one you will meet: **a `#` inside quotes is not a comment**, which is why a hex colour is written as
text.

```fluidscript expects=FS1203
style "#2f6f9f" 2px      # a colour
style #2f6f9f 2px        # NOT a colour: everything from the # is a comment
```

The second line is still a valid `style` directive — it just has nothing in it, so the diagram renders
in the default colour with no complaint. FluidScript warns about this particular shape, because it is
silent otherwise.

## Names

A name is made of letters, digits and underscores. It **may start with a digit**, so `3WV` is a
perfectly good name for a three-way valve.

A name may **not** contain a hyphen. `-` joins components in the `connections` section and subtracts in
an expression, so it can never be part of a name:

```fluidscript expects=FS1108
TV1 3-way-valve      # a hyphen is impossible — write 3_way_valve
```

You never have to learn a canonical spelling for a component *kind*: `3_way_valve`, `3WayValve` and
`threewayvalve` all find the same thing. Only the hyphen is impossible.

**One name is unavailable, and it is worth knowing why.** A name that reads as a number and a unit is
that number and that unit: `3K` is three kelvin, not a component called `3K`. FluidScript says so and
suggests a name that works, such as `K3`.

## Numbers and units

A unit may be written against the number or separated from it by a space. Both are the same value:

```fluidscript
PU1 pump power=30kW
PU2 pump power=30 kW      # the same value
```

A unit is only recognised **immediately after a number**. Everywhere else the same characters are
arithmetic, which is what lets these two lines mean different things:

```fluidscript
let cp   = 4.18 kJ/(kg*K)     # one unit: joules per kilogram per kelvin
let mdot = Q / (cp * dT)      # three operators: divide, multiply
```

Compound units like `kJ/(kg*K)`, `m3/h` and `l/min` are single units, not little formulas — FluidScript
never reads inside one.

**A unit is never recognised before an `=`.** This is what keeps a parameter named after a unit
working:

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50
```

`in` is the symbol for inches *and* the name of the inlet-temperature parameter. Because `=` follows
it, this line is three parameters — not thirty inches.

You can write a number on its own. It picks up the unit the parameter expects, so `power=30` is
30 kW and `length=25` is 25 metres. [Units](units.md) lists what a bare number means for every
quantity.

A decimal point must have a digit after it: `30.5` is one number, and `30..60` is a range from 30
to 60.

## Text

Double quotes, and a piece of text never spans a line:

```fluidscript
style "#2f6f9f"
```

There are no escape sequences. If you leave a quote off, FluidScript tells you rather than swallowing
the rest of the file.

## Reserved words

These words start a statement, so they cannot be used as names. Everything else is available —
component kinds such as `pump`, `node` and `pipe` are **not** reserved, so a component called `pipe`
is legal.

<!-- BEGIN GENERATED: reserved-words -->
| Word | Introduces |
|---|---|
| `fluidscript` | the version line every script opens with |
| `project` | the project name, and the default for how the file is solved |
| `circuit` | a circuit, and everything that follows until the next one |
| `fluid` | what a circuit carries, and how it is solved |
| `dynamic` | solving in time — qualifies `project` or `fluid` |
| `static` | solving as a steady state — qualifies `project` or `fluid` |
| `spacing` | how far apart components are drawn |
| `style` | how the following components are drawn |
| `show` | which property the colour scale follows |
| `let` | a name for a value you use more than once |
| `catalog` | which catalogue sizes are chosen from |
| `connections` | a circuit's topology |
| `schedule` | what changes, and when, during a run |
| `supply` | where a subcircuit takes flow from its parent |
| `return` | where a subcircuit gives that flow back |
| `control` | which controller drives what, measuring what |
<!-- END GENERATED: reserved-words -->

`|` is not used for anything, and is deliberately kept free.

## Your formatting is yours

FluidScript never reformats what you wrote. Spacing, alignment, blank lines, the column your comments
sit in, whether you wrote `power=30kW` or `power=30 kW` — all of it is kept exactly, including when
the diagram writes a change back into the script. Editing a valve's size on the canvas changes that
one value and nothing else on the line, and nothing at all on any other line.

That is deliberate, and it has one visible consequence: after a write-back your comment columns can
end up misaligned, because realigning them would mean changing lines you did not touch. Tidying is a
separate command you run when you want it (`Shift+Alt+F`), and it is one undo step.

## When something is not read the way you meant

| You wrote | FluidScript read | Because |
|---|---|---|
| `style #2f6f9f 2px` | an empty `style` | `#` starts a comment |
| `3-way-valve` | three tokens | a name cannot contain `-` |
| `3K pump` | three kelvin, then `pump` | the name reads as a quantity |
| `let x = 5 - 3` | subtraction | `-` is not a unit |
| `head=15` | 15 m of the fluid being pumped | head has no unit symbol; see [Units](units.md) |
