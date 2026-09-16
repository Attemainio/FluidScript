# style

How the components after it are drawn, and named styles a circuit can adopt.

```fluidscript
fluidscript 1
style "#2f6f9f" 2px sharp --            # applies to everything declared after it

style hot = "#c0392b" 2px -              # defines a named style
style cold = "#2f6f9f" 2px - fill="#e8f1f8"

circuit heating 100
style hot                                # this circuit draws in `hot`
HS1  heat_exchanger power=54 kW out=60

circuit ahu 101
style cold
TV  three_way_valve style=hot            # one component overrides
```

Arguments are positional and each one is read for what it is: a colour, a width, a corner treatment, a
line pattern. Order between different kinds of argument does not matter.

| Argument | Read as | Examples |
|---|---|---|
| a colour name, or a quoted `#rrggbb` | the stroke colour | `blue`, `"#2f6f9f"` |
| `fill=<colour>` | the fill colour of closed shapes | `fill=lightgray`, `fill="#e8f1f8"` |
| a number, bare or in `px` | the line width | `2px`, `1.5` |
| `fillet`, `round`, `sharp` | the corner treatment | |
| `-`, `--`, `..`, `-.` | the line pattern: solid, dashed, dotted, dash-dot | |

Colour names are the CSS names a browser accepts -- `steelblue`, `crimson`, `gray` and the rest.

## Rules

- `style <name> = ...` **defines** a style; the `=` is what makes it a definition. It may appear
  anywhere in the file, before or after its use.
- `style <name>` **applies** a defined style to everything declared after it in the current circuit.
  A circuit starts from the project's style -- the `style` lines before the first `circuit` -- not
  from the previous circuit's.
- `style=<name>` on a component applies a defined style to that one component, over the circuit's.
- A `style` line with no name applies its arguments the same way, anonymously, as it always has.
- Layering: a later style changes only what it states. `style hot` after `style 2px` keeps the width.
- While [`show`](show.md) is active the colour scale paints each symbol's fill; the style's `fill` is
  what shows when it is not. The stroke is always the style's.
- What no style states is drawn in the theme's default, so a script with no `style` lines is still a
  finished drawing.
- **A colour must be quoted.** `#` starts a comment, so `style #2f6f9f 2px` is an empty `style`
  followed by a comment -- FluidScript warns about that particular shape, because it is silent
  otherwise.
- An applied name nothing defined is a warning and changes nothing; a name defined twice is a warning
  and the later definition wins; an argument that is none of the kinds above is a warning and ignored.

## See also

[`show`](show.md) · [`spacing`](spacing.md) · [The shape of a line](syntax.md)
