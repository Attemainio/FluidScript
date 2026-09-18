# Themes

FluidScript ships a light theme and a dark theme, follows your system's choice until you make one,
and reads a theme file of your own. The script pane uses the colours of Visual Studio Code, so a
script looks like code in the editor you already know; everything else -- the chrome, the diagram,
the log -- uses a muted palette where a colour says something about the fluid.

## Choosing

The theme control is on the toolbar. **System** follows the operating system's light or dark
setting and changes with it; **Light** and **Dark** fix one. The choice is kept in the browser, so
it is there after a reload, and switching loses nothing: the script, the diagram and a running
simulation stay as they were.

## What the colours mean

On the diagram, colour is temperature. The circuit's coldest point is blue, its hottest is red, and
everything between is interpolated, so the supply and return of a heating loop read as such without
a legend. The seven stops the ramp runs through are:

| Stop | Light | Dark | Where you see it |
|---|---|---|---|
| cold | `#1B6CA8` | `#4FA3D9` | Chilled water, a cooling supply |
| cool | `#3A8FB7` | `#6FBBD9` | |
| neutral | `#5C7A89` | `#8FA9B5` | Ambient, unheated |
| warm | `#C97B3C` | `#E09E5F` | |
| hot | `#B23A2E` | `#E06C5A` | Heating water, a heating supply |
| air | `#7A9E7E` | `#9CC2A0` | Air-side circuits |
| steam | `#8E7CC3` | `#B0A0DC` | Steam |

Status has its own five colours -- converged, information, warning, error, and stale for a value
that is being recomputed -- and they are the saturated ones. The fluid colours are deliberately
quiet so that a diagram with twenty components does not vibrate; saturation is kept for what
needs attention.

In the script pane, keywords are blue, component kinds teal, names light blue, numbers green,
comments green italic: the Light+ and Dark+ colours of VS Code, value for value. Two things are
FluidScript's own. A parameter name is the name colour dimmed a little, so that the left column of a
declaration reads as the list of things declared. And a unit is its number's colour, dimmed in the
dark theme, so `30 kW` reads as one value with the unit receding.

## Writing your own

A theme is a JSON file: a name and a value for each colour token. Load it with **Load theme…** on the
toolbar. This is the whole format:

```json
{
  "name": "sepia",
  "colors": {
    "--surface-base": "#F6F1E7",
    "--surface-raised": "#FFFDF8",
    "--text-primary": "#3B3128",
    "--fluid-hot": "#A8452A"
  }
}
```

Every value is a hex colour: `#RGB`, `#RRGGBB`, or with an alpha, `#RRGGBBAA`. The token names are
the ones the built-in themes use; the file for the light theme is the complete list, and a copy of
it is the easiest place to start.

You do not have to give every token. **A token you leave out takes the built-in value** -- the dark
theme's if Dark was selected when you loaded the file, the light theme's otherwise -- and a note on
the toolbar names the ones filled in. A value that is not a colour is treated as left out, and named
too.

**Contrast is checked, not refused.** Every text-on-surface pair in the built-in themes meets
WCAG AA, and your theme is measured against the same pairs. One that falls short is loaded all the
same, with a note saying which pair and by how much; it is your tool, and the note is there so a
theme that is hard to read is not a mystery.

**A file that is not valid JSON is not loaded.** The current theme stays and the parse error is
shown, so a missing comma costs nothing but the message.

## Motion

Values cross-fade when they change and a theme switch fades over a fifth of a second. The diagram's
geometry never animates: a component that slid to its new place after every edit would be impossible
to read while typing, so it cuts. If your system asks for reduced motion, every transition is off
and nothing is lost, because nothing is said by motion alone.

## See also

[How the diagram is arranged](how-the-diagram-is-arranged.md) · [Using the API](using-the-api.md)
