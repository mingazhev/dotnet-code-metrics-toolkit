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
- [ ] `summary.json` includes deterministic top-N hotspots.
- [ ] Hotspot reasons are explainable from component metrics.
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

- [ ] Implement shared `ControlFlowFacts`.
- [ ] Implement `cyclomatic_complexity@1.0.0`.
- [ ] Test all defined cyclomatic decision points.
- [ ] Implement cognitive complexity according to ADR.
- [ ] Implement `nesting_depth`.
- [ ] Add metric formula docs next to implementation.
- [ ] Add `hotspot_rank` for members.
- [ ] Add `hotspot_rank` for types.
- [ ] Add `hotspot_rank` for files.
- [ ] Add component reasons to `summary.json`.
- [ ] Add deterministic ordering for ties.

Acceptance:

- [ ] Top-N hotspots are stable on golden samples.
- [ ] Hotspot reasons include metric values, percentiles and weights.
- [ ] LOC does not dominate ranking by accident.
- [ ] Complexity metrics do not re-traverse AST independently when facts already exist.

## 7. Iteration 4 - Diagnostics and Hardening

- [ ] Count compiler diagnostics.
- [ ] Count nullable diagnostics.
- [ ] Count analyzer diagnostics if available.
- [ ] Emit project load diagnostics.
- [ ] Emit semantic model unavailable diagnostics.
- [ ] Add `codemetrics validate-output <artifact-dir>`.
- [ ] Normalize snapshots for paths, timestamps, durations and ordering.
- [ ] Add medium-repo performance smoke test.
- [ ] Add cancellation handling in long loops.
- [ ] Add thread-safety notes to metric authoring docs.

Acceptance:

- [ ] Broken project does not crash full analysis.
- [ ] Critical diagnostics are visible in `diagnostics.ndjson`.
- [ ] Output validation catches missing required artifacts.
- [ ] Performance baseline is recorded.

## 8. Files To Create First

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

## 9. Commands To Keep Green

These commands should become the default verification loop once the solution exists:

```bash
dotnet build
dotnet test
dotnet run --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/SimpleProject --output artifacts/simple
dotnet run --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/simple
```

## 10. Scope Guard

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
