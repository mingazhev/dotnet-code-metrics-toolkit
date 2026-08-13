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
cross-platform CI smoke threshold: under 60 seconds
verification: covered by CliAnalyzeTests.AnalyzeCommandCompletesMediumRepoSmokeWithinThreshold
```

The generous ceiling detects hangs and gross regressions; it is not a comparative performance
benchmark because shared GitHub runners have variable startup, restore, and filesystem costs.
