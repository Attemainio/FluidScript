# spacing

How far apart components are drawn.

```fluidscript
fluidscript 1
spacing 0.75
```

A drawing setting and nothing else: it changes the diagram, never the model. The number is in the
diagram's own units -- a pump is one unit across -- so it takes no unit symbol; `spacing 20 mm` is an
error rather than a conversion. `spacing 20` is accepted and draws every component twenty pumps
apart; a margin between 0.5 and 1 is what a readable diagram wants.

The value is the **margin** every component keeps from every other: each symbol's box is grown by
this amount on every side, and no component's box may enter another's grown box. Without a `spacing`
line the margin is 0.5. Pipes leave a component straight for half the margin before they turn.
[How the diagram is arranged](../advanced/how-the-diagram-is-arranged.md) has the rest.

## See also

[`style`](style.md) · [`show`](show.md)
