# Equipment tags

Every component of a kind that carries a code gets an equipment tag: the circuit number, the kind's
letters, and an ordinal. A pump in circuit 400 is `400PU01`, and the second one is `400PU02`.

Tags are **derived, not stored**. Nothing writes them into your script unless you ask, because a tag
that changed every time you inserted a pump would renumber the identifiers under your cursor as you
typed. They are unique across the whole file by construction: the circuit number is part of the tag,
so `101PU01` and `201PU01` cannot collide.

`node` and `pipe` carry no code deliberately. Both are mostly inferred, both outnumber every other
kind in a real circuit, and no plant schedule tags them — a diagram labelling forty nodes would bury
the six pieces of equipment a reader is looking for.

<!-- BEGIN GENERATED: tag-codes -->
| Kind | Code | Tag in circuit 400 |
|---|---|---|
| `node` | *none* | untagged |
| `supply` | *none* | untagged |
| `return` | *none* | untagged |
| `pipe` | *none* | untagged |
| `heat_exchanger` | `HE` | `400HE01` |
| `valve` | `V` | `400V01` |
| `three_way_valve` | `TV` | `400TV01` |
| `pump` | `PU` | `400PU01` |
| `tank` | `S` | `400S01` |
| `controller` | `PID` | `400PID01` |
| `t_sensor` | `TE` | `400TE01` |
| `p_sensor` | `PE` | `400PE01` |
| `flow_sensor` | `FE` | `400FE01` |
<!-- END GENERATED: tag-codes -->

A code is a house convention rather than a published standard. It is registry data, so a site that
writes `LP` for a pump changes a row rather than patching the tagger.

## See also

[`circuit`](circuit.md) · [Properties](properties.md)
