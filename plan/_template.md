---
id: NN-document-slug
title: Human readable title
tier: NN-tier-folder
status: draft            # draft | reviewed | stable
owns: []                 # concepts/types this document is the single authority for
depends_on: []           # ids of documents this one builds on. Must be equal or LOWER tier.
traces_to: []            # requirement ids from 01-vision-and-scope (R-xx)
open_questions: 0        # must equal the number of entries under "Open questions"
last_review_pass: 0
---

# Title

## Purpose

One paragraph: what this document decides, and what breaks if it is wrong.

## Responsibilities

**Owns.** The concepts, types and rules this document is the single authority for. Anything listed
here must appear in the `owns:` frontmatter and must not appear in another document's `owns:`.

**Explicitly does not own.** The adjacent concerns a reader might expect here, and the document id
that owns each. This section is what keeps the tree free of contradictions — an omission here is how
two documents end up specifying the same thing differently.

## Contracts

Public types, signatures, and data shapes. C# is written as a signature only, never a method body:

```csharp
public interface IExample
{
    /// <summary>What it does.</summary>
    /// <returns>What comes back, and what <see langword="null"/> means.</returns>
    Result<Thing> Resolve(Input input, CancellationToken cancellationToken);
}
```

Wire shapes are written as JSON with every field annotated (type, unit, nullability).

## Invariants

Numbered statements that are always true of a correct implementation. Each must be falsifiable —
"the graph is well formed" is not an invariant; "every `Pipe` has exactly two endpoint `Node`s, and
neither is the other" is.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|

Every failure mode a caller can hit, with the diagnostic it produces. "Should not happen" cases are
listed with what the implementation does anyway.

## Worked example

Real input, real numbers, real output. Show the intermediate values, not just the endpoints. This
section is the one that catches specifications that read plausibly but do not compute.

## Acceptance criteria

- [ ] Checkable statements. A person or a test can say yes/no to each without interpretation.

## Open questions

1. **Question.** What is unknown, what it blocks, and how it will be resolved (spike, user decision,
   measurement). The count here must match `open_questions:` in the frontmatter.
