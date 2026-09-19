# Exporting

The drawing beside your script can leave the app as a file: an SVG that scales without loss and
opens in a browser or in Inkscape, or a PNG that pastes into anything. Both are the diagram as it
stands on the canvas -- the same symbols, pipes, colours, arrows and legend -- and both carry, inside
the file, a record of exactly what produced them.

Press **Export** in the toolbar or `Ctrl+E`. The button is disabled until the script has compiled
to a model, because there is nothing to draw before that.

## What you choose

| Option | Meaning |
|---|---|
| **SVG** or **PNG** | Vector, for documents that scale and for editing further; raster, for a chat, a slide or a wiki that takes images only |
| **Theme** | `Light` is the default whatever the app is showing, so the file reads on paper and on a white page; `Dark` is there when you ask for it |
| **Legend** | The colour scale under the drawing, with the property, its unit and the values along the ramp. On by default: a coloured drawing without its scale is decoration |
| **Values** | The shown property written under each component -- `50.02 °C` under the valve, `20.01 °C` at the mixing node. On by default. On a plant that has not solved every one reads `not solved`; nothing is ever written as zero |
| **Axes** | The origin's X and Y rays, off by default |
| **Resolution** (PNG) | 96 dpi for a screen, 150 for a document, 300 for print |
| **Transparent background** (PNG) | No ground behind the drawing |

The property the drawing follows is the one the canvas shows: switch the legend to `pressure`
before exporting and the file is the pressure map, with the pressure legend.

## What the file contains

The SVG is **standalone**. It needs no network, no stylesheet and no font that might be missing:
every colour is written into the file, and the text names the same family as the app -- your
system's interface font, with a generic family at the end of the list so that a machine without it
still draws readable labels. It contains no script.

Every component is in the file twice over: its **equipment tag** is the drawn text, `100PU01`, the
name an equipment schedule uses, and its **id** is the element's `data-id`, `PU1`, the name that
survives when a pump is inserted above it and the tags renumber. A reader searches the file for
either.

The `<title>` names the circuit. The `<desc>` records what produced the drawing, one line each:

```
FluidScript diagram
document: plant_01
application: 0.4.0
model contract: 2.0
language major: 1
source hash: sha256:5574…
catalogue: steel_en10255 2026.1
property backend: sharp-prop 1.0.0.0
atmosphere: 101.325 kPa absolute
status: solved in 2 iterations
shown: temperature (°C), 0 to 60
tags: equipment tags are as of the source hash above; an insertion above a component renumbers it (D-34)
generated: 2026-09-19T08:12:04.000Z
```

The **source hash** is the line worth knowing about. Two exports of the same plant that differ are
either two designs or two builds, and the hash with the versions under it says which. The script's
text itself is never written into the file.

A screen reader announces the title and reads the description; the drawing is marked as an image
with both.

## The PNG

The PNG is the SVG rasterized in your browser, nothing else: what you get is what the SVG shows,
at the resolution you chose. A browser will not draw a bitmap wider or taller than 16 384 pixels or
larger than about 268 million pixels in all; a plant that would exceed that at 300 dpi is refused
with the exact size in the message, and the SVG remains available -- vector files have no such
limit.

## When something is wrong

- **A component kind the drawing does not know.** The canvas draws an unknown kind as a dashed
  box so you can keep working; the export refuses, naming the component, rather than write a file
  that looks like a plant but is not one.
- **The download did not start.** The file is still generated; the dialog stays open with
  **Download again**. Nothing about your document changes either way -- an export is never a save.
- **A colour the theme does not value.** The export writes what it can, leaves that one as
  written, and tells you so under the options.

## See also

[The canvas](the-canvas.md) · [Files and recovery](files-and-recovery.md) · [Themes](themes.md)
