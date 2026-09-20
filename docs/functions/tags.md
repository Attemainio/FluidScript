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
| `inlet` | *none* | untagged |
| `outlet` | *none* | untagged |
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

## A component between two circuits

A heat exchanger wired into two circuits gets one tag, from the circuit on the side that **loses**
heat. A district-heating substation's exchanger, taking 150 kW out of district circuit 400 and
putting it into heating circuit 100, is `400HE01` — and it is `400HE01` whether its line sits in the
`circuit district` block or the `circuit heating` block. The block you declare it in is a matter of
where the cursor was; the tag follows the plant.

Which side loses heat is read from what you wrote:

| You wrote | The losing side is |
|---|---|
| `power=150` (or `heater`, `boiler`) | side 2 — heat enters side 1 |
| `power=-150` (or `load`, `cooler`, `radiator`, `chiller`) | side 1 |
| No `power`, but `in.t=40 out.t=60` or `in[2].t=85 out[2].t=45` | whichever side's temperature drops |

If both sides are in the same circuit, that circuit owns it. If one side is a stated profile rather
than a circuit (`in[2].t=85 out[2].t=45` with nothing connected), the circuit you declared it in keeps it. If
it spans two circuits and nothing says which way heat goes, the lower circuit number takes it and
[`FS2216`](diagnostics.md) says so — the choice affects the tag and the diagram's grouping, never
the solve.

## See also

[`circuit`](circuit.md) · [Properties](properties.md)
