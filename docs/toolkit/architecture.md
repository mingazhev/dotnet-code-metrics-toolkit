# Architecture

CodeMetricsToolkit separates source facts from consumer gates and objectives.

## Components

### `CodeMetricsToolkit.Core`

Discovers inputs, obtains Roslyn syntax/semantic facts, constructs stable target ids,
projects metrics/graphs/chunks, ranks navigation hotspots, writes artifacts, and validates
the emitted contract. It also owns the artifact-name and contract-version constants; the
contract version is independent of the tool package version, while the JSON vocabulary is
defined by the schemas. The hotspot rank is a versioned, opinionated navigation heuristic;
Core does not know about CI, autoresearch, or a repository's pass/fail policy.

### `CodeMetricsToolkit.Cli`

Provides the installable `codemetrics` process, argument/exit-code contract, cancellation,
and human-readable diagnostics. Automation should consume files, not scrape descriptive
console text.

### `CodeMetricsToolkit.Scoring` (optional package)

Consumes completed artifact generations through a strict, versioned profile and emits
flat numeric values plus provenance hashes. It has no dependency on analyzer Core or the
tool package. Its thresholds remain consumer policy, not analyzer semantics.

### Optional policy consumers

A consumer may map raw observations into a repository-specific score or gate. That code
must remain outside analyzer Core. Thresholds are properties of a team and codebase, not
universal meanings of cyclomatic complexity or dependency counts.

## Analysis modes

Semantic mode restores and loads projects with `MSBuildWorkspace`. It provides stable
symbol ids, internal dependency edges, and compiler diagnostics when loading succeeds.
Any restore or project-load failure degrades the run and makes diagnostics untrusted.

Syntax-only mode parses discovered C# files directly. It is safer and more portable, but
target ids and dependency information are less stable/complete. Every observation carries
the stability/mode information required for consumers to distinguish these cases.

## Determinism

Collections are ordered before serialization, metric formulas are versioned, and volatile
manifest fields are isolated from metric rows. Consumers comparing runs should normalize
root paths and manifest timing fields, and must reject comparisons whose contract, metric
version, analysis mode, or trust state changed unexpectedly.

## Ownership inside Core

- `Analysis` coordinates discovery, workspace loading, facts, metrics, ranking and publication.
- `Discovery` selects and optionally isolates inputs, retaining the same exclusion policy
  and filesystem safety limits for both paths.
- `Syntax` owns Roslyn traversal and extraction; `Facts` holds observations without
  dependencies on artifact writers, the CLI or consumer policy.
- `Metrics` owns the catalog, metric projections, graph calculations and hotspot ranking.
  A metric descriptor owns its ID, version, unit, value kind and supported target kinds.
  One result factory applies that metadata and rejects non-finite values or fractional
  counts. Ranking receives already projected component values; it does not run projectors
  or implement their formulas again.
- `Reporting` projects graph/chunk artifacts and publishes one complete generation.
- `Contracts` owns target identities, metric/hotspot result records, artifact names,
  versions and shared resource ceilings. Metrics and Reporting use these records directly;
  metric calculations do not depend on publication or discovery.
- `Validation` checks untrusted artifact input against schemas and cross-artifact rules.

A physical file can belong to several projects. Project membership is represented once;
solution line totals count unique physical paths, while semantic targets remain scoped to
projects. Partial type declarations and merged member targets have different populations:
file `type_count` counts declarations, and type `member_count` counts logical members.

These are boundaries within one analyzer project. They do not require a project, interface
or plugin registry for each folder or metric. The independent Scoring package deliberately
keeps its own small contract reader instead of taking a dependency on Core/Roslyn.

The cleanup renames public Core C# types and namespaces to match these responsibilities.
Core is not a separately published package; callers compiling against its source must
update imports and renamed members. `MetricResultLine`, `HotspotLine` and
`HotspotComponentLine` now live in `Core.Contracts`. `HotspotRanker.Rank` requires the
projected metric rows, and `MetricDescriptor` declares `ValueKind`. Wire metric IDs and
serialized result fields are independent of these C# changes.

All current producers emit numeric metrics. Their shared `MetricResultLine` describes
that output; the wire schema and independent Scoring reader also understand string and
boolean values. No unused string/boolean producer model or per-metric interface is needed.
