# .NET Code Metrics Toolkit

.NET Code Metrics Toolkit is a standalone command-line product for producing deterministic,
machine-readable facts about C# codebases. It analyzes source structure, complexity,
dependencies, diagnostics, graph relationships, and source chunks without prescribing
how those facts must be used.

Current language support is C# only. The .NET name describes the SDK/MSBuild ecosystem
the tool integrates with; it is not a claim that F# or Visual Basic are analyzed today.

The analyzer does not enforce a quality gate or universal objective. CI gates,
dashboards, audits, and optimization loops are downstream consumers of the same
versioned artifact contract. The one built-in opinion is `hotspot_rank`: an explicitly
documented navigation heuristic that consumers must not treat as ground truth.

## What it produces

Each analysis writes one self-contained artifact directory:

| Artifact | Purpose |
| --- | --- |
| `manifest.json` | Tool, contract, input, timing, mode, and artifact inventory |
| `summary.json` | Counts, analysis health, trust signals, and top hotspots |
| `metrics.ndjson` | One versioned metric observation per line |
| `graph.json` | Project/file/type/member nodes and dependency edges |
| `chunks.ndjson` | Stable source ranges and hashes for downstream retrieval |
| `diagnostics.ndjson` | Syntax, compiler, nullable, workspace, and project-load diagnostics |

The current catalog contains 19 raw and derived metrics. Run `codemetrics list-metrics`
for the authoritative list and `codemetrics explain <metric-id>` for a formula and its
known limitations.

## Quick start

.NET Code Metrics Toolkit targets .NET 10. This repository currently builds the tool package
from source; it is not published to nuget.org.

```bash
dotnet restore CodeMetricsToolkit.sln --disable-parallel -m:1
dotnet pack src/CodeMetricsToolkit.Cli/CodeMetricsToolkit.Cli.csproj \
  --configuration Release \
  --output artifacts/packages
dotnet tool install CodeMetricsToolkit.Tool \
  --global \
  --add-source artifacts/packages \
  --version 0.1.0
```

Analyze a trusted repository with semantic project loading:

```bash
codemetrics analyze /path/to/repository \
  --output artifacts/codemetrics
codemetrics validate-output artifacts/codemetrics
```

For an unfamiliar or untrusted repository, start with syntax-only mode:

```bash
codemetrics analyze /path/to/repository \
  --syntax-only \
  --output artifacts/codemetrics
```

Semantic mode runs `dotnet restore` and loads projects through MSBuild. MSBuild project
evaluation can execute targets from the analyzed repository. Read the
[security model](docs/toolkit/security.md) before analyzing code you do not trust.

## Product boundaries

The supported analyzer pipeline is:

```text
C# source / solution
        |
        v
discover -> parse/load -> collect facts -> project metrics -> validate artifacts
        |
        v
versioned JSON + NDJSON contract
```

The toolkit does not define a universal "code quality" score. `hotspot_rank` is an
opinionated navigation signal, not a release gate. Consumers that need an objective function should
derive it from raw, versioned metrics and keep that policy outside analyzer Core.

The repository also ships `CodeMetricsToolkit.Scoring` as a separate optional NuGet
library. It provides a small version-pinned profile engine for consumers that want a
reproducible scalar objective; the `codemetrics` collector neither references nor
requires that package.

See:

- [CLI reference](docs/toolkit/cli.md)
- [Output contract](docs/toolkit/output-contract.md)
- [Architecture](docs/toolkit/architecture.md)
- [Security and trust model](docs/toolkit/security.md)
- [Metric formulas](src/CodeMetricsToolkit.Core/Metrics/README.md)
- [Metric expansion research](docs/research/metric-expansion.md)
- [Known limitations](docs/mvp-limitations.md)
- [Autoresearch integration example](docs/toolkit/autoresearch-integration.md)

## Development

```bash
dotnet restore CodeMetricsToolkit.sln --disable-parallel -m:1
dotnet build CodeMetricsToolkit.sln --configuration Release --no-restore
dotnet test CodeMetricsToolkit.sln --configuration Release --no-build --no-restore
```

The SDK is pinned by `global.json`, dependency versions are centralized in
`Directory.Packages.props`, and warnings are treated as errors. Contributions must keep
metric formulas and artifact schemas versioned. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Licensed under the [MIT License](LICENSE).

## Repository layout

```text
src/        analyzer libraries, CLI, and optional scoring package
tests/      contract, metric, CLI, and integration tests
schemas/    JSON Schema definitions for public artifacts
docs/       product architecture, contracts, security, and integration guides
eng/        package-level smoke tests
```
