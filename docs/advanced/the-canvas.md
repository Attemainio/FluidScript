# The canvas

The drawing beside your script is the plant as FluidScript understood it: one symbol per
component, one line per pipe, laid out by the compiler and drawn by the browser. It appears a
moment after you stop typing and follows every edit. Nothing about it is typed: where things go is
decided from the circuit ([How the diagram is arranged](how-the-diagram-is-arranged.md)), and the
canvas only draws what it is given.

## Moving around

The canvas works like a CAD viewport. Y is up: a component above another has the larger Y.

| Do | To |
|---|---|
| Scroll the wheel | Zoom about the pointer, from 10 % to 1000 % |
| Shift and scroll, or scroll sideways on a trackpad | Pan |
| Drag with the middle button, or hold Space and drag | Pan |
| `F` | Fit the whole drawing with a small margin |
| `Home` | Back to 100 % with the origin at the centre |

The zoom is shown in the corner. Click the canvas first to give it the keyboard; Tab then walks
the components in the order the compiler numbered them.

A new document opens fitted. After that the view is yours: a recompile redraws the plant but does
not move your viewpoint, so a component you are watching stays where you left it.

## Reading the drawing

**Symbols** are the usual P&I glyphs: a crossed rectangle for a heat exchanger, a circle with a
triangle for a pump pointing with the flow, a bowtie for a valve and a bowtie with a third leg for
a three-way valve, a small filled circle for a junction of three or more pipes. A node with only
two pipes is not drawn at all; the pipe runs through. A boundary, where fluid enters or leaves,
is a small hollow dot.

**Labels** are equipment tags where the component has one, `100PU01` rather than `PU1`, so the
drawing matches an equipment schedule. A component the compiler added for you, a junction it
inferred or the pipe behind a connection line's properties, is drawn fainter and its name is
italic.

**Colour** is the property the diagram follows -- the script's [`show`](../functions/show.md), or
`temperature` when it says nothing. The fill runs from blue at the cold end of the scale to orange
and red at the hot end, so the drawing reads as a temperature map: the supply side warm, the
return cool. See [the colour scale](#the-colour-scale) below.

**Arrows** on the pipes point the way the solver found the fluid to move, one per line between two
symbols. A pipe with no arrow carries no flow.

## The colour scale

Every symbol and every pipe is coloured by one property of the fluid, and the **legend** at the
bottom right says which and what the colours mean: the property and its unit at the top, the ramp,
and the values along it. A drawing is never coloured without the legend, because a colour without a
scale is decoration.

- **A pipe is a gradient** between the value where it leaves one component and the value where it
  enters the next. That is a straight two-point blend along the pipe, not a computed profile -- a
  long pipe that loses heat along its length still draws as a smooth run from its hotter end to
  its cooler one. A pipe you split with `nodes=` has a real value at every cell, so it draws its
  profile cell by cell.
- **A heat exchanger is a gradient across its body**, from its inlet's colour to its outlet's: the
  duty made visible. A pump on the pressure scale runs from suction to discharge the same way.
- **A pipe with a `style` colour keeps it.** The script's word about a pipe's colour is stronger
  than the scale's; the symbols still take the scale.
- **Grey means no value.** A component the solver gave no value for, or a property that does not
  apply to it, draws in the neutral symbol colour, never at the cold end of the scale.

**Switching the property** is the row of names under the ramp: the properties the script's `show`
listed, then `temperature`, `pressure` and `mass flow`, which every diagram offers. A click switches
at once, with no recompile, and changes nothing in the script; `Home` on the canvas puts the view
and the property back to what the script says. Every property has its own range, from the lowest
value in the plant to the highest, rounded outward to round numbers. `show temperature 0..80` fixes
the range instead.

**Hover the ramp** between two values and every symbol whose value lies in that band stands out
while the rest recede: "show me everything above 60 °C" is a movement of the cursor.

**Two things the legend says in words.** When every element has the same value it reads `all
20 °C` and everything takes the middle colour; when nothing has been solved it reads `No solved
temperature values` and everything is neutral. While a newer text is being compiled the colours
turn grey, because they describe the text before your last edit and would otherwise pose as
current.

**A red plant did not solve.** When the script is refused before the solver, or the solver does not
converge, the whole drawing turns red and loses its fills: the shape is still there to work on, and
the log says what stopped it. The colours return with the next script that solves.

**A drawing that breaks its own rules says so.** The layout is checked against its own standard
every time it is drawn -- no pipe through a symbol, every pipe leaving a port straight for a whole
margin, loops clockwise. Where a check fails, the log carries an `FS5002` warning naming the rule
and the two parts involved, and the picture is unreliable there. It is a report about the drawing,
not about your plant: the script is right and the layout rules have not caught up with it yet.

**Two small marks** in a symbol's corners tell you something the picture alone cannot:

- a coloured dot at the top-right corner means the compiler has a warning (amber) or an error
  (red) about that component; the message is in the log;
- a small hollow square at the bottom-left corner means at least one of its values was sized or
  defaulted rather than stated by you.

The second mark is the one worth a habit. A plant where every symbol carries the square is a
plant the tool designed for you; the numbers behind it are in the script's parameters and, soon,
in hover.

## Hover and select

**Hover a symbol** and, after a moment, a card shows what the compiler knows about it: every
parameter with its value and where the value came from, `stated` by you, `sized` by the tool or a
`default` from the registry, with the reasoning under a sized or defaulted one; then the solved
state, flow, temperatures, pressures; then any warning about it. Hover a pipe for its flow, and
for a pipe with a length and a size, its pressure drop. The card comes from the model already in
hand, so it never waits for the server. A component the compiler added says so on its card.

**Click a symbol** to select it: it takes the selection colour and the editor scrolls to its
declaration and marks the line. Shift+click adds to the selection, `Esc` clears it. It works the
other way too: put the caret on a declaration in the script and the component lights up on the
diagram. Clicking a component's name in the log does both at once.

## What shows at which zoom

Zoomed far out, only symbols and pipes are drawn, so a large plant is a shape rather than a fog
of text. From half size the tags appear; from one and a half times, the port positions are marked
as small dots; from three times, the names of the inferred nodes and pipes appear too.

## A kind the drawing does not know

If the compiler sends a component kind the canvas has no symbol for, which can happen when the
two are of different versions, it is drawn as a dashed rectangle with its label. The drawing never
fails because of one symbol.

## See also

[How the diagram is arranged](how-the-diagram-is-arranged.md) · [The editor](the-editor.md) · [The log](the-log.md) · [Style](../functions/style.md)
