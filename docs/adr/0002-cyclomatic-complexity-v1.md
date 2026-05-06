# ADR 0002: Cyclomatic Complexity v1.0

## Status

Accepted

## Context

Cyclomatic complexity is useful for autoresearch only if the formula is stable and testable. A vague "count branches" rule would make the first release incompatible with itself as soon as Roslyn edge cases are found.

## Decision

Metric id and version:

```text
cyclomatic_complexity@1.0.0
```

Formula:

```text
complexity = 1 + decision_points
```

Decision points in v1.0.0:

```text
if
else if
for
foreach
while
do
case label
switch expression arm
catch
catch when
switch when
&&
||
??
?:
pattern and
pattern or
is pattern with relational/type pattern
```

Explicit non-decision points in v1.0.0:

```text
query syntax
null propagation
coalesce assignment
simple assignment
throw expression
await
using
lock
```

Local functions are member-like analysis targets when a stable source range can be produced. Lambdas are counted inside the containing member in MVP and do not receive their own target ids.

## Consequences

- Any formula change requires a new metric version.
- Tests must cover every listed decision point before implementation is accepted.
- The implementation should use shared control-flow facts, not a standalone syntax traversal hidden inside the metric.
- Query syntax and null propagation can be revisited later only as `cyclomatic_complexity@1.1.0` or newer.
