# Using the API

Everything the editor does, it does through five HTTP calls, and you can make them yourself: from a
script, a notebook, a build step, or an agent that writes FluidScript and reads back what it got wrong.
This page is the whole of that surface. There is no authentication and nothing is stored: every call
is a function of the body you send.

Start the host and it answers on `http://localhost:5080`:

```
dotnet run --project src/FluidScript.Api
```

## Compile a script

`POST /api/v1/compile` takes the script and gives back the model: every component, its parameters,
what the solver found, the diagram's placements and routes, and the diagnostics. This is the call the
editor makes as you type.

```json
{ "sessionId": "my-notebook", "script": "fluidscript 1\ncircuit loop\n...", "solve": true }
```

- `sessionId` is any string you choose. Send the same one on every call from the same place: the host
  keeps the last solution under it and starts the next solve from there, which is what makes a second
  compile of a lightly edited script take a few milliseconds instead of a few hundred. Losing the id
  costs one cold solve and nothing else.
- `script` is the whole file, as text.
- `solve` may be `false` to stop after the topology is built, for a first drawing of a script you are
  still writing.

The answer is always `200`, even when the script is wrong -- the request succeeded, it was asked to
compile a script and it did. What it found is inside:

```json
{
  "model": {
    "contractVersion": "2.0",
    "circuits": [ { "name": "loop", "solved": true, "...": "..." } ],
    "components": [ "..." ],
    "solve": { "converged": true, "iterations": 3, "...": "..." },
    "diagnostics": [ { "code": "FS1507", "severity": "warning", "message": "'PU1' is not connected to anything.", "range": { "start": { "line": 5, "character": 0 }, "end": { "line": 5, "character": 3 }, "offset": 96, "length": 3 }, "component": "PU1" } ]
  },
  "timings": { "parseMs": 1, "bindMs": 2, "sizeMs": 5, "solveMs": 14, "totalMs": 31 }
}
```

`model` is described field by field on the [model contract](../functions/model-contract.md) page.
`timings` says where the time went, so "it feels slow" has a number beside it.

**A script with an error is not solved.** Its `circuits[].solved` is `false`, `model.solve` is absent,
and the diagnostics say why; the components and the drawing are still there, which is usually the
fastest way to see what is wrong.

**A script this build cannot read** -- a `fluidscript 2` line, say -- comes back with `model: null` and
the diagnostics beside it in `diagnostics`. That is the one shape where the envelope carries them.

## Ask for an answer

`POST /api/v1/solve` takes the same body and returns the same shape, but it is stricter: a component
that is connected to nothing (`FS1507`) or a part of the circuit cut off from the rest (`FS1511`) is an
error here rather than a warning, and the solver does not run. While you type, a half-written script
should still draw; when you press Solve you are asking for a number, and a number computed round a
pump that is not in the loop would be a wrong one.

## Check without solving

`POST /api/v1/validate` takes `{ "script": "..." }` and answers with the diagnostics alone, in
milliseconds, with no model and no physics:

```json
{ "contractVersion": "2.0", "languageMajor": 1, "diagnostics": [ "..." ], "timings": { "parseMs": 1, "bindMs": 2, "sizeMs": 0, "solveMs": 0, "totalMs": 4 } }
```

Use it for fast feedback on a large script, or in a loop that fixes what it wrote until the list is
empty.

## Read a diagnostic

Every diagnostic, wherever it arrives, has the same shape:

| Field | What to do with it |
|---|---|
| `code` | Look it up on the [diagnostics](../functions/diagnostics.md) page. |
| `severity` | `error`, `warning` or `info`. Errors stop the solve; infos are things the language did for you, such as a node it added. |
| `message` | The explanation, with the names and numbers filled in. |
| `range` | Where in the script. `start` and `end` are line and character, both counted from zero, as an editor counts them; `offset` and `length` are positions in the text, for applying an edit. They describe the same span. `null` when the diagnostic is about no particular text. |
| `component` | Which component it concerns, or `null`. |
| `suggestion` | A fix, when there is one known to be right: a `range` and the `newText` to put there. Apply it and recompile. |
| `related` | Other places worth looking at, each with a message and a range. |

The list is ordered by severity, then by position, so the first entry is the one to read first.

## Read the language

`GET /api/v1/metadata` describes everything the language has: every component kind with its
parameters, aliases, units and ranges; every port and indexed port family; every unit for every
dimension; every diagnostic code with its message; the symbols the diagram is drawn with; the
catalogue and property-package versions; and the limits below. It changes only when the software
does, and it says so with an `ETag`: send it back as `If-None-Match` and an unchanged document is a
`304` with no body.

This is the page to read first if you are writing FluidScript from a program. A parameter you do not
find here does not exist.

## Limits

| What | Limit | What happens over it |
|---|---|---|
| Script size | 1 MiB | `413`, with the size and the limit in the body |
| Declarations | 10 000 | `FS4601`, an error; nothing is solved |
| Tokens | 100 000 | `FS4601` |
| Solver unknowns | 800 | `FS4601`, found after the topology is built and before the solve |

The numbers are the host's and `metadata.limits` reports the ones in force.

## When something is not a 200

| Status | Meaning |
|---|---|
| `400` | The request itself could not be read: not JSON, or `script` or `sessionId` missing. The body says which. |
| `404` | No such path. |
| `413` | The script is over the size limit. |
| `500` | A fault inside FluidScript, never a problem with the script. The body carries `FS9001` and a `correlationId` to quote when reporting it. |

A request that a newer request from the same `sessionId` overtook is abandoned with `499`; if you send
one compile per keystroke you will see these, and they cost nothing. Every error body is
[problem details](https://www.rfc-editor.org/rfc/rfc9457): a `title`, a `status`, and a `detail`.

## Health

`GET /api/health` answers `{ "status": "ok", "core": "1.0.0.0" }` when the host is up.

## See also

[Model contract](../functions/model-contract.md) · [Diagnostics](../functions/diagnostics.md) ·
[Reading the solve report](reading-the-solve-report.md)
