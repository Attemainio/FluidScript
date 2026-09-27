# spacing

How far apart components are drawn: a setting of the [`project`](project.md) block.

```fluidscript
fluidscript 2

project "Plant room":
  spacing = 0.75
```

A drawing setting and nothing else: it changes the diagram, never the model. The number is in the
diagram's own units -- a pump is one unit across -- so it takes no unit symbol; `spacing = 20 mm` is
[`FS1514`](diagnostics.md) rather than a conversion. `spacing = 20` is accepted and draws every
component twenty pumps apart; a margin between 0.5 and 1 is what a readable diagram wants. It is the
project's only: a circuit block does not take it.

The value is the **margin** every component keeps from every other: each symbol's box is grown by
this amount on every side, and no component's box may enter another's grown box. Without a `spacing`
setting the margin is 0.5. Pipes leave a component straight for half the margin before they turn.
[How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md) has the rest.

## See also

[`project`](project.md) · [`style`](style.md) · [`show`](show.md)
