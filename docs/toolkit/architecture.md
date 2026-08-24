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
