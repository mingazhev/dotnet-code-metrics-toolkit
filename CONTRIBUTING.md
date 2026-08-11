# Contributing to CodeMetricsToolkit

## Prerequisites

- The .NET SDK selected by `global.json`.
- Git.

## Build and test

```bash
dotnet restore CodeMetricsToolkit.sln --disable-parallel -m:1
dotnet build CodeMetricsToolkit.sln --configuration Release --no-restore
dotnet test CodeMetricsToolkit.sln --configuration Release --no-build --no-restore
```

Before submitting a change to packaging, also build and install the generated tool in a
temporary tool directory. CI runs this smoke path from the `.nupkg`, not from project
output.

## Compatibility rules

Metrics and artifact schemas are public contracts.

- A formula change requires a new metric version.
- A breaking artifact change requires a new `schemaVersion` and matching schemas.
- Never silently reuse a metric id/version for different semantics.
- New output must remain deterministic after paths, timestamps, and durations are
  normalized.
- A degraded semantic analysis must never be presented as trusted.
- `hotspot_rank` is for prioritization; do not turn it into a generic quality gate.

Add or update all of the following when changing a public contract:

1. the implementation;
2. the catalog or contract constant;
3. the JSON Schema;
4. positive and negative tests;
5. user documentation and `CHANGELOG.md`.

## Test expectations

Prefer small golden C# projects under `tests/CodeMetricsToolkit.TestAssets` over mocks of
Roslyn or MSBuild. Every bug fix needs a regression test. Tests must not depend on an
absolute path, a developer's NuGet configuration, or a neighboring checkout.

Semantic tests may execute restore/MSBuild for the committed test assets. Syntax-only
tests should be used when semantic loading is irrelevant to the behavior under test.

## Pull requests

Keep changes focused and explain intent, relevant decisions, rejected alternatives, and
compatibility impact. A pull request is ready only when Release build, tests, package
installation smoke, and output validation pass from a clean checkout.
