# Metric Formulas

These formulas are part of the public output contract. Changing a formula
requires a new metric version.

## cyclomatic_complexity@1.0.0

Formula:

```text
cyclomatic_complexity = 1 + decision_points
```

Decision points:

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

Explicit non-decision points:

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

## cognitive_complexity@0.1.0

Sonar-inspired, not Sonar-compatible.

The MVP uses the same shared control-flow facts pass as cyclomatic complexity.
Each structural decision adds one point, plus the current nesting depth. Boolean,
coalesce, conditional, pattern, catch-filter, and switch-when decision points add
one point without an additional nesting multiplier.

The metric stays at `0.1.0` until golden samples cover enough SonarSource rules
to claim compatibility.

## nesting_depth@1.0.0

Formula:

```text
nesting_depth = max active structural decision depth inside the member
```

Structural decisions currently include:

```text
if
for
foreach
while
do
catch
switch statement
switch expression
conditional expression
```

Boolean operators and pattern operators are decision points, but do not increase
nesting depth.

## size and count metrics

### lines_of_code@1.0.0

Formula:

```text
lines_of_code = inclusive source line span count
```

Target kinds: `file`, `type`.

### non_comment_lines_of_code@1.0.0

Formula:

```text
non_comment_lines_of_code = distinct source lines containing C# syntax tokens
```

Target kinds: `file`, `type`.

### method_length@1.0.0

Formula:

```text
method_length = inclusive source line span count for the member declaration
```

Target kind: `member`.

### parameter_count@1.0.0

Formula:

```text
parameter_count = declared parameter count
```

Target kind: `member`.

### type_count@1.0.0

Formula:

```text
type_count = number of type declarations in the file
```

Target kind: `file`.

### member_count@1.0.0

Formula:

```text
member_count = number of member targets contained by the file or type
```

Target kinds: `file`, `type`.

## aggregate member metrics

These metrics project already-computed member facts onto file/type targets.

```text
max_member_cyclomatic_complexity = max(cyclomatic_complexity)
p95_member_cyclomatic_complexity = nearest-rank p95(cyclomatic_complexity)
max_member_cognitive_complexity  = max(cognitive_complexity)
p95_member_cognitive_complexity  = nearest-rank p95(cognitive_complexity)
max_member_nesting_depth         = max(nesting_depth)
```

Target kinds: `file`, `type`.

For targets with no contained members, aggregate values are `0`.

## dependency metrics

### outgoing_type_dependency_count@1.0.0

Formula:

```text
outgoing_type_dependency_count =
  count(distinct internal target types reached by inherits, implements, uses_type)
```

`uses_type` edges are member-to-type edges. For this metric they are attributed
to the containing type of the member.

### incoming_type_dependency_count@1.0.0

Formula:

```text
incoming_type_dependency_count =
  count(distinct internal source types depending on the target type)
```

### dependency_cycle_count@1.0.0

Formula:

```text
dependency_cycle_count = 1 if target type belongs to a type dependency SCC, else 0
```

The MVP reports strongly-connected-component membership, not the exact count of
all simple cycles in the graph.

## diagnostic_count@1.0.0

Formula:

```text
diagnostic_count = count(diagnostics whose source span overlaps the target span)
```

Target kinds: `file`, `type`, `member`.

The untagged metric is the total count. Additional rows with the same metric id
may include `tags`, for example:

```text
compiler
nullable
analyzer
syntax
project_load
```

Project-load diagnostics without a source span remain first-class diagnostics in
`diagnostics.ndjson`, but are not attributed to file/type/member metric targets.

## hotspot_rank@1.0.0

`hotspot_rank` is deterministic and explainable. Each candidate receives a rank
score in `[0, 1]` from weighted percentiles. Higher metric values increase the
percentile. Ties are ordered by target kind and target id.

Member weights:

```text
cognitive_complexity  0.30
cyclomatic_complexity 0.25
nesting_depth         0.15
method_length         0.15
diagnostic_count      0.10
parameter_count       0.05
```

Type weights:

```text
max_member_cognitive_complexity  0.25
p95_member_cyclomatic_complexity 0.20
outgoing_type_dependency_count   0.20
lines_of_code                    0.15
member_count                     0.10
diagnostic_count                 0.10
```

File weights:

```text
p95_member_cognitive_complexity  0.25
p95_member_cyclomatic_complexity 0.20
lines_of_code                    0.20
diagnostic_count                 0.15
type_count                       0.10
member_count                     0.10
```

LOC deliberately has a low weight. It can break ties and add context, but should
not dominate ranking over complexity signals.

## Metric Authoring Notes

Metric authors should keep implementations stateless and thread-safe:

```text
do not store mutable per-run state in static fields
derive metrics from shared facts when possible
accept and honor CancellationToken in long syntax walks
emit diagnostics instead of throwing for recoverable per-target failures
document formula changes with a new metric version
```

If a metric needs a cache, scope it to one analysis run and treat it as private
implementation detail. Public metric output must remain deterministic.
