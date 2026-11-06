# MVP Limitations

This document records MVP requirements that are intentionally partial or not
implemented because implementing them correctly would require a larger, separately
versioned product slice.

## Semantic Loading

The MVP now uses `MSBuildWorkspace` for semantic analysis and runs
`dotnet restore` by default before loading the solution or project files.
`--no-restore` skips that restore step.

Impact:

```text
semantic ids, dependency edges and compiler diagnostics are trusted only when analysisHealth.trustedDiagnostics is true
```

If restore fails, MSBuild reports project-load failures, or a compilation cannot resolve
the framework's `System.Object`, the run is marked
`analysisQuality=degraded`. In degraded runs compiler diagnostics are suppressed
from `diagnostic_count` metrics and excluded from `hotspot_rank` so failed
environment setup does not look like code quality.

`trusted` is not a successful-build guarantee. Missing package symbols can still appear
as compiler errors in a loaded compilation with valid framework references. Inspect
`diagnostics.ndjson` and restore dependencies before treating them as source defects.
This is especially relevant to `--no-restore`; the framework check does not replace restore.

MSBuild can still be affected by files above the analyzed root, such as
`Directory.Build.props`, `Directory.Packages.props`, and `NuGet.config`. The
summary health messages report these ambient files. Historical snapshots should
be analyzed from an isolated directory, not inside this tool's repository tree.
Use `--isolate-input` when the input lives under a parent directory that might
contain unrelated MSBuild or NuGet files.

## Parallelism

The current analysis pipeline is sequential for correctness and deterministic output
ordering. There is no public parallelism option. Adding one requires a dedicated pass
over Roslyn object access, shared caches, diagnostics ordering, and snapshot stability.

## Include and Exclude Patterns

`--include` and `--exclude` apply to discovered `.cs` source files. Project files
are still discovered so semantic mode can build the best available compilation
context.

## Project-Load Diagnostic Attribution

`diagnostics.ndjson` includes project-load diagnostics even when they do not
have a source span. `diagnostic_count@1.1.0` is only emitted for source-linked
file/type/member targets, so source-less project-load diagnostics are not
attributed to that metric.

When diagnostics are not trusted, `diagnostic_count@1.1.0` is not emitted at
all. Consumers must check `summary.json.analysisHealth.trustedDiagnostics`
before comparing diagnostic metrics across runs.

Partial declarations contribute all their source spans, with each diagnostic counted
once per target. Attribution uses line overlap; it cannot distinguish two declarations
on the same line. Records without both start and end lines are not attributed.

## Member and control-flow populations

Member metrics cover declared methods, constructors, destructors, operators, properties
and indexers. Top-level statements and field/event initializers do not receive member
targets. Delegate declarations do not receive type targets. Nested lambdas/local functions
contribute to their enclosing member's syntax/operation metrics, but separate nested CFGs
are not collected. CFG complexity clamps `E - N + 2P` to at least 1 and is not a complete
exception-aware path count. See the [metric guide](toolkit/metrics-guide.md).

## Call Graph Precision

`calls` edges are best-effort Roslyn symbol edges. Dynamic dispatch, reflection,
source generators and unresolved external symbols are not modeled precisely in
the MVP.

## Duplication

Duplication compares exact token kinds and source text. Identifier renames and equivalent
expressions are not normalized. Linked files with different preprocessor configurations
use the same first analyzed context as the other physical-file metrics.

The collector has fixed token, window and work budgets; exceeding one fails the analysis.
It does not publish a partial count or silently skip duplication on large inputs. See the
[metric formulas](../src/CodeMetricsToolkit.Core/Metrics/README.md) for thresholds and limits.
