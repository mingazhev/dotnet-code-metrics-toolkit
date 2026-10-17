# Output Contract

An analysis directory is valid only when all six mandatory artifacts exist, every JSON
document/NDJSON row validates against the schema for the declared contract version, and
cross-artifact invariants hold.

The current artifact contract is **0.2.0**. Tool/package versions and individual metric
versions are independent of this contract.

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

Tags distinguish slices of a metric on the same target. A tag array is an unordered set,
so each tag must be unique within a row and tag order does not change metric identity.
Duplication totals are untagged; tagged `duplicated_block_count=1` rows locate individual
duplicated regions. Consumers must select the intended tag set instead of summing totals
together with region rows.

### `graph.json`

Contains typed nodes and directed edges. Every edge endpoint must reference a node in the
same graph. Semantic relationship edges are best-effort when project loading is partial.

Nodes include solution, project, namespace, file, type and member targets. Namespace ids
are scoped to a project and an exact qualified name, including the global namespace.
Projects contain namespaces; namespaces contain their direct types. Nested namespace
names do not imply extra hierarchy nodes.

`depends_on` connects projects through declared direct project references and namespaces
through collected internal type dependencies. External targets are excluded. Module
dependency metrics are emitted only for trusted analysis; inspect health before using
the graph from a degraded run.

Namespace `declarations` identify source files for file selectors. Its primary `filePath`
is project metadata, not a declaration location. The summary keeps its existing counts;
namespace counts can be obtained from graph nodes.

### `chunks.ndjson`

Maps source ranges to stable targets with a SHA-256 content hash and token estimate. Source
text is omitted by default; `--include-chunk-text` opts into embedding it.

Chunk ids include the file, one-based start line and start column to distinguish source
occurrences. They can change after formatting even when the referenced semantic target
stays the same.

### `diagnostics.ndjson`

Contains syntax/compiler/workspace/project-load observations. Source-less project-load
failures remain first-class rows but cannot be attributed to file/type/member metric spans.

## Schemas and compatibility

Schemas live under [`schemas/`](../../schemas/) and use JSON Schema Draft 2020-12. They are
embedded into the packaged tool so `validate-output` does not depend on a source checkout.

Compatible additions require both writer and schema changes. Breaking changes increment
`schemaVersion`. Metric formula changes increment that metric's version even if the
artifact schema itself is unchanged.

To migrate from 0.1.0, regenerate all six artifacts together, update graph consumers for
the `namespace` target kind and `depends_on` edge kind, and set scoring profiles to
`artifactContractVersion: "0.2.0"`. The scoring profile's own `schemaVersion` remains
`1.0.0`. This release reads only the current artifact contract. Chunk ids now include the
start column, and `hotspot_rank@2.0.0` excludes unavailable components and normalizes the
remaining weights. The six existing type-dependency metrics use version `1.1.0` because
their input graph now also covers type declarations, including fields, signatures,
attributes and static member access. Update pinned metric versions intentionally and
compare regenerated baselines with the same versions and populations.

Never compare runs as equivalent when any of these differ unexpectedly:

- artifact contract version;
- metric version;
- analysis mode or target-id stability;
- diagnostic trust state;
- selected source population.

`validate-output` checks schemas and cross-file invariants, including summary counts,
graph endpoints, referenced targets, root paths, and typed metric values. It rejects
required artifact files that are symbolic links.
