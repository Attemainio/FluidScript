# style

How the plant is drawn: a `style:` block in the [`project`](project.md) block, and in a
[`circuit`](circuit.md) block to draw that circuit differently.

```fluidscript
fluidscript 2

project "Substation 12":
  style:                                # the drawing's defaults
    colour = "#2f6f9f"                  # quoted, since '#' starts a comment
    width  = 2
    corner = fillet
    line   = solid

circuit "District primary":
  fluid = water
  style:
    colour = crimson                    # this circuit in crimson, at the project's width

circuit "Heating":
  fluid = water
```

| Setting | Meaning | Values |
|---|---|---|
| `colour` | The stroke colour. `color` is the same setting | A colour name, or a quoted `"#rrggbb"` |
| `width` | The line width | Pixels, bare or in `px`: `2`, `1.5px` |
| `corner` | How a route turns | `sharp` or `fillet` |
| `line` | The line's pattern | `solid`, `dashed`, `dotted` or `dashdot` |

Colour names are the CSS names a browser accepts -- `steelblue`, `crimson`, `gray` and the rest.

## Rules

- **The project's style is the default; a circuit's overrides only what it states.** Above, the
  district primary draws in crimson at the project's width, corners and line, and the heating circuit
  draws entirely in the project's style.
- What no style states is drawn in the theme's default, so a script with no `style:` block is still a
  finished drawing.
- While [`show`](show.md) is active the colour scale paints each symbol's fill; the stroke is always
  the style's.
- **A colour must be quoted.** `#` starts a comment, so `colour = #2f6f9f` is a `colour` with nothing
  after it -- FluidScript warns about that shape ([`FS1203`](diagnostics.md)), because it is silent
  otherwise.
- A setting the block does not have is [`FS1503`](diagnostics.md), and a value outside the table above
  is [`FS1514`](diagnostics.md), naming what the setting accepts. A setting stated twice in one block
  is [`FS1202`](diagnostics.md), and the later one wins.
- A style belongs to the project or a circuit. There are no named styles and no style per component.

## See also

[`show`](show.md) · [`spacing`](spacing.md) · [`project`](project.md) · [`circuit`](circuit.md)
