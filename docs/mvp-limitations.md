# MVP Limitations

This document records MVP requirements that are intentionally partial or not
implemented because implementing them correctly would require a larger product
slice than the current autoresearch metrics contract.

## Semantic Loading

The MVP now uses `MSBuildWorkspace` for semantic analysis and runs
`dotnet restore` by default before loading the solution or project files.
`--no-restore` skips that restore step.

Impact:

```text
semantic ids, dependency edges and compiler diagnostics are trusted only when analysisHealth.trustedDiagnostics is true
```

If restore fails or MSBuild reports project-load failures, the run is marked
`analysisQuality=degraded`. In degraded runs compiler diagnostics are suppressed
from `diagnostic_count` metrics and excluded from `hotspot_rank` so failed
environment setup does not look like code quality.

MSBuild can still be affected by files above the analyzed root, such as
`Directory.Build.props`, `Directory.Packages.props`, and `NuGet.config`. The
summary health messages report these ambient files. Historical snapshots should
be analyzed from an isolated directory, not inside this tool's repository tree.
Use `--isolate-input` when the input lives under a parent directory that might
contain unrelated MSBuild or NuGet files.

## Parallelism Option

The CLI accepts `--max-degree-of-parallelism` as part of the MVP command
surface, but the current analysis pipeline remains sequential for correctness
and deterministic output ordering.

Implementing real parallel analysis requires a dedicated pass over the fact
collectors to verify Roslyn object access, shared caches, diagnostics ordering
and snapshot stability.

## Include and Exclude Patterns

`--include` and `--exclude` apply to discovered `.cs` source files. Project files
are still discovered so semantic mode can build the best available compilation
context.

## Project-Load Diagnostic Attribution

`diagnostics.ndjson` includes project-load diagnostics even when they do not
have a source span. `diagnostic_count@1.0.0` is only emitted for source-linked
file/type/member targets, so source-less project-load diagnostics are not
attributed to that metric.

When diagnostics are not trusted, `diagnostic_count@1.0.0` is not emitted at
all. Consumers must check `summary.json.analysisHealth.trustedDiagnostics`
before comparing diagnostic metrics across runs.

## Call Graph Precision

`calls` edges are best-effort Roslyn symbol edges. Dynamic dispatch, reflection,
source generators and unresolved external symbols are not modeled precisely in
the MVP.
