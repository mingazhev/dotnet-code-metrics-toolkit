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

## hotspot_rank@1.0.0

`hotspot_rank` is deterministic and explainable. Each candidate receives a rank
score in `[0, 1]` from weighted percentiles. Higher metric values increase the
percentile. Ties are ordered by target kind and target id.

Member weights:

```text
cyclomatic_complexity 0.35
cognitive_complexity  0.35
nesting_depth         0.20
method_length         0.10
```

Type and file weights:

```text
max_member_cyclomatic_complexity 0.35
max_member_cognitive_complexity  0.35
max_member_nesting_depth         0.20
lines_of_code                    0.10
```

LOC deliberately has a low weight. It can break ties and add context, but should
not dominate ranking over complexity signals.
