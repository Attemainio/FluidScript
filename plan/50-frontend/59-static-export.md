---
id: 59-static-export
title: Static SVG and PNG export
tier: 50-frontend
status: reviewed
owns: [M3 SVG export, M3 PNG export, export accessibility and provenance]
depends_on: [26-model-contract, 53-canvas-renderer, 55-design-system, 57-state-visualization]
traces_to: [R-23, R-24, R-28, R-31, R-37, R-39, R-42, R-44, R-47]
open_questions: 0
last_review_pass: 6
---

# Static SVG and PNG export

## Purpose

Produces the shareable output required by the usable static product. SVG/PNG are client-side because
the browser already owns placements; they use the same Core `SymbolDefinition`s as the live canvas.

## Responsibilities

**Owns.** Standalone SVG serialization, PNG rasterization, options, provenance, and accessible output.

**Explicitly does not own.** Layout (`53`), symbol definitions (`26`), durable source files (`58`),
or future DXF/model interchange (`71`).

## Contracts

```typescript
export function exportSvg(scene: PreparedScene, options: SvgExportOptions): Blob;
export function exportPng(scene: PreparedScene, options: PngExportOptions): Promise<Blob>;

export interface SvgExportOptions {
  theme: "light" | "dark";       // default light
  includeLegend: boolean;         // default true
  includeValues: boolean;         // default true
  includeAxes: boolean;           // default false
  text: "embed-font" | "paths";  // default embed-font, path fallback
}

export interface PngExportOptions extends SvgExportOptions {
  dpi: 96 | 150 | 300;
  transparentBackground: boolean;
}
```

Export uses a frozen `PreparedScene` containing model contract, computed placements, routes, labels,
styles, visualization, and export timestamp. Its geometric structure is specified by
[`53-canvas-renderer`](53-canvas-renderer.md) and cited here, not restated: `D-71` makes the scene the
verification target for both drawing paths, so the predicates that guard the canvas guard the export
with no export-side geometry test to write. It clones/reconstructs SVG from the generic declarative
symbol interpreter, inlines styles, embeds fonts or paths, sets a tight viewBox plus 5% margin, and
includes `<title>`/`<desc>`. PNG rasterizes that exact SVG; it has no separate drawing path.

`<desc>` records application version, model contract, language major, source hash, exact catalogue and
property-backend versions, atmosphere reference, solved/unsolved status, shown property/unit/scale, and
generation time.

**Tags are recorded as of the exported source hash, and the `<desc>` says so.** A tag is derived from
declaration order (`D-34`), so the same component can carry `400PU01` in one export and `400PU02` in a
later one after a pump was inserted above it. That is correct behaviour and it is also exactly the
kind of thing that makes someone distrust two drawings side by side. The source hash already in
`<desc>` is what distinguishes "the tags changed because the design changed" from "the tags changed
for no reason", and it is the reason tags may be exported at all. It does not embed the source text unless explicitly added in a future privacy review.

## Invariants

1. SVG is standalone: no network, external CSS, script, or unavailable font is required.
2. SVG and PNG resolve the same Core symbols, placements, routes, state, diagnostics, and theme.
3. Export never invents geometry or engineering data absent from the model/scene.
3a. No component appears at a corner in an exported diagram (`D-44`). This needs no separate
   enforcement — export serializes the same `PreparedScene` the canvas draws, so a violation would
   already have failed `53`'s invariant — but it is asserted on the export path too, because the
   exported file is the artefact that outlives the session and gets read by someone who cannot
   re-render it.
4. Unsolved values are absent/labelled “not solved”, never serialized as zero.
5. Component ids remain searchable and unique; text and warning meaning are not conveyed by colour alone.
5a. A component's visible label is its equipment tag where it has one, and its id otherwise (`D-34`).
   Both are present: the tag is the drawn text, the id is the element's `id` attribute. An exported
   diagram is the artefact a reader matches against an equipment schedule, so the tag has to be
   readable; a consumer diffing two exports has to key on something that survives an insertion, so the
   id has to be there too. Serializing only one loses one of those.
6. Export work may run in a worker, but its source scene is immutable and identified by source hash.
7. Export preserves the scene's left-to-right thermal stages and real per-connection fluid arrows; it
   never mirrors a return branch merely to make every arrow point right (`D-31`).
8. The exported geometry is the scene's geometry: every symbol origin, port anchor, route point and
   label box in the file is the value the scene carried. Export invents no placement and adjusts none,
   which is what lets `53`'s predicate sweep stand as the export's geometric test (`D-71`).

## Error cases

| Situation | Required result |
|---|---|
| Unsolved model | Export topology; state labels say “not solved” or are omitted according to option |
| Font embedding fails | Convert text to paths and report one non-blocking warning |
| Canvas dimensions exceed browser raster limit | Refuse PNG with exact dimensions; keep SVG available |
| Symbol/placement/route id cannot resolve | Refuse export; do not create a partial misleading file |
| Download fails | Keep generated Blob available for retry; source document state is unchanged |

## Worked example

The cooling loop exports to a light SVG whose `<desc>` names source hash, contract/language/catalogue
versions, and `show temperature` scale. `HE1` remains id `HE1`, its gradient and legend survive, and
the same SVG rasterized at 300 dpi produces the PNG.

## Acceptance criteria

- [ ] SVG opens identically in current Chrome/Edge/Firefox and Inkscape with networking disabled.
- [ ] PNG pixels match rasterization of the golden SVG at 96/150/300 dpi within image tolerance.
- [ ] Light export is legible on white regardless of current app theme; dark export is explicit.
- [ ] Every component id, label, state unit, warning cue, gradient, and legend survives as configured.
- [ ] Labels carry tags where the kind has a tag code, while element ids carry identifiers; an export
      of the distribution header contains `PU_AHU`/`PU_RAD` as element ids and `101PU01`/`102PU01` as
      drawn labels.
- [ ] `<title>`/`<desc>` pass screen-reader inspection and carry all required provenance.
- [ ] The canvas and exporter consume one symbol-definition golden set; no TypeScript kind-specific
      drawing implementation exists.
- [ ] Every symbol origin, port anchor, route point and label box parsed back out of an exported SVG
      equals the prepared scene's value for it, for each of the six reference circuits.
- [ ] Cooling-loop, substation, storage-header, and multi-conversion goldens preserve the prepared
      scene's stage ranks and per-connection arrows exactly; export performs no thermal reranking.
- [ ] Unsolved, oversized, missing-font, missing-symbol, and failed-download cases match the table.

## Open questions

None.
