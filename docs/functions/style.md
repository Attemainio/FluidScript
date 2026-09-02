# style

How the components after it are drawn.

```fluidscript
fluidscript 1
style "#2f6f9f" 2px sharp --
```

Arguments are positional and each one is read for what it is: a colour, a width, a corner treatment, a
line pattern. Order between different kinds of argument does not matter.

## Rules

- A style applies to everything declared after it, until the next `style` line.
- **A colour must be quoted.** `#` starts a comment, so `style #2f6f9f 2px` is an empty `style`
  followed by a comment — FluidScript warns about that particular shape, because it is silent
  otherwise.
- Line patterns are written from `-` and `.`: `--`, `..`, `-.`.

## See also

[`show`](show.md) · [`spacing`](spacing.md) · [The shape of a line](syntax.md)
