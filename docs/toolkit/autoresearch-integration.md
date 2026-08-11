# Autoresearch Integration

Autoresearch is one consumer of CodeMetricsToolkit; it is not part of the analyzer's
meaning or runtime architecture.

## Boundary

Use the analyzer to produce facts:

```bash
codemetrics analyze . --output .autoresearch-metrics/current
codemetrics validate-output .autoresearch-metrics/current
```

Then let a repository-local verifier read `summary.json` and `metrics.ndjson`, calculate a
versioned objective, run repository-specific guards, and emit the flat numeric JSON expected
by the optimization loop.

```text
CodeMetricsToolkit artifacts
            |
            v
repo-owned policy/profile + tests
            |
            v
flat numeric metrics_json
            |
            v
autoresearch keep/discard decision
```

Do not optimize `hotspot_rank` as a universal score. It is population-relative and can move
when another target changes. Prefer raw, additive debt relative to thresholds selected for
the repository, plus guards such as tests and trusted-analysis checks.

## Required guards

At minimum, a verifier should fail closed when:

- `analysisHealth.trustedDiagnostics` is false but diagnostics affect the objective;
- semantic metrics are required but semantic loading degraded;
- the selected target population is empty or changed unexpectedly;
- tool, artifact, metric, or profile versions drift during a run;
- repository tests fail.

The first payment-terminal experiment proved that the transport works, but its original
Bash verifier contained an absolute path and an inline formula. Treat that as historical
evidence, not a reusable integration contract. A reusable policy adapter belongs in a
separate optional module and must consume an installed/pinned tool package.
