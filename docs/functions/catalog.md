# catalog

Which catalogue sizing chooses from.

```fluidscript
fluidscript 1
catalog steel_en10255@2026.1
```

## Rules

- One catalogue per script, named before the components that draw on it.
- The `@` and the version pin it, so a script sizes to the same pipe next year as it did today. A
  catalogue that changes underneath a saved script would change its answers silently.
- With no `catalog` line the shipped default applies, and the log says which.
- The catalogue holds dimensions — bores, roughnesses, standard sizes — with a public source for every
  row. It is not a copy of a standard.

## See also

[`pipe`](pipe.md) · [Catalogues](catalogs.md)
