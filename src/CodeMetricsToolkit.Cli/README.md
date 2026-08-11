# CodeMetricsToolkit.Tool

`CodeMetricsToolkit.Tool` installs the `codemetrics` command, a standalone .NET 10
analyzer that emits deterministic, machine-readable facts about C# codebases.

```bash
dotnet tool install CodeMetricsToolkit.Tool --global --version 0.1.0
codemetrics analyze /path/to/repository --output artifacts/codemetrics
codemetrics validate-output artifacts/codemetrics
```

The output generation contains a manifest, summary, versioned metric observations,
project/source graph, source chunks, and diagnostics. Run `codemetrics list-metrics` for
the catalog and `codemetrics explain <metric-id>` for formula details.

The default semantic mode may run `dotnet restore` and evaluate MSBuild files from the
target repository. Use `--syntax-only` for a non-executing first pass and read the
[security model](https://github.com/mingazhev/dotnet-code-metrics-toolkit/blob/main/docs/toolkit/security.md)
before analyzing untrusted code.

CodeMetricsToolkit does not define a universal quality score. `hotspot_rank` is a
versioned navigation heuristic. Repository-specific gates and objectives belong in a
downstream consumer or the optional `CodeMetricsToolkit.Scoring` package.

Full documentation:
[github.com/mingazhev/dotnet-code-metrics-toolkit](https://github.com/mingazhev/dotnet-code-metrics-toolkit)
