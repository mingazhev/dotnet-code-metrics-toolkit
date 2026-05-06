# Implementation Checklist

This checklist is the execution layer for `autoresearch_metrics_mvp.md`.

It exists so the next development pass can start from concrete tasks instead of re-reading the whole plan and rediscovering the same decisions.

Development workflow: `development_workflow.md`.
Context reset anchor: `CURRENT_STATE.md`.
Autonomous run instructions: `AUTONOMOUS_RUNBOOK.md`.

## 0. Planning Decisions Already Made

- [x] Keep the original broad plan as reference only: `code_metrics_toolkit_plan.md`.
- [x] Split the work into MVP and full roadmap.
- [x] Make autoresearch the first product, not a generic NDepend/Sonar clone.
- [x] Treat `graph.json` as a required product artifact.
- [x] Treat `chunks.ndjson` as a required product artifact.
- [x] Use stable `target_id` as a core contract, not a formatting detail.
- [x] Replace magic `hotspot_score` with explainable `hotspot_rank`.
- [x] Defer Halstead, MI, LCOM, WMC, NPath, package metrics and layer rules.
- [x] Require formula documentation as part of each metric DoD.
- [x] Require shared analysis passes before adding overlapping AST-based metrics.
- [x] Use orchestrator-led development with bounded subagents only.
- [x] Use `CURRENT_STATE.md` for context cleanup and handoff.
- [x] Create autonomous run instructions.

## 1. Local Tooling Assumptions

- [x] .NET SDK is available locally.
- [x] .NET 8 SDK is available locally.
- [x] .NET 10 SDK is also available locally, so implementation must pin SDK deliberately.
- [x] Add `global.json` to avoid accidentally building with .NET 10.
- [x] Target `net8.0` for libraries and CLI unless there is a deliberate reason to require newer .NET.
- [x] Pin Roslyn package versions explicitly.
- [x] Add `Directory.Build.props` with nullable enabled and warnings policy.
- [x] Add `.editorconfig` for formatting and analyzer consistency.

Recommended first `global.json`:

```json
{
  "sdk": {
    "version": "8.0.410",
    "rollForward": "latestFeature"
  }
}
```

Rationale: .NET 8 is installed and is the conservative runtime target. The machine also has .NET 10, but using it implicitly would make the project less portable and could change Roslyn/MSBuild behavior under us.

## 2. Stop Conditions

Do not start implementing the metric catalog until these are done:

- [x] JSON schemas exist for every mandatory output.
- [x] Golden sample repositories exist.
- [x] Stable target id ADR exists.
- [x] Complexity formula ADR exists.
- [x] Output validation test exists.
- [x] CLI end-to-end smoke test exists.

Do not add advanced metrics until these are done:

- [x] `graph.json` is emitted and schema-validated.
- [x] `chunks.ndjson` is emitted and schema-validated.
- [x] `summary.json` includes deterministic top-N hotspots.
- [x] Hotspot reasons are explainable from component metrics.
- [x] Syntax fallback works when semantic loading fails.

## 3. Iteration 0 - Contract First

- [x] Create solution skeleton.
- [x] Add `global.json`.
- [x] Add `Directory.Build.props`.
- [x] Add `.editorconfig`.
- [x] Create `src/CodeMetricsToolkit.Abstractions`.
- [x] Create `src/CodeMetricsToolkit.Core`.
- [x] Create `src/CodeMetricsToolkit.Cli`.
- [x] Create `tests/CodeMetricsToolkit.Tests`.
- [x] Create `tests/CodeMetricsToolkit.TestAssets`.
- [x] Add schema files under `schemas/`.
- [x] Define `manifest.schema.json`.
- [x] Define `summary.schema.json`.
- [x] Define `metric-result.schema.json`.
- [x] Define `graph.schema.json`.
- [x] Define `chunk.schema.json`.
- [x] Define `diagnostic.schema.json`.
- [x] Add schema validation tests.
- [x] Add ADR: stable target ids.
- [x] Add ADR: cyclomatic complexity v1.0.
- [x] Add ADR: cognitive complexity decision.
- [x] Add golden expected output without requiring real analysis.

Acceptance:

- [x] `dotnet build` succeeds.
- [x] `dotnet test` succeeds.
- [x] Schema validation rejects malformed output.
- [x] Target id examples cover overloads, generics, constructors, properties and partial types.

## 4. Iteration 1 - Syntax MVP

- [x] Implement project/file discovery.
- [x] Implement syntax-only C# loading.
- [x] Detect file nodes.
- [x] Detect namespace nodes where useful for parent context.
- [x] Detect type nodes.
- [x] Detect member nodes.
- [x] Emit syntax fallback target ids.
- [x] Calculate `lines_of_code`.
- [x] Calculate `non_comment_lines_of_code`.
- [x] Calculate `method_length`.
- [x] Calculate `parameter_count`.
- [x] Emit `manifest.json`.
- [x] Emit `summary.json`.
- [x] Emit `metrics.ndjson`.
- [x] Emit `diagnostics.ndjson`.
- [x] Add CLI command `codemetrics analyze <path> --output <dir>`.

Acceptance:

- [x] CLI analyzes `SimpleProject`.
- [x] CLI handles a directory without `.sln`.
- [x] CLI handles multiple `.csproj` files.
- [x] Output validates against schemas.
- [x] Generated files are excluded by default.

## 5. Iteration 2 - Graph and Chunks

- [x] Add graph node model.
- [x] Add graph edge model.
- [x] Emit file nodes.
- [x] Emit type nodes.
- [x] Emit member nodes.
- [x] Emit `declares` edges.
- [x] Emit `contains` edges.
- [x] Add Roslyn semantic loading.
- [x] Emit semantic target ids when available.
- [x] Emit `inherits` edges.
- [x] Emit `implements` edges.
- [x] Emit `uses_type` edges.
- [x] Emit best-effort `calls` edges.
- [x] Mark graph edge confidence.
- [x] Emit `graph.json`.
- [x] Emit `chunks.ndjson`.
- [x] Support `--include-chunk-text`.

Acceptance:

- [x] Partial types produce one logical type node with multiple declarations.
- [x] Related targets can be resolved from a member target.
- [x] Syntax fallback still works if semantic load fails.
- [x] `graph.json` validates against schema.
- [x] `chunks.ndjson` validates against schema.

## 6. Iteration 3 - Complexity and Ranking

- [x] Implement shared `ControlFlowFacts`.
- [x] Implement `cyclomatic_complexity@1.0.0`.
- [x] Test all defined cyclomatic decision points.
- [x] Implement cognitive complexity according to ADR.
- [x] Implement `nesting_depth`.
- [x] Add metric formula docs next to implementation.
- [x] Add `hotspot_rank` for members.
- [x] Add `hotspot_rank` for types.
- [x] Add `hotspot_rank` for files.
- [x] Add component reasons to `summary.json`.
- [x] Add deterministic ordering for ties.

Acceptance:

- [x] Top-N hotspots are stable on golden samples.
- [x] Hotspot reasons include metric values, percentiles and weights.
- [x] LOC does not dominate ranking by accident.
- [x] Complexity metrics do not re-traverse AST independently when facts already exist.

## 7. Iteration 4 - Diagnostics and Hardening

- [x] Count compiler diagnostics.
- [x] Count nullable diagnostics.
- [x] Count analyzer diagnostics if available.
- [x] Emit project load diagnostics.
- [x] Emit semantic model unavailable diagnostics.
- [x] Add `codemetrics validate-output <artifact-dir>`.
- [x] Normalize snapshots for paths, timestamps, durations and ordering.
- [x] Add medium-repo performance smoke test.
- [x] Add cancellation handling in long loops.
- [x] Add thread-safety notes to metric authoring docs.

Acceptance:

- [x] Broken project does not crash full analysis.
- [x] Critical diagnostics are visible in `diagnostics.ndjson`.
- [x] Output validation catches missing required artifacts.
- [x] Performance baseline is recorded.

## 8. Iteration 5 - MVP Completion

- [x] Emit `diagnostic_count@1.0.0` metrics for source-linked file/type/member targets.
- [x] Support `tags` on metric results for diagnostic tag counts.
- [x] Emit `outgoing_type_dependency_count@1.0.0`.
- [x] Emit `dependency_cycle_count@1.0.0`.
- [x] Emit `incoming_type_dependency_count@1.0.0` as an additional blast-radius signal.
- [x] Emit `type_count@1.0.0` and `member_count@1.0.0`.
- [x] Emit file/type member complexity aggregate metrics.
- [x] Update `hotspot_rank@1.0.0` to use MVP weights with diagnostics and dependency signals.
- [x] Add `codemetrics list-metrics`.
- [x] Add `codemetrics explain <metric-id>`.
- [x] Accept MVP analyze options `--include`, `--exclude`, `--semantic`, `--no-restore`, and `--max-degree-of-parallelism`.
- [x] Document limitations for partial semantic loading, restore, parallelism and source-less diagnostics.

Acceptance:

- [x] Required MVP metric ids are visible in `metrics.ndjson`.
- [x] Diagnostic tags are visible on metric rows when applicable.
- [x] CLI catalog commands expose formula and target-kind metadata.
- [x] Output still validates against schemas.
- [x] `dotnet test CodeMetricsToolkit.sln` passes.

## 8.1. Production Repo Hardening - MSBuild and Trust

- [x] Replace lightweight semantic compilations with `MSBuildWorkspace`.
- [x] Run `dotnet restore` by default before semantic loading.
- [x] Keep `--no-restore` as an explicit skip.
- [x] Add solution discovery so single-solution repos load through the solution file.
- [x] Reuse workspace syntax trees so semantic models and target ids come from the loaded compilation.
- [x] Add `summary.json.analysisHealth`.
- [x] Track `analysisQuality`, `semanticModel`, `restoreStatus`, `buildStatus`, `trustedDiagnostics`, and `diagnosticsIncludedInHotspotRank`.
- [x] Detect ambient parent MSBuild/NuGet files outside the analyzed root.
- [x] Add `--isolate-input` to copy the target root to a temporary directory before restore/MSBuild loading.
- [x] Suppress compiler diagnostics when restore fails.
- [x] Skip `diagnostic_count` metric projection when diagnostics are untrusted.
- [x] Exclude diagnostic components from `hotspot_rank` when diagnostics are untrusted.
- [x] Keep broken-project and invalid-project runs non-crashing.
- [x] Validate a real `payment-terminal.app.api` December 2025 snapshot in isolated and contaminated locations.

Acceptance:

- [x] `dotnet test CodeMetricsToolkit.sln` passes.
- [x] Isolated `payment-terminal.app.api` December 2025 snapshot reports trusted diagnostics.
- [x] Contaminated in-repo snapshot reports degraded quality and excludes diagnostics from hotspot ranking.
- [x] `--isolate-input` can recover a contaminated in-repo snapshot by analyzing a temporary copy.
- [x] Output still validates against schemas.

## 9. Files To Create First

- [x] `global.json`
- [x] `Directory.Build.props`
- [x] `.editorconfig`
- [x] `CodeMetricsToolkit.sln`
- [x] `schemas/manifest.schema.json`
- [x] `schemas/summary.schema.json`
- [x] `schemas/metric-result.schema.json`
- [x] `schemas/graph.schema.json`
- [x] `schemas/chunk.schema.json`
- [x] `schemas/diagnostic.schema.json`
- [x] `docs/adr/0001-stable-target-ids.md`
- [x] `docs/adr/0002-cyclomatic-complexity-v1.md`
- [x] `docs/adr/0003-cognitive-complexity.md`

## 10. Commands To Keep Green

These commands should become the default verification loop once the solution exists:

```bash
dotnet build
dotnet test
dotnet run --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/SimpleProject --output artifacts/simple
dotnet run --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/simple
```

## 11. Scope Guard

If a task is not needed for one of these outputs, it belongs in `full_code_metrics_toolkit_roadmap.md`, not in MVP:

- [ ] `manifest.json`
- [ ] `summary.json`
- [ ] `metrics.ndjson`
- [ ] `graph.json`
- [ ] `chunks.ndjson`
- [ ] `diagnostics.ndjson`

Deferred by default:

- [ ] CSV
- [ ] Markdown
- [ ] SARIF
- [ ] Quality gates
- [ ] Baseline compare command
- [ ] Halstead
- [ ] Maintainability Index
- [ ] LCOM
- [ ] WMC
- [ ] NPath
- [ ] Package metrics
- [ ] Layer violations
- [ ] Token-based duplication

The unchecked boxes above are intentionally unchecked: they are not MVP work.
