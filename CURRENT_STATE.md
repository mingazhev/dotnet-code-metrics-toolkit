# Current State

## Active Iteration

Post-Iteration 4 review / next MVP slice

## Completed

- Git repository initialized in `/Users/mingazhev/Repos/meetups/ods_autoresearch` with baseline commit `935eaa5`.
- Iteration worktree created at `/Users/mingazhev/Repos/meetups/ods_autoresearch-iteration0` on branch `iteration-0-contract-first`.
- Iteration 0 solution skeleton created: `CodeMetricsToolkit.sln`, `Abstractions`, `Core`, `Cli`, and `Tests`.
- SDK pinned with `global.json` to .NET SDK `8.0.410`; projects target `net8.0`.
- Roslyn packages pinned to `4.11.0` because SDK `8.0.410` uses compiler `4.11.0`.
- Mandatory output schemas created for `manifest`, `summary`, `metric-result`, `graph`, `chunk`, and `diagnostic`.
- Schema validation tests created with valid fixture output and malformed negative cases.
- Golden sample projects created under `tests/CodeMetricsToolkit.TestAssets`.
- ADRs created for stable target ids, cyclomatic complexity v1.0, and cognitive complexity baseline.
- `implementation_checklist.md` updated for completed Iteration 0 items.
- Iteration 1 syntax MVP implemented.
- `codemetrics analyze <path> --output <dir>` writes `manifest.json`, `summary.json`, `metrics.ndjson`, and `diagnostics.ndjson`.
- Syntax fallback target ids are emitted for file, type, and member targets.
- Syntax metrics implemented: `lines_of_code`, `non_comment_lines_of_code`, `method_length`, and `parameter_count`.
- CLI smoke tests cover `SimpleProject`, a directory without `.sln`, multiple `.csproj` files, schema-valid output, and generated-file exclusion.
- Iteration 1 writes schema-valid empty placeholders for `graph.json` and `chunks.ndjson` only because `manifest.json` requires all mandatory artifact names.
- Iteration 2 graph and chunks implemented.
- `graph.json` now emits file/type/member nodes, `declares` and `contains` edges, and semantic `inherits`, `implements`, `uses_type`, and best-effort `calls` edges.
- `chunks.ndjson` now emits file/type/member chunks with source ranges, token estimates, SHA-256 text hashes, and related target ids.
- `--include-chunk-text` adds source text to chunk lines when needed.
- `--syntax-only` forces the syntax fallback path and keeps graph/chunk output schema-valid.
- Partial type declarations are merged into one logical type node when semantic ids are available.
- Iteration 3 complexity and ranking implemented.
- Shared `ControlFlowFacts` now feeds `cyclomatic_complexity@1.0.0`, `cognitive_complexity@0.1.0`, and `nesting_depth@1.0.0`.
- `summary.json` now includes deterministic top-N hotspots with reasons and component values, percentiles, and weights.
- `metrics.ndjson` now includes `hotspot_rank@1.0.0` for member, type, and file targets.
- Metric formulas are documented next to the implementation in `src/CodeMetricsToolkit.Core/Metrics/README.md`.
- Cyclomatic tests cover every decision and non-decision point listed in ADR 0002.
- Iteration 4 diagnostics and hardening implemented.
- `diagnostics.ndjson` now includes syntax, compiler, nullable, project-load, and available analyzer diagnostic tags.
- Invalid project files emit `project_load_failed` with `critical` severity while syntax fallback continues.
- Lightweight semantic compilation honors SDK implicit usings to avoid false compiler diagnostics.
- `codemetrics validate-output <artifact-dir>` validates mandatory artifacts and JSON/NDJSON parseability, returning exit code 2 on validation failures.
- Snapshot normalization utility removes volatile paths, timestamps, durations, and sorts known arrays for stable tests.
- Medium-repo smoke coverage and performance baseline were added.
- Long analysis/ranking/control-flow loops now honor cancellation tokens.

## In Progress

- No implementation slice is currently in progress.

## Next Actions

- Review MVP readiness against `autoresearch_metrics_mvp.md`.
- Decide whether to start Iteration 5 as packaging/docs polish or branch into full roadmap items.
- Consider adding real MSBuildWorkspace loading before analyzing production repositories; current semantic loading is still lightweight.

## Verification

- `dotnet --version`: passed, returned `8.0.410`.
- `dotnet restore CodeMetricsToolkit.sln`: passed.
- `dotnet build CodeMetricsToolkit.sln`: passed.
- `dotnet test CodeMetricsToolkit.sln --no-restore`: passed, 44 tests.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/SimpleProject --output artifacts/iteration2-simple`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/SemanticGraphProject --output artifacts/iteration2-semantic`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/PartialTypesProject --output artifacts/iteration2-partial`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/ComplexityProject --output artifacts/iteration3-complexity --top 3`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets --output artifacts/iteration4-medium --top 5`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/iteration4-medium`: passed.

## Known Decisions

- Work for implementation should happen in `/Users/mingazhev/Repos/meetups/ods_autoresearch-iteration0`, not the main planning worktree.
- Test asset projects are intentionally not added to the solution; `BrokenProject` and nullable diagnostics fixtures are analyzer inputs, not normal build targets.
- `graph.json` and `chunks.ndjson` remain first-class product artifacts.
- `targetIdStability` is required for metric results and chunks.
- `graph.json` and `chunks.ndjson` are no longer placeholders after Iteration 2.
- `cognitive_complexity` remains `0.1.0` and explicitly Sonar-inspired, not Sonar-compatible.
- LOC is deliberately capped at low hotspot weight so it does not dominate complexity signals.
- Semantic analysis still uses lightweight Roslyn compilations, not full MSBuildWorkspace project loading.
- Synthetic implicit-usings trees are used for semantic accuracy but their own diagnostics are filtered from output.

## Blockers

- None.
