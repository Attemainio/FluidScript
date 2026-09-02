# fluidscript

The version line every script opens with.

```fluidscript
fluidscript 1
```

It states which major version of the language the file is written in, and it is the first line of a
saved file. A file without it cannot be saved durably: the version is what lets a script written today
still mean the same thing when the language has moved on.

## Rules

- One number, the language major. There is no minor.
- It comes before everything except comments and blank lines.
- A file is read under the major it declares. Current behaviour is never applied to an older file
  behind your back.

## See also

[`project`](project.md) · [`circuit`](circuit.md)
