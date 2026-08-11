# Current State

.NET Code Metrics Toolkit is an independent .NET 10 metrics product. Autoresearch remains a
consumer example and is not a runtime dependency or the source of metric semantics.

## Product surface

- `CodeMetricsToolkit.Tool` installs the `codemetrics` command.
- `CodeMetricsToolkit.Core` discovers C# inputs, collects syntax and semantic facts, and
  writes a validated artifact generation.
- `CodeMetricsToolkit.Scoring` is an optional, separately packaged profile engine for
  consumers that need a reproducible scalar objective.
- The public artifact contract is version `0.1.0`; the catalog currently contains 19
  versioned raw and derived metrics.
- Semantic runs use MSBuild/Roslyn and fail closed when restore, workspace loading,
  compiler diagnostics, or selected-source coverage are not trusted.
- Syntax-only analysis remains available for safer first-pass inspection.

## Release readiness

- Release build: 0 warnings, 0 errors.
- Test suite: 119 passed, 0 failed.
- Locked restore is verified against the committed lock files.
- Both NuGet packages are built and smoke-tested as external consumers from a local-only
  package source.
- CI covers Linux, macOS, and Windows, then packs and smoke-tests both packages.
- Runtime validation enforces embedded JSON Schemas and cross-artifact invariants.

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
