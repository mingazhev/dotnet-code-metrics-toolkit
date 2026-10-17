# Current State

.NET Code Metrics Toolkit is an independent .NET 10 metrics product. Autoresearch remains a
consumer example and is not a runtime dependency or the source of metric semantics.

## Product surface

- `CodeMetricsToolkit.Tool` installs the `codemetrics` command.
- `CodeMetricsToolkit.Core` discovers C# inputs, collects syntax and semantic facts, and
  writes a validated artifact generation.
- `CodeMetricsToolkit.Scoring` is an optional, separately packaged profile engine for
  consumers that need a reproducible scalar objective.
- The artifact contract is versioned independently of metric formulas. The current
  vocabulary is defined in `schemas/`; `codemetrics list-metrics` is the source of truth
  for supported IDs, versions and target populations.
- Semantic runs use MSBuild/Roslyn and fail closed when restore, workspace loading,
  compiler diagnostics, or selected-source coverage are not trusted.
- Syntax-only analysis remains available for safer first-pass inspection.

## Verification

The repository requires locked restore, a warning-free Release build and the complete
Release test suite. CI repeats those checks on Linux, macOS and Windows, then packs and
smoke-tests the CLI and Scoring packages as external consumers using a local package source.
Runtime validation enforces embedded JSON Schemas and cross-artifact invariants.

A historical passing count is not a release guarantee. Use the checks on the exact PR or
commit being evaluated; the test suite and catalog change as contracts are extended.

## Known boundaries

- Packages are built from source and are not yet published to nuget.org.
- `hotspot_rank` is a versioned, population-relative navigation heuristic, not a
  universal quality score.
- Semantic analysis may execute restore/MSBuild logic from the target repository; use an
  isolated container or VM for untrusted inputs.
- Artifact directory replacement is rollback-safe for ordinary exceptions, but supports
  one writer and readers that start only after analysis completes.
- Analysis is sequential. Parallel execution is not part of the current CLI contract.

See [README.md](README.md), the [CLI reference](docs/toolkit/cli.md), and the
[security model](docs/toolkit/security.md) for the supported product contract.
