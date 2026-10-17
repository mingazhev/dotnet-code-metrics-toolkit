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

### token_line_count@1.0.0

Formula:

```text
token_line_count = distinct source lines containing C# syntax tokens
```

Target kinds: `solution`, `project`, `file`, `type`.

### duplicated_block_count@1.0.0, duplicated_line_count@1.0.0, and duplicated_line_ratio@1.0.0

The duplication pass uses exact syntax-token identity: token kind and source text must
match. It ignores trivia, including whitespace and comments, while preserving identifier
spelling and literal text. It does not claim semantic or rename-equivalent clone matching.

A qualifying occurrence is a window of 100 consecutive nonmissing C# tokens whose
inclusive physical line span is at least 10 lines. Every counted window has a matching
occurrence in another file or a same-file occurrence at least 100 tokens away. Overlapping
qualifying token windows are merged within each file. Therefore
`duplicated_block_count` counts merged regions, not clone pairs or clone groups.
`duplicated_line_count` counts the union of physical lines intersecting those regions, so
each line contributes at most once per file. `duplicated_line_ratio` is
`duplicated_line_count / lines_of_code` and is omitted when the denominator is zero.

Solution totals use unique physical file paths. Project totals follow project-file
membership, so a linked physical file can contribute to each project that includes it
while contributing once to the solution. Every file region also emits a tagged
`duplicated_block_count=1` metric row. Its `startLine` and `endLine` identify the region;
the deterministic `duplication_block:NNNNNN` tag distinguishes it from the untagged file
aggregate and from other regions. Project and solution rows remain untagged aggregates.
If a linked file is parsed differently by project-specific preprocessor symbols, the
duplication result uses the first analyzed context, matching the existing per-file facts;
it does not report separate conditional variants for each project. The line-ratio
denominator uses that same canonical file's `lines_of_code` value.

Analysis with duplication is bounded to 500,000 nonmissing tokens across unique physical
source files, 500,000 candidate windows, and 20,000,000 work units. Exceeding a bound
fails analysis with a clear error instead of emitting truncated or zero-valued results.

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

`commented_line_count` deliberately overlaps `token_line_count` on mixed lines.
An empty-looking line inside a multiline comment is comment-only, not blank. All five
raw metrics target `solution`, `project`, and `file`.

The corresponding ratios are:

```text
token_line_ratio = token_line_count / lines_of_code
blank_line_ratio = blank_line_count / lines_of_code
comment_only_line_ratio = comment_only_line_count / lines_of_code
commented_line_ratio = commented_line_count / lines_of_code
documentation_comment_ratio = documentation_comment_line_count / lines_of_code
```

Ratios use unit `ratio` and are omitted for a zero-line population. Numerators and the
denominator are always emitted alongside a ratio.

### member_length@1.0.0

Formula:

```text
member_length = inclusive source line span count for the member declaration
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

Each partial declaration counts in its source file, including multiple declarations
of the same logical type in one file. The graph still contains one node per logical type.

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

These metrics project already-computed member facts onto file, type, project, and
solution targets. The project population is selected by each member's project key;
the solution population counts each semantic member target once. File populations use
the member's primary source file, so a partial member is counted once.

```text
max_member_length                 = max(member_length)
p95_member_length                 = nearest-rank p95(member_length)
max_member_cyclomatic_complexity = max(cyclomatic_complexity)
p95_member_cyclomatic_complexity = nearest-rank p95(cyclomatic_complexity)
max_member_cognitive_complexity  = max(cognitive_complexity)
p95_member_cognitive_complexity  = nearest-rank p95(cognitive_complexity)
max_member_nesting_depth         = max(nesting_depth)
```

The type-only `weighted_member_complexity` aggregate is:

```text
weighted_member_complexity = sum(cyclomatic_complexity for contained logical members)
```

Project/solution type-size distributions use one `lines_of_code` value per logical
type target:

```text
max_type_lines_of_code = max(lines_of_code)
p95_type_lines_of_code = nearest-rank p95(lines_of_code)
```

For project type populations, each type's project key determines ownership. The
solution population counts each semantic type target once; a partial type contributes
one logical type value, whose existing `lines_of_code` metric includes its declarations.

For targets with no contained members or types, aggregate values are `0`.

## dependency metrics

### outgoing_type_dependency_count@1.1.0

Formula:

```text
outgoing_type_dependency_count =
  count(distinct internal target types reached by inherits, implements, uses_type)
```

`uses_type` edges come from both member declarations and type declarations. Member
edges are attributed to the containing type; type-declaration edges are attributed to
their source type. Type-declaration usage includes referenced field and signature types,
generic arguments, attributes, and resolved static member use.
These six existing type-dependency metrics use version `1.1.0` because their source
population now includes type-declaration edges; their count and SCC formulas are unchanged.

### incoming_type_dependency_count@1.1.0

Formula:

```text
incoming_type_dependency_count =
  count(distinct internal source types depending on the target type)
```

### dependency_cycle_membership@1.1.0

Formula:

```text
dependency_cycle_membership = 1 if target type belongs to a type dependency SCC, else 0
```

The MVP reports strongly-connected-component membership, not the exact count of
all simple cycles in the graph.

### dependency_component_size@1.1.0

```text
dependency_component_size = size(type dependency SCC) when cyclic, otherwise 0
```

### transitive_type_dependency_count@1.1.0

```text
transitive_type_dependency_count = distinct internal types reachable from target
```

### transitive_type_dependent_count@1.1.0

```text
transitive_type_dependent_count = distinct internal types that can reach target
```

The target itself is excluded, including when it belongs to a cycle.

### project dependency metrics

```text
outgoing_project_dependency_count = distinct analyzed projects directly referenced by the project
incoming_project_dependency_count = distinct analyzed projects that directly reference the project
project_dependency_cycle_membership = 1 when the project belongs to a direct-reference SCC, else 0
project_dependency_component_size = size of that cyclic SCC, or 0 outside a cycle
```

Only declared project references with both endpoints in the successful semantic
workspace are included. Package, assembly, and source-level type references do not add
project edges. Cycle values describe strongly connected components, not a count of
simple cycles.

### namespace dependency metrics

Namespaces are identified within a project, including its global namespace. Their
dependency graph aggregates internal type dependency edges and excludes dependencies
within the same namespace:

```text
outgoing_namespace_dependency_count = distinct other analyzed namespaces reached by internal type edges
incoming_namespace_dependency_count = distinct other analyzed namespaces with incoming internal type edges
namespace_dependency_cycle_membership = 1 when the namespace belongs to a dependency SCC, else 0
namespace_dependency_component_size = size of that cyclic SCC, or 0 outside a cycle
```

Type usage edges include declarations such as fields, signatures, generic arguments,
attributes, and resolved static member use. External types are excluded from the internal
dependency graph. Nested types remain assigned to their lexical namespace; they do not
create additional namespace nodes. Namespace metrics and all eight project/namespace
dependency metrics require a trusted semantic analysis.

Namespace graph nodes use the project path as primary metadata and carry source
declaration spans separately. Consumers selecting namespaces by source file must use
those declarations.

## call graph metrics

```text
distinct_outgoing_callee_count = count(distinct internal member callees)
distinct_incoming_caller_count = count(distinct internal member callers)
recursive_component_size = size(call SCC) when recursive, otherwise 0
```

`distinct_outgoing_callee_count` and `distinct_incoming_caller_count` target members and types. A type value
aggregates distinct member endpoints across its members. A direct self-recursive member
has `recursive_component_size=1`; mutual recursion reports the full SCC size.

The static call graph contains explicit invocations and object construction resolved to
source members. It is not complete for reflection, `dynamic`, delegates, framework
callbacks, dependency injection, generated dispatch, or external members.

All graph metrics are emitted only for a trusted semantic analysis.

## semantic symbol metrics

### inheritance_depth@1.0.0

```text
inheritance_depth = count(non-System.Object base classes)
```

A class directly deriving from `System.Object` has depth 0. Interfaces and value types
also report 0.

### type_coupling@1.0.0

```text
type_coupling = count(distinct coupled named type original definitions)
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

## hotspot_rank@2.0.0

`hotspot_rank` is a built-in, opinionated navigation heuristic. It is deterministic
and explainable, but it is not a policy-free fact or a universal quality objective.
Each candidate receives a rank score in `[0, 1]` from weighted empirical CDF
percentiles calculated over all candidates of the same target kind. Higher metric
values increase the percentile. Ties share a percentile and final ties are ordered
by target kind and target id.

Diagnostic components are included only when
`summary.json.analysisHealth.diagnosticsIncludedInHotspotRank` is `true`.
Components are read from the emitted metric rows. If a component is unavailable,
the ranker excludes it and normalizes the weights of the remaining components.
This includes trusted graph metrics during syntax-only or degraded analysis; an
unavailable dependency metric is not treated as a measured zero.

Member weights:

```text
cognitive_complexity  0.30
cyclomatic_complexity 0.25
nesting_depth         0.15
member_length         0.15
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
