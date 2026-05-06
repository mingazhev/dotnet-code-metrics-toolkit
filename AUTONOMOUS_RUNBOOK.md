# Autonomous Runbook

This file explains how to run the next development pass without babysitting the agent.

## 1. Before Running

Current folder:

```bash
cd /Users/mingazhev/Repos/meetups/ods_autoresearch
```

Important: this folder is currently not a git repository. For serious autonomous work, initialize git first or move these files into the intended implementation repo.

Recommended before autonomous mode:

```bash
git init
git add *.md prompts
git commit -m "docs: define autoresearch metrics MVP workflow"
```

If you do not want git here, use `--skip-git-repo-check` in Codex commands.

## 2. Best Default Autonomous Command

Current local Codex config uses:

```text
model = "gpt-5.5"
model_reasoning_effort = "xhigh"
```

The commands below set `xhigh` explicitly so the run does not depend on future config changes.

If you later want a cheaper/faster run for a narrower task, replace it with:

```bash
-c 'model_reasoning_effort="high"'
```

For Iteration 0 architecture work, use `xhigh`.

Use this for Iteration 0:

```bash
mkdir -p artifacts/codex-runs

codex exec \
  --cd /Users/mingazhev/Repos/meetups/ods_autoresearch \
  -c 'model_reasoning_effort="xhigh"' \
  --skip-git-repo-check \
  --sandbox workspace-write \
  --add-dir "$HOME/.nuget" \
  --ask-for-approval never \
  --output-last-message artifacts/codex-runs/iteration0-last-message.md \
  - < prompts/autonomous_iteration0.md \
  > artifacts/codex-runs/iteration0.log 2>&1
```

This runs non-interactively and writes:

```text
artifacts/codex-runs/iteration0.log
artifacts/codex-runs/iteration0-last-message.md
```

Use `workspace-write` first. It is safer than full filesystem access and should be enough for normal project files. `--add-dir "$HOME/.nuget"` allows NuGet cache writes during restore.

## 3. Leave It Running In Background

For a background run:

```bash
mkdir -p artifacts/codex-runs

nohup codex exec \
  --cd /Users/mingazhev/Repos/meetups/ods_autoresearch \
  -c 'model_reasoning_effort="xhigh"' \
  --skip-git-repo-check \
  --sandbox workspace-write \
  --add-dir "$HOME/.nuget" \
  --ask-for-approval never \
  --output-last-message artifacts/codex-runs/iteration0-last-message.md \
  - < prompts/autonomous_iteration0.md \
  > artifacts/codex-runs/iteration0.log 2>&1 &

echo $! > artifacts/codex-runs/iteration0.pid
```

Check progress:

```bash
tail -f artifacts/codex-runs/iteration0.log
```

Check whether it is still running:

```bash
ps -p "$(cat artifacts/codex-runs/iteration0.pid)"
```

Stop it if needed:

```bash
kill "$(cat artifacts/codex-runs/iteration0.pid)"
```

## 4. More Autonomous, More Risk

If `workspace-write` fails because .NET restore or tooling needs broader filesystem access, rerun with:

```bash
codex exec \
  --cd /Users/mingazhev/Repos/meetups/ods_autoresearch \
  -c 'model_reasoning_effort="xhigh"' \
  --skip-git-repo-check \
  --sandbox danger-full-access \
  --ask-for-approval never \
  --output-last-message artifacts/codex-runs/iteration0-last-message.md \
  - < prompts/autonomous_iteration0.md
```

This is more capable and more dangerous. Use it only when you trust the workspace and want unattended execution.

Avoid `--dangerously-bypass-approvals-and-sandbox` unless you are deliberately running inside an external sandbox.

## 5. Interactive Orchestrator Mode

If you want to watch and occasionally steer:

```bash
codex \
  --cd /Users/mingazhev/Repos/meetups/ods_autoresearch \
  -c 'model_reasoning_effort="xhigh"' \
  --sandbox workspace-write \
  --ask-for-approval on-request \
  "$(cat prompts/autonomous_iteration0.md)"
```

Use this when architecture decisions are still moving. Use `codex exec` when the iteration is clear and bounded.

## 6. What To Inspect After A Run

Read in this order:

```bash
sed -n '1,220p' CURRENT_STATE.md
rg -n '\[x\]|\[ \]' implementation_checklist.md
sed -n '1,220p' artifacts/codex-runs/iteration0-last-message.md
tail -120 artifacts/codex-runs/iteration0.log
```

Then run:

```bash
dotnet build
dotnet test
```

If no solution exists yet, `dotnet build` and `dotnet test` are expected to fail or be inapplicable.

## 7. How To Resume

After any run, start the next session with:

```bash
codex exec \
  --cd /Users/mingazhev/Repos/meetups/ods_autoresearch \
  -c 'model_reasoning_effort="xhigh"' \
  --skip-git-repo-check \
  --sandbox workspace-write \
  --add-dir "$HOME/.nuget" \
  --ask-for-approval never \
  --output-last-message artifacts/codex-runs/resume-last-message.md \
  "Read development_workflow.md, CURRENT_STATE.md and implementation_checklist.md. Continue from the next unchecked item. Work autonomously until the current iteration acceptance is complete or a real blocker appears. Update CURRENT_STATE.md and implementation_checklist.md before finishing."
```

## 8. Rules For Leaving It Alone

Only leave autonomous mode running when:

- the task is bounded to one iteration;
- the prompt says what to update before finishing;
- `--ask-for-approval never` is set;
- logs go to `artifacts/codex-runs`;
- `CURRENT_STATE.md` exists;
- the agent has permission to write project files;
- you accept that it may make imperfect choices and you will review the diff.

Do not leave it running to "implement the whole MVP". That is too broad. Run one iteration or one checklist slice at a time.

## 9. Manual Runbook vs `$codex-autoresearch`

Use this manual runbook first for Iteration 0 and early Iteration 1.

Reason: the hard part right now is not improving a numeric metric. The hard part is establishing contracts, schemas, target id policy, solution layout and acceptance tests. `$codex-autoresearch` is strongest when there is a measurable improve/verify loop. At the beginning of this project, that loop does not exist yet.

### Prefer this runbook when:

- the task is architectural;
- schemas or ADRs are being decided;
- the repository skeleton does not exist yet;
- `dotnet build` / `dotnet test` are not meaningful yet;
- the success condition is checklist completion, not a numeric metric;
- scope control matters more than repeated trial-and-error.

### Prefer `$codex-autoresearch` when:

- a working verify command exists;
- progress can be measured mechanically;
- the loop can keep/discard changes based on evidence;
- the task is "reduce failing tests", "reduce validation errors", "improve coverage", "reduce analyzer warnings" or similar;
- the workspace is already in git;
- run artifacts under `autoresearch-results/` are acceptable.

Good future `$codex-autoresearch` goals:

```text
Reduce schema validation test failures to zero.
Reduce CLI e2e test failures to zero.
Increase passing golden-output snapshot tests without changing schemas.
Reduce nullable/analyzer warnings to zero.
Improve performance benchmark duration while keeping all tests green.
```

Bad current `$codex-autoresearch` goals:

```text
Implement the whole MVP.
Design the architecture.
Figure out all schemas.
Make the best code metrics toolkit.
```

Those goals are too broad or not mechanically verifiable.

### Practical Recommendation

Use this sequence:

1. Manual runbook with `codex exec` for Iteration 0.
2. Manual runbook for the first syntax MVP slice.
3. Switch to `$codex-autoresearch` once `dotnet test` and output validation exist.
4. Use `$codex-autoresearch` for narrow improve/verify loops, not for product direction.

`/goal` was not found as a local CLI command or skill in this environment. If it is a UI-level command, use it only for turning a vague intent into a clearer task. Do not use it as the execution engine for this project unless it produces a concrete scope, verify command and stop condition.
