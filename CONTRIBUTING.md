# Contributing

## Building

```bash
dotnet build                              # zero warnings, or it fails
dotnet test                               # everything
dotnet test --filter-trait Category=Unit  # the fast tier -- run it constantly, under 2 s
cd frontend && npm install && npm run dev
```

`.NET 10 SDK` and `Node 22` or newer. `global.json` pins the SDK band and selects the test runner;
`dotnet test` from the root needs no arguments.

## The test tiers

`plan/60-docs-and-devex/62-testing-strategy.md` owns this in full. In short: `Category=Unit` runs in
under two seconds against a fake property backend and is meant to be run after every edit;
`Property`, `Validation`, `Golden` and `Api` run before a commit.

**A golden-file diff in a pull request is a behaviour change and must be explained in the
description.** An unexplained one is a silent contract change.

## The documentation gate

Every user-visible feature ships with its `/docs` page in the same pull request. A missing page fails
the build with the same status as a failing test. This is not negotiable and not deferrable: a
separate documentation milestone never happens.

## Adding a component

Nine files, listed in `plan/00-foundation/03-repository-layout.md`'s worked example. That list is the
definition of done, and the pull-request template restates it.

## Decisions

**A change that alters a documented decision adds a new `D-` entry to
`plan/00-foundation/06-decision-log.md`; it never edits the old one.** Entries are never renumbered
and never deleted — a superseded entry keeps its body and gains a status line. This is the habit that
keeps the reasoning intact as contributors arrive, and it is the one convention worth learning first.

## Branches

`main` is always releasable and always green. Work on a branch, merge by pull request, squash merge.
Required checks: build, test, lint, docs gate, architecture tests.
