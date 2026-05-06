# Iteration 4 Performance Baseline

This is a smoke baseline, not a benchmark suite.

Command:

```bash
dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- \
  analyze tests/CodeMetricsToolkit.TestAssets \
  --output artifacts/iteration4-medium
```

Expected scale on the checked-in test assets:

```text
projects: multiple fixture projects
files: > 10 C# files
artifacts: manifest, summary, metrics, graph, chunks, diagnostics
```

Local baseline recorded on 2026-05-06:

```text
smoke threshold: under 30 seconds
verification: covered by CliAnalyzeTests.AnalyzeCommandCompletesMediumRepoSmokeWithinThreshold
```
