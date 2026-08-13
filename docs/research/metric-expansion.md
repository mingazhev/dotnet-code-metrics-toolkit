# Metric expansion research

Status: P0-P2 core slice implemented, 2026-08-13.

This document evaluates metrics that can extend the toolkit without turning the
collector into a quality policy engine. The recommendation is deliberately narrower
than the universe of metrics that can be computed: a metric belongs in the collector
only when its definition is deterministic, versionable, explainable, and useful as a
raw observation outside one optimization strategy.

## Executive decision

The next implementation should start with three slices:

1. A complete line-accounting family plus project and solution aggregates.
2. Metrics derived from graph edges the toolkit already collects but does not measure.
3. A semantic/CFG slice built on Roslyn symbols, `IOperation`, and
   `ControlFlowGraph`.

Total LOC is useful and should be emitted for projects and solutions. It is an inventory
metric, not a quality score. A reduction in total LOC can mean simplification, deleted
functionality, generated-code filtering, or merely formatting changes. Optimization
loops should therefore use it as context or a guarded secondary objective.

Ratios are useful only when their numerator and denominator are also emitted. A ratio
without its components hides scale: 50% comments can mean one comment line in a two-line
file or 5,000 comment lines in a mature library.

## Current baseline

The baseline catalog contained 19 metrics over files, types, and members. The graph had
project, file, type, and member nodes, with `contains`, `declares`, `inherits`,
`implements`, `uses_type`, and `calls` edges. Only type dependency edges currently
produce graph metrics; call edges are collected but not measured.

A syntax-only self-analysis of commit `8e81221` produced:

| Observation | Value |
| --- | ---: |
| C# files | 85 |
| Types | 146 |
| Members | 710 |
| File `lines_of_code` total | 11,802 |
| File `non_comment_lines_of_code` total | 9,825 |
| Token-bearing line ratio | 83.25% |

The total is already derivable by summing file observations, but making every consumer
repeat that aggregation is a contract gap. Type or member LOC must not be summed to
obtain a repository total: nested declarations overlap, and partial declarations have
different aggregation semantics.

## 1. Line and size metrics

### Recommended line partition

Keep `lines_of_code@1.0.0` for compatibility, but document it as physical source lines.
Keep `non_comment_lines_of_code@1.0.0` as the existing count of distinct lines containing
C# syntax tokens. Do not silently redefine either metric.

Add the following raw file metrics:

| Proposed metric | Definition | Why it matters |
| --- | --- | --- |
| `blank_line_count` | Lines containing only whitespace | Separates formatting from source content |
| `comment_only_line_count` | Lines with comment trivia and no syntax token | Measures standalone commentary without conflating inline comments |
| `commented_line_count` | Lines containing any comment trivia | Numerator for an explicit comment-bearing ratio |
| `mixed_code_comment_line_count` | Lines containing both a syntax token and comment trivia | Explains overlap between code and comments |
| `documentation_comment_line_count` | Lines containing XML documentation trivia | Separates API documentation from ordinary comments |

The categories are intentionally not all mutually exclusive:
`mixed_code_comment_line_count` is included in both token-bearing and commented lines.
The contract must state this instead of pretending that “code + comments + blanks =
LOC” for every formatting style.

Add project and solution targets for these line metrics and for the existing two LOC
metrics. Project and solution totals must be computed from canonical unique physical
files, not by summing types. Solution aggregation must deduplicate linked source files
that appear in multiple projects. Generated files remain governed by the existing
`--include-generated` input option and that choice must stay visible in the manifest.

### Recommended ratios

Ratios should use floating-point `numericValue`, unit `ratio`, and be omitted when the
denominator is zero rather than inventing a value.

| Proposed metric | Formula |
| --- | --- |
| `token_line_ratio` | `non_comment_lines_of_code / lines_of_code` |
| `blank_line_ratio` | `blank_line_count / lines_of_code` |
| `comment_only_line_ratio` | `comment_only_line_count / lines_of_code` |
| `commented_line_ratio` | `commented_line_count / lines_of_code` |
| `documentation_comment_ratio` | `documentation_comment_line_count / lines_of_code` |

These ratios are descriptive. They should not be built into `hotspot_rank`: comments and
blank lines are not monotonically good or bad.

### Other cheap size observations

The existing facts can support project/solution totals and distributions for file,
type, and member counts. Add p50/p95/max distributions where the population is useful,
especially file LOC and member length. Prefer distributions over averages: a mean hides
the small number of very large files or methods that usually drives review effort.

`decision_point_count` is already collected inside `ControlFlowFacts` and should be
published. It is a better numerator than cyclomatic complexity for a density metric,
because summing cyclomatic complexity also adds a baseline of one per member.
If a density is needed, define:

```text
decision_density = sum(decision_point_count) / non_comment_lines_of_code
```

Emit both components. Treat the ratio as navigation context, not a target to minimize.

## 2. Metrics available from the existing graph

The current graph already contains enough information for useful raw measurements:

| Proposed metric | Target | Definition | Cost |
| --- | --- | --- | --- |
| `outgoing_call_count` | member/type | Distinct internal callees | Low |
| `incoming_call_count` | member/type | Distinct internal callers | Low |
| `recursive_component_size` | member | Size of the call-graph SCC, or 0 outside a cycle | Low |
| `dependency_component_size` | type | Size of the type-dependency SCC, or 0 outside a cycle | Low |
| `transitive_type_dependency_count` | type | Distinct reachable internal type dependencies | Medium |
| `transitive_type_dependent_count` | type | Distinct internal types that can reach the target | Medium |

`dependency_cycle_count` currently returns only 0 or 1, despite its name. Preserve it
for compatibility, then add component size and a stable component identifier. Counting
all simple cycles is not recommended: the number can grow exponentially and is rarely
the question a maintainer needs answered.

Call metrics must state that the current edge collector covers explicit invocations and
object construction resolved to internal members. Delegates, reflection, dynamic calls,
framework callbacks, dependency injection, serialization, and generated dispatch are
not a complete static call graph.

### Extend the graph model

Add `solution` and `namespace` nodes before adding architecture metrics. Then collect:

| Proposed metric | Target | Source |
| --- | --- | --- |
| `direct_project_dependency_count` | project | `ProjectDependencyGraph` |
| `transitive_project_dependency_count` | project | `ProjectDependencyGraph` |
| `incoming_project_dependency_count` | project | Reverse project dependencies |
| `namespace_outgoing_dependency_count` | namespace | Aggregated semantic type edges |
| `namespace_incoming_dependency_count` | namespace | Aggregated semantic type edges |
| `namespace_dependency_component_size` | namespace | SCC over namespace dependencies |
| `instability` | project/namespace | `Ce / (Ca + Ce)`, with raw `Ce` and `Ca` also emitted |

Architecture layer violations require a user-supplied rule set. They belong in an
optional policy/profile package, not in the policy-free collector.

PageRank and betweenness centrality are not recommended for the first graph release.
They are population-relative, sensitive to missing semantic edges, harder to explain,
and more expensive. They can later exist as optional derived navigation signals, with
algorithm parameters and graph population encoded in the metric version.

## 3. Roslyn symbol metrics

These metrics require a trusted semantic load. Syntax fallback must not emit them as if
they were complete.

| Proposed metric | Target | Definition |
| --- | --- | --- |
| `inheritance_depth` | type | Longest base-type path to the root |
| `derived_type_count` | type | Direct internal child types |
| `class_coupling` | type | Distinct types used across a documented set of semantic usage sites |
| `weighted_member_complexity` | type | Sum of member cyclomatic complexity |
| `public_api_count` | project/type | Publicly accessible declared types and members |
| `documented_public_api_count` | project/type | Public API symbols with XML documentation |
| `public_api_documentation_ratio` | project/type | Documented public API count / public API count |
| `override_count` | type | Members overriding base members |
| `interface_implementation_count` | type | Implemented interface members |
| `generic_arity` | type/member | Number of declared type parameters |
| `overload_count` | type/member-name group | Members with the same source name |

The current `outgoing_type_dependency_count` is not class coupling in the Microsoft
sense: it counts only internal types reachable through a limited set of graph edges.
`class_coupling` needs a separate metric ID and an explicit usage table covering
parameters, locals, return types, calls, generic instantiations, base types, interfaces,
fields, and attributes. External/framework types must be handled deliberately rather
than silently discarded.

Reference counts and unused-symbol candidates can be built with
`SymbolFinder.FindReferencesAsync`, but they are expensive solution-wide operations.
They also have correctness limits around reflection, dependency injection, markup,
serialization, native entry points, and external consumers of public APIs. Ship these as
opt-in observations with confidence metadata, not as “dead code” facts.

## 4. `IOperation` and control-flow graph metrics

Roslyn exposes language-normalized operations and a `ControlFlowGraph` made of entry,
intermediate, and exit basic blocks connected by explicit branches. This enables more
semantic metrics than walking C# syntax nodes alone.

Recommended first CFG/operation metrics:

| Proposed metric | Target | Definition |
| --- | --- | --- |
| `operation_count` | member | Executable `IOperation` nodes under the member body |
| `basic_block_count` | member | CFG blocks, with entry/exit treatment stated explicitly |
| `reachable_basic_block_count` | member | CFG blocks where `IsReachable` is true |
| `unreachable_basic_block_count` | member | CFG blocks where `IsReachable` is false |
| `control_flow_edge_count` | member | Non-null conditional and fall-through successors |
| `cfg_cyclomatic_complexity` | member | `E - N + 2P`, with the exact CFG normalization versioned |
| `return_count` | member | Return operations |
| `throw_count` | member | Throw operations |
| `exception_handler_count` | member | Catch/finally handlers |
| `await_count` | member | Await operations |
| `allocation_count` | member | Object, array, anonymous-object, and delegate creation operations |
| `invocation_count` | member | Invocation operations, including external calls |

Do not replace the existing syntax cyclomatic metric in place. Publish CFG cyclomatic
complexity under a new ID, compare both on a golden corpus, and only then decide whether
one should become the preferred metric. Different CFG normalization choices around
short-circuit operators, pattern matching, exception edges, and local functions can
change the result.

Executable LOC should be a new metric, not a rename of
`non_comment_lines_of_code`. A defensible implementation counts distinct source lines
mapped from executable operations, while documenting how synthesized, implicit, and
multi-line operations are treated.

Full data-flow metrics such as def-use chains, liveness, and captured-variable pressure
are technically possible but not first-wave product metrics. Their cost and explanation
burden exceed their likely value until concrete consumer questions require them.

## 5. Metrics to defer or keep outside Core

### Maintainability Index and Halstead

Microsoft documents Maintainability Index as a formula over Halstead Volume,
cyclomatic complexity, and LOC. It is familiar, but it compresses several causes into a
single score and inherits every ambiguity in C# operator/operand classification.

If implemented, first publish the primitive Halstead counts with an explicit C# token
classification and golden samples:

```text
n1 = distinct operators     n2 = distinct operands
N1 = total operators        N2 = total operands
vocabulary = n1 + n2        length = N1 + N2
volume = length * log2(vocabulary)
```

Only then add a versioned Maintainability Index. It belongs as a derived compatibility
metric, not as the toolkit's definition of maintainability.

### Cohesion

LCOM-family metrics require a field-to-member usage graph and have several incompatible
formulas. They can be valuable at type scope, but the metric ID must name or document the
exact variant. Defer until field/property symbols and usages are part of the semantic
fact model.

### Duplication

Token-normalized clone detection is valuable, but it is a separate indexing and matching
subsystem rather than a small Roslyn projection. Treat it as a later optional analyzer
slice with clone class, source spans, minimum token length, and normalization policy in
the public contract.

### Git, coverage, runtime, and analyzer findings

- Churn, ownership, and change coupling require Git history and belong in a separate
  enrichment package or downstream join.
- Test coverage is runtime evidence and should be ingested from coverage artifacts, not
  guessed from static source.
- Allocation counts from `IOperation` are static sites, not allocated bytes or runtime
  frequency.
- Third-party `DiagnosticAnalyzer` execution must be opt-in. An analyzer is executable
  code with a different security and determinism boundary from parsing source.

## Delivery plan

The first implementation now ships the P0 line family, existing-graph metrics,
inheritance/coupling/public API coverage, and the first `IOperation`/CFG slice. Reference
search, cohesion, Halstead/MI, and duplication remain deferred.

### P0: line accounting and aggregation

1. Add line classification facts and raw file metrics.
2. Emit existing and new LOC metrics for project and solution targets.
3. Add ratios with zero-denominator behavior and generated-file semantics.
4. Add p50/p95/max project/solution distributions.
5. Add golden fixtures for trailing newlines, multiline comments, inline comments,
   directives, raw strings, disabled code, linked files, partial and nested types.

### P1: harvest the current graph

1. Publish call fan-in/fan-out.
2. Publish dependency and recursion SCC size/identity.
3. Add transitive type dependency counts with scale benchmarks.
4. Add solution/namespace nodes and project dependency edges.

### P2: semantic and CFG slice

1. Publish inheritance depth, child count, weighted member complexity, and public API
   counts.
2. Define and implement class coupling with an explicit usage table.
3. Introduce `IOperation` facts and the first basic-block/operation metrics.
4. Compare syntax and CFG cyclomatic complexity on a golden corpus.

### P3: optional expensive analyzers

1. Reference search and unused-symbol candidates.
2. Cohesion variant.
3. Halstead primitives and Maintainability Index compatibility metric.
4. Token-normalized duplication.

## Acceptance rules for every new metric

A metric is ready for the public catalog only when all of the following are true:

- The target population and aggregation/deduplication rules are explicit.
- The formula, unit, version, trust requirements, and known blind spots are documented.
- Raw components accompany ratios and composite metrics.
- Determinism is tested across operating systems and repeated runs.
- Syntax fallback cannot masquerade as complete semantic analysis.
- Golden fixtures cover C# language constructs that affect the formula.
- Runtime and memory cost are measured on both a small fixture and a production-scale
  repository.
- The metric is localizable back to a target or graph component when practical.
- A metric version changes when semantics change; a documentation edit does not hide a
  formula change.

## Primary references

- Microsoft, [Code metrics values](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-values?view=visualstudio): Maintainability Index, cyclomatic complexity, inheritance depth, class coupling, source LOC, and executable LOC definitions.
- Microsoft, [Maintainability Index range and meaning](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-maintainability-index-range-and-meaning?view=visualstudio): the Visual Studio formula and thresholds.
- Microsoft, [Class coupling](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-class-coupling?view=visualstudio): semantic usage categories counted by the Visual Studio metric.
- Microsoft, [Depth of inheritance](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-depth-of-inheritance?view=visualstudio): DIT definition.
- Roslyn, [`ControlFlowGraph`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.flowanalysis.controlflowgraph?view=roslyn-dotnet-5.3.0): control-flow graph and basic-block API.
- Roslyn, [`ProjectDependencyGraph`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.projectdependencygraph): direct, transitive, reverse, and topological project dependency APIs.
- Roslyn, [`SymbolFinder.FindReferencesAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder.findreferencesasync?view=roslyn-dotnet-5.3.0): solution-wide reference search.
