# MVP Limitations

This document records MVP requirements that are intentionally partial or not
implemented because implementing them correctly would require a larger product
slice than the current autoresearch metrics contract.

## Semantic Loading

The MVP uses lightweight Roslyn compilations over discovered C# files. It does
not run a full `MSBuildWorkspace` load, restore packages, evaluate every target
framework, or reproduce all project-system behavior.

Impact:

```text
semantic ids, dependency edges and compiler diagnostics can be partial on complex production repositories
```

The CLI accepts `--no-restore` because the MVP never performs restore. A future
MSBuildWorkspace slice must decide online/offline restore behavior explicitly.

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

## Call Graph Precision

`calls` edges are best-effort Roslyn symbol edges. Dynamic dispatch, reflection,
source generators and unresolved external symbols are not modeled precisely in
the MVP.
