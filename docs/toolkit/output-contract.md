# Output Contract

An analysis directory is valid only when all six mandatory artifacts exist, every JSON
document/NDJSON row validates against the schema for the declared contract version, and
cross-artifact invariants hold.

## Files

### `manifest.json`

Declares `schemaVersion`, tool/package version, reported root, timestamps, duration,
analysis mode, input selection, analysis options, and the exact mandatory filenames.
The input block records whether the caller selected a directory, project, or solution;
the explicit selected path when applicable; and a SHA-256 digest of the normalized
source-file population. This makes selection drift detectable without publishing source
contents.

The manifest is the completion marker and is written last. A completed directory is a
self-consistent generation, but directory replacement is not a concurrent-reader
transaction or a crash-proof database commit. Use one writer per output path and read
only after `codemetrics analyze` has exited successfully.

### `summary.json`

Contains project/file/type/member/metric/diagnostic counts, `analysisHealth`, and top
hotspots. Consumers must inspect `analysisHealth` before using semantic ids or diagnostics
as comparable measurements.

### `metrics.ndjson`

Each non-empty line is an independent metric observation with:

- metric id and metric version;
- target id, kind, and id stability;
- exactly one typed value;
- unit and optional source location/tags.

Metric identity is the pair `metricId@metricVersion`. The same id at another version can
have different semantics and must not be silently mixed in a time series.

### `graph.json`

Contains typed nodes and directed edges. Every edge endpoint must reference a node in the
same graph. Semantic relationship edges are best-effort when project loading is partial.

### `chunks.ndjson`

Maps source ranges to stable targets with a SHA-256 content hash and token estimate. Source
text is omitted by default; `--include-chunk-text` opts into embedding it.

### `diagnostics.ndjson`

Contains syntax/compiler/workspace/project-load observations. Source-less project-load
failures remain first-class rows but cannot be attributed to file/type/member metric spans.

## Schemas and compatibility

Schemas live under [`schemas/`](../../schemas/) and use JSON Schema Draft 2020-12. They are
embedded into the packaged tool so `validate-output` does not depend on a source checkout.

Compatible additions require both writer and schema changes. Breaking changes increment
`schemaVersion`. Metric formula changes increment that metric's version even if the
artifact schema itself is unchanged.

Never compare runs as equivalent when any of these differ unexpectedly:

- artifact contract version;
- metric version;
- analysis mode or target-id stability;
- diagnostic trust state;
- selected source population.

`validate-output` checks schemas and cross-file invariants, including summary counts,
graph endpoints, referenced targets, root paths, and typed metric values. It rejects
required artifact files that are symbolic links.
