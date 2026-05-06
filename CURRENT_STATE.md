# Current State

## Active Iteration

Iteration 2 - Graph and Chunks (next)

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

## In Progress

- No implementation slice is currently in progress.

## Next Actions

- Start Iteration 2 from `implementation_checklist.md`.
- Replace placeholder `graph.json` with real file/type/member graph nodes.
- Emit `declares` and `contains` edges.
- Add first real `chunks.ndjson` output with source ranges.
- Decide how much semantic loading belongs in Iteration 2 without breaking syntax fallback.

## Verification

- `dotnet --version`: passed, returned `8.0.410`.
- `dotnet restore CodeMetricsToolkit.sln`: passed.
- `dotnet build CodeMetricsToolkit.sln`: passed.
- `dotnet test CodeMetricsToolkit.sln`: passed, 11 tests.
- `dotnet run --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/SimpleProject --output artifacts/simple`: passed.

## Known Decisions

- Work for implementation should happen in `/Users/mingazhev/Repos/meetups/ods_autoresearch-iteration0`, not the main planning worktree.
- Test asset projects are intentionally not added to the solution; `BrokenProject` and nullable diagnostics fixtures are analyzer inputs, not normal build targets.
- `graph.json` and `chunks.ndjson` remain first-class product artifacts.
- `targetIdStability` is required for metric results and chunks.
- Do not treat the current empty `graph.json` and `chunks.ndjson` as completed graph/chunk implementation.
- Do not start complexity/ranking metrics until graph/chunk context exists.

## Blockers

- None.
