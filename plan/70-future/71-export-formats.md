---
id: 71-export-formats
title: Future interchange formats
tier: 70-future
status: reviewed
owns: [future DXF export, future versioned model interchange, format admission criteria]
depends_on: [26-model-contract, 59-static-export]
traces_to: [R-31]
open_questions: 0
last_review_pass: 2
---

# Future interchange formats

## Purpose

Defines the evidence gate for export formats after v1. M3 SVG and PNG are already owned by
[`59-static-export`](../50-frontend/59-static-export.md); this document prevents “export” from becoming
an unbounded promise to reproduce file formats for which FluidScript has neither geometry nor a real
consumer (`D-12`, `D-29`).

## Responsibilities

**Owns.** Admission criteria and contracts for future DXF and versioned model interchange.

**Explicitly does not own.** M3 SVG/PNG, the model contract, live rendering, or any STEP/IFC promise.

## Admission gate

A future format is implemented only when all of these are known:

1. a named external consumer and workflow;
2. the exact information that workflow consumes;
3. a compatibility/versioning policy and migration owner;
4. test fixtures produced by an independent reader;
5. licensing and redistribution terms acceptable to the project.

“A user may want it” is not evidence. A sample target application and a round-trip or import test are.

## Candidate formats

| Format | Decision | Rationale and promotion evidence |
|---|---|---|
| DXF | Candidate for M6 | Prefer documented ASCII DXF over proprietary DWG. Promote when a named CAD workflow supplies layer/block conventions and at least two independent import targets. |
| Versioned JSON model | Candidate for M6 | Promote only for a real programmatic consumer. It is a public compatibility commitment, not merely a download button; the envelope carries language, contract, catalogue, and property-backend versions. |
| Spreadsheet (`.xlsx`) | Candidate | The equipment list's CSV ([`73-equipment-list`](73-equipment-list.md)) needs no gate — every spreadsheet reads RFC 4180. `.xlsx` is a library dependency, a second compatibility surface, and a versioned format, so it needs one. Promote when a workflow demonstrably cannot use the CSV. |
| XML | Not promised | Adds an XSD and a second compatibility surface without adding information. Reconsider only when a named consumer cannot use the versioned JSON model. |
| DWG | Declined | Proprietary and version-specific; use DXF or a proven licensed conversion dependency. |
| STEP / IFC | Declined | The model has topology and computed 2D diagram placement, not physical 3D routes, elevations, fittings, or equipment solids. Inventing geometry would create a misleading engineering artefact. |

## Future contracts

DXF consumes an immutable export scene supplied by the frontend: model contract, computed placements,
routes, and Core `SymbolDefinition`s (`D-20`). It maps symbols to blocks, routes to polylines, labels to text,
and component kinds to documented layers. It never reads frontend DOM structure or internal Core
objects.

Versioned model interchange serializes a stable public envelope rather than exposing an internal
runtime object graph:

```json
{
  "format": "fluidscript-model",
  "formatVersion": 1,
  "contractVersion": "2.0",
  "provenance": {
    "sourceHash": "sha256:…",
    "languageMajor": 1,
    "catalog": { "id": "steel-en10255", "version": "2026.1" },
    "propertyBackend": { "id": "sharp-prop", "version": "…" },
    "atmosphereKPaAbsolute": 101.325
  },
  "model": {}
}
```

Unknown required major versions are rejected. Unknown additive fields within a supported major are
ignored and preserved by any tool claiming round-trip support.

## Invariants

1. An exporter never invents engineering data or physical geometry absent from its input.
2. Every output identifies its format, language, contract, catalogue, property backend, and source.
3. A frontend-derived drawing exporter receives one immutable scene; source edits cannot alter it
   while export is in progress.
4. Failure produces no partial file presented as successful.
5. A new public interchange format must pass the admission gate before entering a milestone.

## Error cases

| Situation | Required result |
|---|---|
| Consumer requests an unsupported major | Reject with supported versions and migration guidance |
| DXF scene contains an unresolved symbol or route | Refuse the export; identify the unresolved stable id |
| Independent reader rejects the output | Milestone remains incomplete; do not label the format supported |
| A requested format lacks the model data it implies | Explain the missing model capability; never synthesize it |
| Export is cancelled or fails | Discard the incomplete artefact and leave the source unchanged |

## Worked example

A facilities team demonstrates that its CAD workflow imports ASCII DXF with one layer per component
kind and block attributes for stable id, tag, and nominal size. The implementation proposal names
AutoCAD and LibreCAD import fixtures, maps the M3 export scene to those layers, and documents the DXF
revision. That passes the consumer, information, testing, and licensing gates. “Support DWG because
CAD users know it” passes none of them.

## Acceptance criteria

- [ ] A proposed format names its consumer, workflow, version policy, independent reader, and licence.
- [ ] DXF, if promoted, consumes posted placements and Core symbols without DOM or Core-runtime access.
- [ ] Versioned interchange rejects unsupported majors and includes all provenance fields above.
- [ ] XML, DWG, STEP, and IFC remain unavailable unless a new decision explicitly supersedes this one.
- [ ] Every failed export leaves no file that could be mistaken for a valid result.

## Open questions

None. Format selection is evidence-driven under the admission gate; a concrete consumer proposal is
new evidence, not an unresolved design choice.
