---
id: 63-ci-and-repo-hygiene
title: CI and repository hygiene
tier: 60-docs-and-devex
status: reviewed
owns: [CI pipeline, branch policy, public-repo requirements, README, licence, contribution flow, release]
depends_on: [03-repository-layout, 61-documentation-plan, 62-testing-strategy]
traces_to: [R-28, R-30, R-32, R-46, R-49]
open_questions: 0
last_review_pass: 2
---

# CI and repository hygiene

## Purpose

`R-30`: a public repository with a README that gets a newcomer to a rendered diagram. Public means the
repository is a shop window as much as a codebase — the first thing a visitor sees decides whether they
try it. It also means the gates that make `R-28` real have to be automated, because a public project
accumulates contributors who have not read this plan.

## Responsibilities

**Owns.** The CI pipeline, branch policy, public-repository requirements, the README's structure, the
licence, the contribution flow, and releases.

**Explicitly does not own.** Test content ([`62-testing-strategy`](62-testing-strategy.md)),
documentation content ([`61-documentation-plan`](61-documentation-plan.md)), repository layout
([`03-repository-layout`](../00-foundation/03-repository-layout.md)).

## CI pipeline

GitHub Actions. One workflow on push and pull request.

```
┌─ build ────────────────────────────────────────┐
│  dotnet build   (TreatWarningsAsErrors)        │
│  npm ci && npm run build                       │
└────────────────┬───────────────────────────────┘
                 │
   ┌─────────────┼─────────────┬─────────────────┐
   ▼             ▼             ▼                 ▼
┌────────┐  ┌─────────┐  ┌───────────┐  ┌──────────────┐
│ test   │  │ lint    │  │ docs gate │  │ arch tests   │
│ dotnet │  │ eslint  │  │ (below)   │  │ (below)      │
│ vitest │  │ tsc     │  │           │  │              │
└────┬───┘  └────┬────┘  └─────┬─────┘  └──────┬───────┘
     └───────────┴─────────────┴───────────────┘
                 ▼
            ┌─────────┐
            │  e2e    │   Playwright, on PRs to main only
            └─────────┘
```

Parallel after build, so a failure surfaces in the shortest possible time. **Target: under five
minutes** for everything but e2e — a pipeline slower than that stops being read.
All script discovery and fixtures use the `.fluid` extension selected by `D-10`; CI never treats them
as F# `.fs` inputs.

### The docs gate

[`61-documentation-plan`](61-documentation-plan.md)'s enforcement, and the mechanism that makes `R-28`
more than an intention:

1. Every registered component kind, **every reserved word that introduces a statement**, and every
   diagnostic code has a page or generated entry. Enumerating the reserved-word list matters as much
   as enumerating the component registry: `D-33`, `D-37` and `D-40` added five statements that are not
   component kinds, and a gate that walked only the registry would have passed all five undocumented.
   A retired diagnostic code is exempt and must be, or the gate demands a page for `FS1509`.
2. Every `fluidscript` block in `/docs` compiles, or produces its annotated diagnostic.
3. Generated pages match what the code would generate.
4. Every function page has every template section.
5. No forward reference in the tutorial.

**A missing page fails the build.** Not a warning, not a label — the same status as a failing test.
Anything softer produces documentation for the first three features.

### Architecture tests

Run as ordinary xUnit tests so they fail with a readable message rather than as a shell script:

| Assertion | Guards |
|---|---|
| Core references no ASP.NET, UI, or serialization package | `R-16`, [`04`](../00-foundation/04-engineering-standards.md)'s architecture test |
| Exactly one type references SharpProp | [`21`](../20-core-domain/21-fluid-and-state.md)'s invariant 1 |
| Tier-10 namespaces reference no tier-20 type | [`15`](../10-language/15-semantic-model.md)'s invariant 7 |
| `CircuitGraph` references no syntax type | [`23`](../20-core-domain/23-topology-and-graph.md)'s invariant 7 |
| No `Contracts/` type exposes a Core type | [`41`](../40-api/41-api-architecture.md)'s invariant 1 |
| No tolerance literal in the solver namespace | [`36`](../30-solver/36-numerics-and-convergence.md)'s invariant 1 |
| No literal colour outside the theme files | [`55`](../50-frontend/55-design-system.md)'s invariant 1 |

Each one encodes an invariant that review alone would eventually miss. They are cheap and they are the
difference between an architecture that is documented and one that holds.

## Branch policy

- `main` is always releasable and always green.
- Work on branches; merge by pull request.
- Required checks: build, test, lint, docs gate, architecture tests.
- Squash merge, so `main`'s history is one commit per change.
- No force push to `main`.

## README

The single highest-leverage file in a public repository (`R-30`). Structure:

```markdown
# FluidScript

One sentence. Then a screenshot: the script on the left, the diagram on the right.

## What it does
Three bullets. Not a feature list — the three things that make it different.

## Try it
    git clone …
    dotnet run --project src/FluidScript.Api
    cd frontend && npm install && npm run dev
Open http://localhost:5173 and paste this:
    <a ten-line script>
You should see: <screenshot>

## How it works
A paragraph and a diagram: script → parse → solve → render.

## Documentation
Tutorial · Advanced · Reference — with links.

## Status
Which milestone, what works, what does not. Honest.

## Contributing / Licence
```

**The screenshot goes above the fold**, because a visual tool that shows no picture in its README
looks abandoned. **"Try it" is third**, because the second question after "what is it" is always "can I
run it".

**The Status section is not optional and must be honest.** A public project at M2 that reads as
finished disappoints everyone who clones it; one that says "M2: steady-state solving works, the canvas
is in progress" gets contributors instead.

## Licence

**MIT.** Permissive, universally understood, and it imposes nothing on a user's designs.

**One licence question deserves care**, and it is the catalogue. The dimension data
([`27-component-catalog`](../20-core-domain/27-component-catalog.md)) is factual and gathered from
public sources, so it carries no third-party licence — but `SOURCES.md` must make the sourcing
auditable, and `LICENSE` should note that the catalogue data is factual and separately attributed.
Being able to answer a question from a standards body in one link is worth the paragraph.

SharpProp and CoolProp have their own licences (CoolProp is MIT), listed in a `THIRD-PARTY-NOTICES`
file generated at build time.

## Contribution flow

`CONTRIBUTING.md` covers: how to build and run, the tiered test story
([`62`](62-testing-strategy.md)), what the docs gate expects, and where design decisions are recorded.

**The decision-log rule is the one worth stating loudly:** a change that alters a documented decision
adds a `D-` entry ([`06-decision-log`](../00-foundation/06-decision-log.md)) rather than editing the
old one. It is the habit that keeps the rationale intact as contributors arrive.

**A new component's PR checklist** is [`03-repository-layout`](../00-foundation/03-repository-layout.md)'s
nine-file list, restated in the PR template so it is in front of the contributor rather than in a
document they have not read.

## Public planning and security files

Because `plan/` remains public under `D-30`, `plan/README.md` explains that the tree is a planning
contract, its status vocabulary, dependency direction, and review process. `plan/70-future/README.md`
explains that roadmap entries are evidence gates rather than promises, identifies `72-roadmap` as the
scope authority, and points promoted work back to its `D-` entry. CI asserts both files exist and
contain those links; a bare public directory of speculative documents is not acceptable.

The repository also ships `SECURITY.md` with the supported-release policy, a private vulnerability
reporting channel (GitHub Security Advisories), expected acknowledgement window, and a request not to
publish exploitable script/API payloads before coordination. The README and issue templates route
security reports there; ordinary public issues are not the disclosure channel.

## Releases

Tagged, semver, with a changelog generated from PR titles. Two artefacts:

| Artefact | Contents |
|---|---|
| Source | The repository at the tag |
| Self-contained app | The API with `wwwroot` built in — one download, `dotnet FluidScript.Api.dll`, open a browser |

**The self-contained artefact matters for adoption.** "Clone, install the .NET SDK, install Node, run
two commands" filters out most of the people who would otherwise try it.

**A release records its catalogue version** ([`27`](../20-core-domain/27-component-catalog.md)), because
a design's sizing is reproducible only against a known catalogue.

## Invariants

1. `main` always builds with zero warnings and passes every check.
2. The docs gate blocks a merge; it never warns.
3. Architecture tests run as part of `dotnet test`.
4. No secret, credential, or key is ever committed — scanned in CI.
5. The README's "Try it" commands work on a clean clone, verified by CI running them.
6. Every release records its catalogue version and its contract version.
7. `THIRD-PARTY-NOTICES` is generated, never hand-maintained.

Invariant 5 is unusual and worth the effort: a README whose instructions have quietly broken is the
most common defect in a public repository, and it is entirely preventable.

## Error cases

| Situation | Handling |
|---|---|
| Build warning | Fails (`TreatWarningsAsErrors`) |
| Missing docs page | Fails the docs gate, naming the feature |
| Golden-file diff | Fails; the PR must explain it |
| Architecture test fails | Fails; the invariant it names is in a plan document |
| Flaky test | Deleted or fixed the same session — never retried into passing |
| CI over five minutes, excluding e2e | Fails the quality budget and is treated as a defect; parallelise or move non-gating work to nightly |

## Worked example

A contributor adds a `filter` component. The nine-file checklist, and what CI says at each stage:

| Step | File | CI without it |
|---|---|---|
| 1 | `Components/Filter.cs` | — |
| 2 | Registry entry | — |
| 3 | `Sizing/FilterSizer.cs` | — |
| 4 | `Components/FilterTests.cs` | Passes, but coverage of the new equation is nil |
| 5 | `Sizing/FilterSizerTests.cs` | Same |
| 6 | `samples/filter-basic.fluid` | — |
| 7 | `src/FluidScript.Core/Model/Symbols/FilterSymbol.cs` | Metadata/symbol registry test fails |
| 8 | `frontend/src/features/canvas/symbols/symbolRenderer.test.tsx` | Generic renderer coverage is missing |
| 9 | `docs/functions/filter.md` | **Docs gate fails: "Component kind 'filter' has no page at docs/functions/filter.md"** |

Steps 7–9 are all automated; step 9 is the documentation failure most likely to be skipped and the one
`R-28` says is non-negotiable. Steps 4 and 5 rely on review, which is why the PR template restates them.

The generated parameter table in the new page also has to match the registry, so a page written before
the parameters settled fails until regenerated — which is annoying exactly once, and then the
contributor regenerates as a habit.

## Acceptance criteria

- [ ] CI runs build, test, lint, docs gate, and architecture tests on every PR.
- [ ] Everything but e2e completes in under five minutes.
- [ ] A PR adding a component without its docs page fails, with a message naming the file.
- [ ] Architecture tests fail when Core gains a UI reference.
- [ ] The README's "Try it" commands are executed by CI on a clean checkout.
- [ ] A release produces a self-contained artefact that runs with one command.
- [ ] `THIRD-PARTY-NOTICES` regenerates identically.
- [ ] Secret scanning runs and has never had a finding.
- [ ] `plan/README.md` and `plan/70-future/README.md` exist with the explanatory content and authority
      links required above.
- [ ] `SECURITY.md` routes private vulnerability reports through GitHub Security Advisories and the
      README/issue templates link to it.

## Open questions

None. v1 promises screenshots/video, not a hosted service. After M0 proves native packaging, the full
property-accuracy tier runs on Linux for every PR and supported-platform native smoke tests gate a
release; inability to do so blocks M1/M2 rather than silently weakening validation. Application
packages use 0.x from M2, with a `## Breaking` changelog section, and become 1.0 only at M5 exit.
Language/model/frame contract majors remain independent (`D-27`, `D-30`).
