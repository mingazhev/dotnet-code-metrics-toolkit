# ADR 0003: Cognitive Complexity Baseline

## Status

Accepted

## Context

Autoresearch needs a ranking signal for methods that are hard to reason about. A private "cognitive complexity" formula would be hard to validate and impossible to compare with existing tools.

## Decision

Use SonarSource Cognitive Complexity as the baseline specification for the first full implementation.

If the MVP implements only a partial version, it must use:

```text
cognitive_complexity@0.1.0
```

and the metric explanation must explicitly say:

```text
Sonar-inspired, not Sonar-compatible.
```

A version may be promoted to `cognitive_complexity@1.0.0` only after golden samples cover the supported Sonar rules and known deviations are documented.

## Consequences

- The project avoids inventing a metric where an external specification already exists.
- Golden samples should include nested flow, recursion, boolean expressions, switch expressions, catch filters, and local functions before claiming compatibility.
- Deviations from Sonar are allowed only with a documented reason and a versioned metric id.
