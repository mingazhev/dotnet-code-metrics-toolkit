You are the implementation orchestrator for this repository.

Work autonomously on Iteration 0 - Contract First.

Read these files first:

1. development_workflow.md
2. CURRENT_STATE.md
3. implementation_checklist.md
4. Relevant sections of autoresearch_metrics_mvp.md

Follow the workflow strictly:

- Treat implementation_checklist.md as the execution tracker.
- Treat CURRENT_STATE.md as the context reset anchor.
- Keep full toolkit features deferred.
- Do not use code_metrics_toolkit_plan.md as binding scope.
- Use subagents only for bounded tasks with disjoint write scopes, if subagents are available.
- If subagents are not available in this environment, continue locally.

Primary goal:

Complete as much of Iteration 0 - Contract First as possible without asking for help.

Expected work:

- Add global.json pinned to .NET 8.0.410.
- Add Directory.Build.props.
- Add .editorconfig.
- Create solution skeleton.
- Create Abstractions, Core, Cli and test projects.
- Add schema files for mandatory outputs.
- Add ADRs for stable target ids, cyclomatic complexity and cognitive complexity.
- Add initial schema validation tests if practical.
- Add golden expected output without requiring real analysis if practical.

Verification:

- Run dotnet build when solution exists.
- Run dotnet test when tests exist.
- Record all verification in CURRENT_STATE.md.

Stop conditions:

- Stop when Iteration 0 acceptance is complete, or
- Stop when there is a concrete blocker that cannot be resolved locally.

Before finishing:

- Update implementation_checklist.md checkboxes truthfully.
- Update CURRENT_STATE.md with completed work, next actions, verification and blockers.
- Do not mark unchecked work as done.
- Do not implement deferred full-toolkit features.
