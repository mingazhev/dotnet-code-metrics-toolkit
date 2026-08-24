# Changelog

All notable changes to CodeMetricsToolkit are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow semantic
versioning for the tool package. Artifact and metric contracts are versioned separately.

## [Unreleased]

### Added

- Standalone .NET tool packaging for the `codemetrics` command.
- Runtime JSON Schema validation for all emitted artifacts.
- Product documentation, security guidance, CI, and package-install smoke coverage.
- Explicit CLI help, version, trust gates, and automation-oriented exit behavior.
- Optional, independently packaged `CodeMetricsToolkit.Scoring` profile engine.
- Project nodes and project-to-file edges in the public graph.
- Input-selection and source-population provenance in `manifest.json`.

### Changed

- CodeMetricsToolkit now lives in a dedicated product repository; autoresearch is
  documented as one optional consumer.
- The complete build, test, tool, package-smoke, and fixture surface targets .NET 10.
- Roslyn was updated to 5.6.0 and MSBuild compile-time references to 18.6.3.
- The vulnerable transitive `System.Security.Cryptography.Xml` version is pinned to
  the patched 10.0.10 release without shipping a private MSBuild runtime.
- Tool version metadata is sourced from package/assembly metadata instead of a literal.
- Semantic trust now requires complete coverage of the selected source population.

### Fixed

- Untrusted diagnostics no longer affect hotspot component weights.
- Explicit project/solution selection no longer widens silently to sibling projects.
- Artifact validation now enforces schemas, typed metric values, cross-file references,
  and required-file ownership at runtime.
- Semantic restore now launches an absolute `dotnet` muxer path instead of searching the
  process current directory.
- Isolation copy, source discovery, and syntax-only source reads reject non-regular files
  and apply the documented file-count, depth, and per-file size ceilings.
- Output-versus-input overlap now compares fully resolved symlink paths.
- Scoring rejects artifact directories and required artifacts that are symbolic links,
  and applies `allowedTargetIdStabilities` only to selected scored targets.
- Isolation quota failures and other `InvalidDataException` analyze failures map to CLI
  exit 1 instead of internal error 70.
- Publication replace-vs-refuse reads existing `manifest.json` through the same JSON size
  and depth ceilings as `validate-output`, and refuses a reparse-point manifest.
- Canceled restore no longer leaves unbounded stdout/stderr drains, and leftover isolated
  input cleanup failures are attached to the thrown exception.
- Trusted semantic runs with no type or member declarations now emit `manifest.mode=semantic`
  instead of `syntax`.
- Member fallback identities that embed a line are tagged `line_fallback`.
- Scoring identity-stability pins apply to type/member rows, so a semantic pin can still
  score structural file metrics.
- Default discovery now enforces the 4 GiB aggregate ceiling and rejects non-regular
  `.sln`/`.csproj` inputs before parsing.
- `validate-output` and scoring reject FIFOs and devices, not only reparse points.
- Failed restore process text is no longer copied into `summary.json`.
- Semantic graph metrics are omitted for degraded analysis instead of publishing
  untrusted type-dependency counts under the same metric ids.
- Source snapshots stored on analysis facts are plain text, not Roslyn `SourceText`.
- Diagnostic and graph-edge collection fail closed at the 1,000,000-record consumer ceiling.

## [0.1.0] - Unreleased

Initial contract-first analyzer implementation with 19 metrics, stable target ids,
semantic and syntax-only analysis, graph/chunk artifacts, diagnostics, and hotspot
navigation.
