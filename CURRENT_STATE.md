# Current State

## Active Iteration

Post-MVP hardening: trusted MSBuild semantic analysis

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
- Iteration 5 MVP completion implemented.
- `metrics.ndjson` now includes required MVP metrics `outgoing_type_dependency_count`, `dependency_cycle_count`, and `diagnostic_count`.
- Additional source-linked metrics were added for `incoming_type_dependency_count`, `type_count`, `member_count`, and file/type member complexity aggregates.
- `diagnostic_count@1.0.0` supports metric `tags`, including compiler and nullable tags when present.
- `hotspot_rank@1.0.0` now uses the MVP weights with diagnostics, parameter counts, member/type counts, and outgoing type dependencies.
- CLI now supports `list-metrics`, `explain <metric-id>`, `--include`, `--exclude`, `--semantic`, `--no-restore`, and `--max-degree-of-parallelism`.
- MVP limitations are documented in `docs/mvp-limitations.md`.
- Post-MVP production-readiness hardening implemented.
- Semantic analysis now uses `MSBuildWorkspace` instead of lightweight manual compilations.
- `codemetrics analyze` runs `dotnet restore` by default and records restore status in `summary.json.analysisHealth`.
- `summary.json` now includes `analysisHealth` with quality, semantic model, restore/build status, trusted diagnostics, hotspot diagnostic inclusion, and health messages.
- `diagnostic_count@1.0.0` is emitted only when diagnostics are trusted.
- `hotspot_rank@1.0.0` excludes diagnostic components when diagnostics are untrusted.
- Ambient parent MSBuild/NuGet files outside the analyzed root are reported as health messages.
- Compiler diagnostics are suppressed when restore fails so environment/setup failures are not reported as code-quality metrics.
- CLI now supports `--isolate-input` to copy the target root to a temporary directory before restore/MSBuild loading.

## In Progress

- No implementation slice is currently in progress.

## Next Actions

- Consider preserving original source-root metadata when `--isolate-input` is used; current artifact root points at the temporary isolated copy used for analysis.
- Consider adding real parallel analysis after the fact collectors have deterministic ordering and thread-safety coverage.

## Verification

- `dotnet --version`: passed, returned `8.0.410`.
- `dotnet restore CodeMetricsToolkit.sln`: passed.
- `dotnet build CodeMetricsToolkit.sln`: passed.
- `dotnet test CodeMetricsToolkit.sln --no-restore`: passed, 44 tests.
- `dotnet test CodeMetricsToolkit.sln`: passed, 47 tests after Iteration 5.
- `dotnet test CodeMetricsToolkit.sln`: passed, 47 tests after MSBuildWorkspace hardening.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze /private/tmp/codemetrics-inputs/payment-terminal.app.api-2025-12-30-5a511a95 --output artifacts/metrics/payment-terminal.app.api-2025-12-30-5a511a95-msbuild --top 20`: passed; trusted MSBuild run, 516 files, 838 types, 1514 members, 27317 metrics, 54 diagnostics.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/metrics/payment-terminal.app.api-2025-12-30-5a511a95-msbuild`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze artifacts/input/payment-terminal.app.api-2025-12-30-5a511a95 --output artifacts/check-contaminated --top 5`: passed; degraded run due parent MSBuild contamination, diagnostics untrusted and excluded from hotspot rank.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/check-contaminated`: passed.
- `dotnet run --project src/CodeMetricsToolkit.Cli -- analyze artifacts/input/payment-terminal.app.api-2025-12-30-5a511a95 --output artifacts/check-isolated --isolate-input --top 5`: passed; trusted MSBuild run from temporary isolated copy, 54 diagnostics.
- `dotnet run --project src/CodeMetricsToolkit.Cli -- list-metrics`: passed and lists 19 metric descriptors.
- `dotnet run --project src/CodeMetricsToolkit.Cli -- explain outgoing_type_dependency_count`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets --output artifacts/iteration5-medium --top 5`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/iteration5-medium`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets --output artifacts/iteration5-filtered --include 'SemanticGraphProject/*.cs' --exclude '**/Domain.cs' --top 3`: passed.
- `dotnet run --no-build --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/iteration5-filtered`: passed.
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
- Semantic analysis uses `MSBuildWorkspace` and project compilations loaded through the real project system.
- Target repositories should be analyzed outside this tool's worktree when MSBuild parent-file contamination is possible.
- `--isolate-input` is the preferred CLI fallback when a target root already lives under a contaminated parent directory.
- `--max-degree-of-parallelism` is accepted for CLI contract compatibility, but current analysis is still sequential.
- `--no-restore` skips the default restore step; diagnostics should be treated according to `analysisHealth`.
- Project-load diagnostics without source spans are emitted in `diagnostics.ndjson` but not attributed to `diagnostic_count` metrics.
- `diagnostic_count` metrics and hotspot diagnostic components require trusted diagnostics.

## Blockers

- None.
