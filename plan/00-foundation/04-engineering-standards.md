---
id: 04-engineering-standards
title: Engineering standards
tier: 00-foundation
status: reviewed
owns: [coding standards routing, XML documentation policy, frontend lint policy, review cadence]
depends_on: [01-vision-and-scope, 03-repository-layout]
traces_to: [R-16, R-17, R-28]
open_questions: 0
last_review_pass: 2
---

# Engineering standards

## Purpose

Names the standards this project is written to, and where they live. FluidScript does not maintain its
own C# style guide — the `dotnet-toolkit` plugin already ships one, and a second copy would diverge
within a month. This document is a routing table plus the few rules that are genuinely
FluidScript-specific.

## Responsibilities

**Owns.** Which standard applies when, the XML-documentation policy, the frontend lint/format policy,
and the review cadence.

**Explicitly does not own.** The content of the C# standards (plugin-owned), the test *strategy*
([`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md)), CI enforcement
([`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md)).

## C# standards — plugin-owned, never copied

The canonical standards are the `dotnet-toolkit` plugin's `standards/` folder. Reach them by calling
`workspace_status`, taking its `pluginRoot:` line, and joining `<pluginRoot>/standards/<name>.md`.
Never write `${CLAUDE_PLUGIN_ROOT}` into a path — it is not expanded inside a rule or agent definition.

The routing table is `<pluginRoot>/standards/index.md`; do not restate it here. What matters for
FluidScript is which rows fire unusually often:

| Standard | Why it matters more here than in an average repo |
|---|---|
| `performance.md` | The solver's residual and Jacobian assembly run thousands of times per solve, and property lookups dominate. Anything inside a Newton iteration is a hot path by definition. |
| `api-design.md` | Every wire DTO in `FluidScript.Api/Contracts` triggers it, and the model contract is consumed by the canvas, the exporters, and `/docs` examples simultaneously. |
| `architecture.md` | `R-16` — Core must not acquire a UI or hosting dependency, and that is the direction things drift. |
| `error-handling.md` | A script is user input; the *normal* path includes malformed input. Diagnostics are a return value, not an exception. |
| `concurrency.md` | Transient runs stream while the user keeps editing. Cancellation correctness is not optional. |
| `testing.md` | `R-17` |

**Navigation rule.** For anything C#, use the `dotnet-toolkit` MCP tools rather than `Grep`, `Glob`,
`find`, or reading whole `.cs` files to locate something — grep is blind to interface and virtual
dispatch, counts comment and string matches as hits, and under-reports silently on truncation. `Read`
remains correct for a known file and region; `Grep`/`Glob` remain correct for non-C# files —
TypeScript, CSS, Markdown, `.fluid` scripts, config.

## XML documentation policy

`GenerateDocumentationFile` + `TreatWarningsAsErrors` makes CS1591 a build failure, so **every public
member carries an XML doc or the build breaks**. Beyond mere presence:

- `<summary>` says what the member is *for*, not what its name already says. `/// <summary>Gets the
  pressure drop.</summary>` on `PressureDrop` adds nothing and should be a `<value>` describing units,
  sign convention, and what zero means.
- **Every dimensioned member states its unit and sign convention in `<value>` or `<returns>`.** This
  is the FluidScript-specific rule. A `double Head` with no stated unit is a defect regardless of what
  the type system says, and `Quantity` types do not remove the need to say whether a pressure drop is
  positive in the flow direction.
- `<remarks>` carries the *why* — the assumption, the correlation used, the range of validity. A
  correlation with no stated validity range will be applied outside it.
- Public types that appear in a `/docs` page link to it: `<seealso href="../../docs/functions/pump.md"/>`.

## Frontend standards

No plugin covers TypeScript here, so these are stated rather than routed:

- **TypeScript strict mode on.** `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`.
- **ESLint + Prettier**, with formatting delegated entirely to Prettier so it is never a review topic.
- **No `any`** on any exported surface. Wire types are generated from the API contract, not hand-written.
- **Components are function components with typed props.** No class components, no `React.FC`.
- **Feature-folder layout** (`features/editor`, `features/canvas`, …), not type-folder. A feature owns
  its components, hooks, state, and styles together.
- **No physics in the frontend.** If a number can be computed, Core computes it. The frontend converts
  units for display and computes geometry — nothing else. This is `D-03` restated as a lint-able rule:
  a `Math.` call outside `features/canvas/layout` deserves a review question.

## Review cadence

- **After any C# change**: validate against the in-memory compilation before it touches disk
  (`validate_patch`), rather than building afterwards — it localises the failure to the edit that
  caused it. Read its sufficiency field rather than assuming success; fall back to `dotnet build` when
  it reports the validation was insufficient.
- **`validate_patch` is not a test run.** After a Core change, run `dotnet test`.
- **Before a commit**: full `dotnet build` plus `dotnet test`. `validate_patch` sees the C#
  compilation, not the frontend build or analyzer passes across untouched projects.
- **Broad reviews** go through the `dotnet-toolkit:dotnet-review` skill, partitioned by folder into
  disjoint scopes reviewed in parallel — not inline, because the review agents read the standards
  files directly and a session that reviews inline has not loaded them.

## Invariants

1. No copy of any `dotnet-toolkit` standard exists in this repository.
2. Every public C# member has an XML doc comment (enforced by the build).
3. Every public member representing a dimensioned value states its unit (enforced by review).
4. `frontend/` has no dependency that performs thermodynamic or hydraulic computation.

## Worked example

A correctly documented Core member, showing the FluidScript-specific requirements:

```csharp
/// <summary>
/// Pressure drop across the component at the current solved operating point.
/// </summary>
/// <value>
/// Pressure in Pa, positive when pressure falls in the nominal flow direction. Negative for a
/// component adding energy — a pump reports its rise as a negative drop, so that summing drops
/// around a closed loop yields zero.
/// </value>
/// <remarks>
/// Zero until the circuit has been solved; check <see cref="Circuit.IsSolved"/> before reading.
/// </remarks>
public Pressure PressureDrop { get; }
```

The sign convention in `<value>` is the part that stops a real bug. Without it, half the components
will be written with the opposite sign and the loop closure equation will look correct in isolation.

## Acceptance criteria

- [ ] `dotnet build` produces zero warnings across the solution.
- [ ] A public member added without an XML doc fails the build (verify once, deliberately).
- [ ] `npm run lint` in `frontend/` passes with zero warnings.
- [ ] No file under `standards/` exists in this repository.

## Open questions

None. A test in `FluidScript.Core.Tests` inspects project/assembly references and fails `dotnet test`
when Core reaches UI, ASP.NET, or transport packages. Under `D-46` Core's model and realtime types are
hand-written and authoritative, the JSON Schemas are emitted from them and drift-checked in CI, and the
TypeScript DTOs and the Api `Contracts/` mirror are generated from the committed schema; OpenAPI
documents REST only and cannot become a competing source (`D-30`).
