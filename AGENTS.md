# AGENTS.md

**Read [`CLAUDE.md`](CLAUDE.md) first, in full. It is the operating contract for this repository and
it applies to every agent, not only to Claude Code.**

This file exists because agents differ in which file they load by convention. `CLAUDE.md` holds the
rules; this one holds nothing but the pointer to them, so that the two can never drift apart. If you
are reading this and have not read `CLAUDE.md`, stop and read it.

## What is in there

- **The reference index** — which `plan/` document owns which subject, so you read one file instead of
  the tree.
- **Working a plan** — what to read before starting, when a defect gets an entry, and what to write
  when a work package closes. This is the part most often skipped and most expensive to skip.
- **Non-negotiable rules** — the docs gate, the SI boundary, the single SharpProp reference, the
  decision-log rule, and the rest.
- **Non-obvious invariants** — the mistakes a session makes on its first day, each with the
  consequence that follows.
- **Interaction and response style** — including *explain a defect from the beginning, not from the
  conclusion*, which is the house style for every `defects.md` entry.

## The two files that say where the project is

Read them in this order, before anything else:

1. **[`plan/08-implementation-sequence.md`](plan/08-implementation-sequence.md)** — the plan. Every
   phase, every work package, and the reason each sits where it does.
2. **[`plan/09-project-state.md`](plan/09-project-state.md)** — the state. What has shipped, which
   phase is current, what is next, and where the open questions live.

`08` is written in the future tense and does not change as work lands. `09` is the memory, and
updating it is part of finishing a package rather than a courtesy to the next session.

## If you write C#

`.cs` files in this repository are edited with `Edit` and `Write`, anchored on text you have read,
never through a shell redirect or `sed`; a `PreToolUse` hook blocks Bash writes to `.cs`. The
`dotnet-toolkit` MCP server (2.0) is the navigation side of every C# change -- `search_index`,
`get_symbol`, `get_references`, the hierarchies, `rename_symbol` for a rename -- and a `PostToolUse`
hook keeps its index in step with each edit. `dotnet build` is the compile gate, run after a burst of
related edits. The protocol is the plugin's `dotnet-read` / `dotnet-write` skills, and `CLAUDE.md`
says why grep is the wrong tool for C#.

An agent without that server available should say so and stop, rather than navigate C# by grep.
