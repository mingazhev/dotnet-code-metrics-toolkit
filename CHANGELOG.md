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

## [0.1.0] - Unreleased

Initial contract-first analyzer implementation with 19 metrics, stable target ids,
semantic and syntax-only analysis, graph/chunk artifacts, diagnostics, and hotspot
navigation.
