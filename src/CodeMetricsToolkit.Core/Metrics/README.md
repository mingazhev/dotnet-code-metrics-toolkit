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

Target kinds: `solution`, `project`, `file`, `type`.

Project and solution observations aggregate unique file paths. Never sum type LOC to
obtain a repository total: nested types overlap their containing types and partial types
span multiple files.

### non_comment_lines_of_code@1.0.0

Formula:

```text
non_comment_lines_of_code = distinct source lines containing C# syntax tokens
```

Target kinds: `solution`, `project`, `file`, `type`.

### line classification and ratios

The file line pass builds two sets from Roslyn: lines containing syntax tokens and lines
intersecting comment trivia. XML documentation trivia is tracked separately.

```text
blank_line_count = whitespace-only lines outside comment trivia
comment_only_line_count = comment-bearing lines with no syntax token
commented_line_count = all distinct comment-bearing lines
mixed_code_comment_line_count = lines with both a token and comment trivia
documentation_comment_line_count = lines intersecting XML documentation trivia
```

`commented_line_count` deliberately overlaps `non_comment_lines_of_code` on mixed lines.
An empty-looking line inside a multiline comment is comment-only, not blank. All five
raw metrics target `solution`, `project`, and `file`.

The corresponding ratios are:

```text
token_line_ratio = non_comment_lines_of_code / lines_of_code
blank_line_ratio = blank_line_count / lines_of_code
comment_only_line_ratio = comment_only_line_count / lines_of_code
commented_line_ratio = commented_line_count / lines_of_code
documentation_comment_ratio = documentation_comment_line_count / lines_of_code
```

Ratios use unit `ratio` and are omitted for a zero-line population. Numerators and the
denominator are always emitted alongside a ratio.

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

### decision_point_count@1.0.0

```text
decision_point_count = the decision_points component of cyclomatic_complexity
```

This exposes the non-baseline numerator without adding one per member.

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

### dependency_component_size@1.0.0

```text
dependency_component_size = size(type dependency SCC) when cyclic, otherwise 0
```

### transitive dependency metrics

```text
transitive_type_dependency_count = distinct internal types reachable from target
transitive_type_dependent_count = distinct internal types that can reach target
```

The target itself is excluded, including when it belongs to a cycle.

## call graph metrics

```text
outgoing_call_count = count(distinct internal member callees)
incoming_call_count = count(distinct internal member callers)
recursive_component_size = size(call SCC) when recursive, otherwise 0
```

`outgoing_call_count` and `incoming_call_count` target members and types. A type value
aggregates distinct member endpoints across its members. A direct self-recursive member
has `recursive_component_size=1`; mutual recursion reports the full SCC size.

The static call graph contains explicit invocations and object construction resolved to
source members. It is not complete for reflection, `dynamic`, delegates, framework
callbacks, dependency injection, generated dispatch, or external members.

New graph metrics are emitted only for a trusted semantic analysis. The three original
type dependency metrics retain their legacy best-effort behavior for compatibility.

## semantic symbol metrics

### inheritance_depth@1.0.0

```text
inheritance_depth = count(non-System.Object base classes)
```

A class directly deriving from `System.Object` has depth 0. Interfaces and value types
also report 0.

### class_coupling@1.0.0

```text
class_coupling = count(distinct coupled named type original definitions)
```

The population includes bases, interfaces, attributes, member signatures, constraints,
and type syntax in the target's declarations and executable members. Constructed generic
types contribute the generic definition and their named type arguments. Primitive
special types, type parameters, error types, and the target itself are excluded.

This is a toolkit definition, not a compatibility claim with Visual Studio, SonarQube,
or another CBO implementation.

### public API documentation

```text
public_api_count = effectively public/protected explicit source symbols
documented_public_api_count = public API symbols with non-empty XML documentation
public_api_documentation_ratio = documented_public_api_count / public_api_count
```

Target kinds: `type`, `project`. Public members inside an inaccessible containing type
are not public API. Implicit compiler members and property/event accessor methods are
excluded. Nested types are counted on their own type target to avoid project double
counting. The ratio is omitted for a zero-symbol population.

All semantic symbol metrics require `analysisQuality=trusted`.

## operation and control-flow graph metrics

The operation pass uses explicit Roslyn `IOperation` nodes. `operation_count` excludes
implicit operations and method/constructor/block wrapper operations.

```text
allocation_count = explicit object + array + anonymous object + delegate creation sites
await_count = explicit await operations
control_flow_graph_count = Roslyn CFG roots aggregated into the member
basic_block_count = all CFG blocks, including entry and exit
reachable_basic_block_count = blocks where BasicBlock.IsReachable is true
unreachable_basic_block_count = basic_block_count - reachable_basic_block_count
control_flow_edge_count = non-null fall-through + conditional successors
cfg_cyclomatic_complexity = E - N + 2P
```

In the CFG formula, `E` is `control_flow_edge_count`, `N` is
`basic_block_count`, and `P` is `control_flow_graph_count`. The CFG metric is omitted when
Roslyn cannot construct a graph. It does not replace `cyclomatic_complexity@1.0.0`, which
remains syntax-based and independently versioned.

Properties can contribute multiple accessor graphs. Auto-properties with no authored
executable body do not emit operation/CFG observations. Static allocation counts are
source sites, not bytes or runtime frequency. All operation/CFG metrics require a trusted
semantic analysis.

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
syntax
project_load
```

Project-load diagnostics without a source span remain first-class diagnostics in
`diagnostics.ndjson`, but are not attributed to file/type/member metric targets.

`diagnostic_count` is emitted only when `summary.json.analysisHealth.trustedDiagnostics`
is `true`. If restore or MSBuild project loading fails, compiler diagnostics are
not treated as code-quality metrics because they may reflect a broken analysis
environment instead of broken source code.

## hotspot_rank@1.0.0

`hotspot_rank` is a built-in, opinionated navigation heuristic. It is deterministic
and explainable, but it is not a policy-free fact or a universal quality objective.
Each candidate receives a rank score in `[0, 1]` from weighted empirical CDF
percentiles calculated over all candidates of the same target kind. Higher metric
values increase the percentile. Ties share a percentile and final ties are ordered
by target kind and target id.

Diagnostic components are included only when
`summary.json.analysisHealth.diagnosticsIncludedInHotspotRank` is `true`.
Otherwise the ranker re-normalizes the remaining component weights.

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
