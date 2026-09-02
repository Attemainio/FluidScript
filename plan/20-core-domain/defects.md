---
id: 20-core-domain-defects
title: What implementing against the core domain found
tier: 20-core-domain
owns: [defect and observation record for documents 21-27]
---

# What implementing against the core domain found

Defects, deferrals and observations from implementing against `21`–`27`. The rule and its reasoning
are in [`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

**Only [`22-component-model`](22-component-model.md) has been implemented against so far**, and only
its metadata half: P2.6 built the component registry the binder reads. No physics, no residuals, no
catalogue. `21`, `23`, `24`, `25`, `26` and `27` are unread at implementation depth, so their absence
from this file means nothing has looked, not that nothing is wrong.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| C-1 | [`22`](22-component-model.md) | `pipe.insulation` and `pump.curve` are documented but not registered | Both are placeholders in `22`'s tables with no dimension and no range — heat loss is post-v1, and named curves arrive with the catalogue in P3.5. Registering them would make `FS1503` accept a name nothing reads, so a user would get silence where they expect an effect. The registry-comparison test carries both as named exceptions with these reasons, so they cannot be forgotten or silently added. |
| C-2 | [`22`](22-component-model.md) | Every invariant except the parameter-set one is unasserted | Invariants 2–7 are about `EvaluateResiduals` — zero allocation, determinism, fixed length, scaled residuals, continuity — and no component exists to assert them on. **They must be written in P3.3, with the components**, not retrofitted across six kinds afterwards; `08` says the same. |
| C-3 | [`22`](22-component-model.md) | The tank's per-layer and per-port properties (`t1`…`tN`, `inN_t`, `outN_t`) are not in the registry | They cannot be a fixed dictionary: which exist depends on `layers` and on which ports a script names. The registry carries the *families*; materializing them is the binder's job. P2.8. |
| C-4 | [`22`](22-component-model.md) | `heat_exchanger` mode selection is unimplemented | Duty, Rated and Coupled are computed at lowering from connections and stated properties. The registry marks `in2`/`out2` optional, which is all the binder needs; the rest is P3.3/P4.1. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| C-5 | [`22`](22-component-model.md) | Convention 5 requires every parameter to declare a display precision, and **no table carries one** | `ParameterInfo.DisplayPrecision` is the authority, and `22` now says so. A column of precisions in the document would be a second place to keep in step, for a formatting decision with no bearing on the physics. |
| C-6 | [`22`](22-component-model.md) | The ranges are written in the "bare number means" unit, and nothing said so | Stated, with the failure it prevents: transcribing a temperature range of −50 … 300 as SI by hand gives −50 K, and every plausible temperature then falls outside its own range. The registry converts at build time instead. |

## Observations

**The parameter tables are now machine-read.** `22`'s own parameter-registry section asks for a test
comparing the registry against its tables, and there is one — it parses the tables out of the document
and compares in both directions. Two things it needs to keep working:

- A component section is bounded by the *next* second-level heading, not the next numbered one.
  Without that the tank's section swallows `## Parameter registry` and `## Error cases`, and every
  `FS21xx` code becomes a parameter of `tank`.
- It reads **only the table headed `| Parameter |` or `| Parameter pattern |`**. A component section
  holds other tables whose first cell is a backticked name — the exchanger's ε-NTU arrangement
  formulae are one — and reading those as parameters made `counter` a parameter of `heat_exchanger`.

Both are the kind of thing that will be re-derived painfully if this note is not here.

**The controller's parameters are not in `22` at all.** `kp`, `ki` and `kd` are specified in
[`34-controllers`](../30-solver/34-controllers.md), because `22` describes six *flow-component
families* and a controller carries no flow. The registry-comparison test therefore skips any kind
`22` does not document, which is correct but means the controller's parameter set has no
document-drift guard. It will need one when `34` is implemented against.

**A tag code is checked by lexing the tag it produces, not against the unit table.** `22` says no tag
may lex as a quantity literal; the check that matters is whether `400PU01` comes back as one
identifier, since it is the whole tag that must not read as a number and a unit.

**`node` and `pipe` carry no tag code deliberately**, and the registry test asserts exactly that pair.
A future kind added without a code should have to justify itself against this, not inherit silence.
