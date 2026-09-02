# circuit

Opens a circuit, and everything that follows belongs to it until the next `circuit` line.

```fluidscript
fluidscript 1
circuit coolingLoop
circuit primary 200
```

## Rules

- `circuit <name>` is enough. A number is optional.
- Numbers you do not write are resolved in declaration order: 100, 200, 300. **A number you do write
  is kept verbatim**, and a number you did not write is never written into your file.
- The number is what makes equipment tags unique across circuits: `101PU01` and `201PU01` are pumps in
  different circuits, and neither collides with the other.
- A `circuit` line ends whatever section the previous circuit was in and opens the new one's
  declaration section.

## See also

[`project`](project.md) · [`connections`](connections.md) · [`supply` and `return`](supply-return.md)
