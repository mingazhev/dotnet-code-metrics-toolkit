# Development Workflow

This workflow is optimized for implementing `autoresearch_metrics_mvp.md` with Codex-style agents.

The goal is not to maximize parallelism. The goal is to keep architecture coherent while using subagents only where they reduce elapsed time without fragmenting design decisions.

## 1. Source of Truth

Use these files in this order:

1. `implementation_checklist.md` - execution tracker.
2. `autoresearch_metrics_mvp.md` - product and technical contract for MVP.
3. `full_code_metrics_toolkit_roadmap.md` - deferred scope only.
4. `code_metrics_toolkit_plan.md` - historical broad plan, not binding for MVP.

If these files conflict, prefer the earlier item in the list.

## 2. Operating Model

Use an orchestrator-led workflow.

The main agent is the orchestrator and owns:

- architecture decisions;
- iteration boundaries;
- checklist status;
- shared contracts and schemas;
- integration of subagent work;
- final verification;
- keeping scope out of the full toolkit roadmap.

Subagents are workers, not co-architects. They can implement bounded tasks, review narrow areas, or inspect a specific subsystem. They must not independently redefine schemas, target ids, output contracts, metric formulas, or iteration scope.

## 3. Context Hygiene

This project is contract-heavy. Context cleanup matters more than usual.

At the start of every development session:

1. Read `development_workflow.md`.
2. Read the current section of `implementation_checklist.md`.
3. Read only the relevant sections of `autoresearch_metrics_mvp.md`.
4. Avoid loading the original broad `code_metrics_toolkit_plan.md` unless a deferred metric needs historical context.

At the end of every development session:

1. Update checked boxes in `implementation_checklist.md`.
2. Add or update a short handoff note in `CURRENT_STATE.md`.
3. List commands run and their status.
4. List known blockers or deliberately deferred work.
5. Keep the handoff factual and short.

`CURRENT_STATE.md` should be treated as the context reset anchor. After compaction or a fresh session, the next agent should be able to resume from it without rereading all planning documents.

## 4. Handoff Note Format

Use this exact shape in `CURRENT_STATE.md`:

```markdown
# Current State

## Active Iteration

Iteration N - Name

## Completed

- ...

## In Progress

- ...

## Next Actions

- ...

## Verification

- `command`: passed/failed/not run

## Known Decisions

- ...

## Blockers

- None / ...
```

Do not turn `CURRENT_STATE.md` into a diary. It is a compact resume point.

## 5. When To Use Subagents

Use subagents when all of these are true:

- the task has a clear expected output;
- the task can be done without changing shared architecture decisions;
- the write scope is disjoint from other active work;
- the orchestrator can verify the result quickly;
- the subagent output materially advances the current checklist iteration.

Do not use subagents for:

- deciding schemas;
- deciding target id formats;
- choosing metric formulas;
- changing iteration scope;
- making broad refactors;
- touching the same files as another active worker;
- fixing bugs whose root cause is not yet understood.

Subagents are useful for bounded implementation and verification. They are bad at preserving product intent across many documents unless the orchestrator keeps the constraints tight.

## 6. Recommended Subagent Split By Iteration

### Iteration 0 - Contract First

Orchestrator owns:

- solution structure;
- schema shape;
- ADR decisions;
- package choices;
- final schema validation strategy.

Good subagent tasks:

- Worker A: draft JSON schemas for one artifact family only, for example graph/chunk.
- Worker B: create golden sample C# projects under `tests/CodeMetricsToolkit.TestAssets`.
- Worker C: write schema validation tests after schemas are already decided.

Bad subagent tasks:

- "Design all schemas."
- "Figure out target ids."
- "Set up the whole architecture."

### Iteration 1 - Syntax MVP

Orchestrator owns:

- discovery pipeline;
- public abstractions;
- output writer integration;
- checklist updates.

Good subagent tasks:

- Worker A: implement file/type/member syntax facts in a bounded module.
- Worker B: implement output writers for `manifest.json`, `summary.json`, `metrics.ndjson`, `diagnostics.ndjson`.
- Worker C: add CLI smoke tests using existing contracts.

Keep write scopes separate:

```text
Worker A: src/CodeMetricsToolkit.Core/Facts/*
Worker B: src/CodeMetricsToolkit.Core/Reporting/*
Worker C: tests/CodeMetricsToolkit.Tests/Cli/*
```

### Iteration 2 - Graph and Chunks

Orchestrator owns:

- graph contract;
- target id stability;
- semantic/syntax fallback policy.

Good subagent tasks:

- Worker A: graph model and graph writer.
- Worker B: chunk model and chunk writer.
- Worker C: semantic id tests for overloads/generics/partial types.

Avoid parallel edits to the same Roslyn loading code. That layer is too coupled.

### Iteration 3 - Complexity and Ranking

Orchestrator owns:

- control-flow facts design;
- metric formula ADR compliance;
- hotspot ranking behavior.

Good subagent tasks:

- Worker A: cyclomatic decision point tests.
- Worker B: nesting depth tests and implementation.
- Worker C: hotspot ranking tests once metric outputs exist.

Do not let each metric create its own syntax traversal. Shared `ControlFlowFacts` is mandatory.

### Iteration 4 - Diagnostics and Hardening

Orchestrator owns:

- failure policy;
- validation command behavior;
- performance baseline acceptance.

Good subagent tasks:

- Worker A: broken project test assets.
- Worker B: snapshot normalization.
- Worker C: validation command tests.

## 7. Orchestrator Loop

For each implementation slice:

1. Select the next unchecked checklist item.
2. Read only the relevant contract sections.
3. Decide whether the task is local or delegable.
4. If delegating, assign a narrow write scope and expected verification.
5. Implement or integrate.
6. Run the smallest relevant tests.
7. Update checklist.
8. Update `CURRENT_STATE.md` if the session changed project state.
9. Run broader verification before ending an iteration.

Never spawn workers before the orchestrator knows what must be true at integration time.

## 8. Verification Levels

Use layered verification.

### Slice Verification

Run after a small change:

```bash
dotnet test --filter <relevant tests>
```

### Iteration Verification

Run before marking iteration acceptance complete:

```bash
dotnet build
dotnet test
```

### MVP Smoke Verification

Run after CLI exists:

```bash
dotnet run --project src/CodeMetricsToolkit.Cli -- analyze tests/CodeMetricsToolkit.TestAssets/SimpleProject --output artifacts/simple
dotnet run --project src/CodeMetricsToolkit.Cli -- validate-output artifacts/simple
```

### Contract Verification

Every mandatory output must validate against its schema:

```text
manifest.json
summary.json
metrics.ndjson
graph.json
chunks.ndjson
diagnostics.ndjson
```

## 9. Scope Control Rules

The orchestrator must reject scope expansion during MVP unless it directly supports one of:

- mandatory outputs;
- stable target ids;
- source ranges;
- graph/chunk context;
- diagnostics;
- hotspot ranking;
- schema validation;
- syntax fallback;
- CLI smoke path.

Default answer for these during MVP is "defer":

- CSV;
- Markdown;
- SARIF;
- quality gates;
- baseline compare command;
- Halstead;
- Maintainability Index;
- LCOM;
- WMC;
- NPath;
- package metrics;
- layer violations;
- token-based duplication.

This is not loss of ambition. It is keeping the first product shippable.

## 10. Branching and Commits

This directory is currently not a git repository. Once implementation starts, either:

1. initialize a repo here, or
2. move the plan into the intended implementation repository.

Recommended commit boundaries:

- contract/schema commit;
- solution skeleton commit;
- syntax facts commit;
- output writers commit;
- graph/chunk commit;
- complexity metrics commit;
- ranking commit;
- diagnostics/hardening commit.

Each commit should leave `dotnet build` and relevant tests passing once the solution exists.

## 11. Best Workflow Summary

Use one orchestrator plus occasional bounded workers.

Do not run a swarm. The hard parts of this project are not raw coding volume; they are stable contracts, Roslyn edge cases, and keeping the MVP narrow. Subagents help with fixtures, tests, schema implementation, writers and isolated metrics. They should not own architecture.

Context cleanup should happen through `CURRENT_STATE.md` plus checked boxes in `implementation_checklist.md`. That is more reliable than relying on the chat transcript.
