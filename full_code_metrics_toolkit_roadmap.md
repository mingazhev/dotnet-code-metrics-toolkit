# Full Code Metrics Toolkit Roadmap

MVP execution checklist: `implementation_checklist.md`.
Development workflow: `development_workflow.md`.

## 1. Назначение

Этот документ описывает развитие полного toolkit после `Autoresearch Metrics MVP`.

Он намеренно отделен от MVP. Полный toolkit может быть полезен для CI quality gates, отчетов, сравнения релизов и инженерной аналитики, но это не должно блокировать первый autoresearch-сценарий.

Главное правило: новая функциональность попадает сюда, если она не нужна напрямую для:

```text
top-N autoresearch targets
graph/chunk context
stable source-linked metrics
baseline-compatible machine output
```

## 2. Graduation Criteria

Переходить к этому roadmap можно только после того, как MVP выполнен:

1. Есть стабильные schemas для mandatory outputs.
2. `graph.json` и `chunks.ndjson` используются autoresearch end-to-end.
3. Hotspot ranking объясним и детерминирован на golden repositories.
4. Stable target ids покрывают overloads, generics и partial types.
5. Syntax fallback работает на сломанных проектах.
6. Есть измеренный performance baseline.

Если эти пункты не закрыты, расширять каталог метрик рано. Это будет не развитие, а размазывание незаконченной основы.

## 3. Principles

### 3.1. Metrics Are Projectors

Метрики должны проецировать shared facts в results. Они не должны каждая заново обходить AST, если нужные facts уже собраны.

Допустимо:

```text
CyclomaticComplexityMetric reads ControlFlowFacts
NestingDepthMetric reads ControlFlowFacts
NPathComplexityMetric reads ControlFlowFacts
```

Недопустимо:

```text
CyclomaticComplexityMetric traverses method syntax
CognitiveComplexityMetric traverses method syntax again
NPathComplexityMetric traverses method syntax again
```

### 3.2. Formula Before Implementation

Перед реализацией новой метрики требуется:

```text
metric id
metric version
target kind
analysis mode
formula spec
edge cases
known limitations
example input/output
golden tests
```

Если формула не определена, метрика не готова к разработке.

### 3.3. External Compatibility Is a Decision

Для известных метрик нужно явно выбрать:

```text
compatible with known tool/spec
inspired by known tool/spec
custom formula
```

Пример:

```text
cognitive_complexity: Sonar-compatible v1.0.0
maintainability_index: Visual Studio-inspired, custom normalization
halstead_volume: custom C# operator/operand table
```

## 4. Deferred Metric Catalog

### 4.1. Complexity Extensions

Deferred:

```text
npath_complexity
boolean_expression_complexity
try_catch_complexity
switch_arm_count
query_complexity
async_complexity
```

Before implementation:

```text
define overflow/cap policy
define lambda/local function handling
define switch expression handling
define query syntax handling
```

`npath_complexity` must use capped arithmetic and emit diagnostics when capped:

```text
integer_overflow_guard
npath_value_capped
```

### 4.2. OO Metrics

Deferred:

```text
depth_of_inheritance
number_of_children
weighted_methods_per_class
lack_of_cohesion_of_methods
public_api_size
abstractness
instability
```

Required shared facts:

```text
TypeSymbolFacts
InheritanceFacts
MemberAccessFacts
PublicApiFacts
```

LCOM cannot be added until field/member access extraction is reliable. A naive implementation will produce numbers that look formal and mean little.

### 4.3. Maintainability Metrics

Deferred:

```text
halstead_volume
halstead_difficulty
halstead_effort
maintainability_index
technical_debt_proxy
```

Halstead requires a C# operator/operand table before implementation.

The table must decide treatment for:

```text
.
?.
??
??=
=>
await
new
is
as
pattern matching
attributes
generic type arguments
lambda parameters
LINQ query clauses
```

Maintainability Index must state whether it is:

```text
Visual Studio compatible
Visual Studio inspired
custom formula
```

No MI number should be exposed without formula version and known limitations.

### 4.4. Duplication Metrics

Do not add line-based duplication as a serious signal.

Preferred:

```text
token_based_duplication
normalized_token_duplication
duplicated_token_density
duplicated_block_count
```

Normalization policy:

```text
identifier -> ID
literal -> LITERAL
type name optionally preserved
punctuation/operators preserved
comments ignored
whitespace ignored
```

Config:

```yaml
duplication:
  minTokens: 80
  ignoreGeneratedCode: true
  normalizeIdentifiers: true
  normalizeLiterals: true
```

Line-based duplication may exist only as a cheap heuristic marked `experimental`.

### 4.5. Package Metrics

Deferred:

```text
package_reference_count
outdated_package_count
vulnerable_package_count
transitive_package_count
central_package_management_usage
```

Do not implement vulnerability/outdated checks without defining online/offline behavior.

Required decisions:

```text
NuGet API access allowed or not
lock file support
Directory.Packages.props support
packages.config support
transitive dependency source
```

### 4.6. Layer and Architecture Rules

Deferred:

```text
layer_violation_count
forbidden_dependency_count
namespace_rule_violation_count
assembly_rule_violation_count
```

Config must be formal. Do not use ambiguous pattern examples.

Example:

```yaml
architecture:
  layers:
    - id: domain
      assemblyRegex: "^MyApp\\.Domain$"
      namespaceRegex: "^MyApp\\.Domain(\\.|$)"
    - id: application
      assemblyRegex: "^MyApp\\.Application$"
      namespaceRegex: "^MyApp\\.Application(\\.|$)"
  rules:
    - from: domain
      mayDependOn: []
    - from: application
      mayDependOn: [domain]
```

Rules must state whether they match:

```text
assembly name
namespace
project path
package id
```

## 5. Reporting Extensions

MVP outputs stay primary. Additional reporters are derived views.

Deferred:

```text
csv
markdown
sarif
html
console summary
```

### 5.1. CSV

Useful for spreadsheets, not canonical storage.

Must preserve:

```text
metricId
metricVersion
targetId
targetKind
numericValue/stringValue/boolValue
filePath
startLine
endLine
```

### 5.2. Markdown

Markdown is human-readable summary only. It must not become input for autoresearch.

### 5.3. SARIF

SARIF should be added only for threshold violations and diagnostics, not raw metrics.

## 6. Quality Gates

Quality gates belong after stable metrics and baseline diff.

Config:

```yaml
thresholds:
  - metric: cyclomatic_complexity
    version: "1.0.0"
    targetKind: member
    max: 15
    severity: warning

  - metric: cognitive_complexity
    version: "1.0.0"
    targetKind: member
    max: 20
    severity: warning
```

Quality gate output:

```text
threshold_violations.ndjson
quality_gate.json
optional SARIF
```

Exit codes:

```text
0 no violations above failure level
2 gate failed
```

Do not mix metric calculation and gate evaluation. Gates are a separate layer.

## 7. Baseline and Diff

Baseline diff should compare canonical artifacts from MVP:

```text
metrics.ndjson
graph.json
diagnostics.ndjson
summary.json
```

Commands:

```bash
codemetrics compare ./baseline ./current --output ./diff
```

Diff outputs:

```text
metric_changes.ndjson
graph_changes.ndjson
diagnostic_changes.ndjson
hotspot_changes.json
summary.md
```

Comparison key:

```text
targetId
metricId
metricVersion
targetKind
```

Rules:

```text
removed targets are not regressions by default
new high-risk targets are regressions
rank movement is reported separately from value movement
metric version mismatch is not silently compared
```

## 8. Caching and Incremental Analysis

Caching comes after correctness and stable contracts.

Cache key must be per pass and per metric, not one global `options_hash`.

Inputs:

```text
toolVersion
schemaVersion
metricId
metricVersion
project file hash
source file hash
relevant options hash
Roslyn version
TFM/configuration
```

Relevant options examples:

```text
includeGeneratedCode affects LOC and graph
semantic mode affects symbol ids and graph
threshold config does not affect metric facts
report format does not affect analysis facts
```

Cache layers:

```text
discovery cache
syntax facts cache
semantic facts cache
graph cache
metric projection cache
report cache
```

## 9. Performance Roadmap

Measure before optimizing.

Benchmarks:

```text
small: <25K LOC
medium: 100K-500K LOC
large: >500K LOC
```

Track:

```text
total duration
workspace load duration
syntax facts duration
semantic facts duration
graph build duration
metric projection duration
peak memory
diagnostic count
fallback count
```

Performance goals are invalid without a named benchmark repository.

## 10. Documentation

Formula docs are not a final phase. They are required with each metric.

Documentation sets:

```text
metric reference
schema reference
target id reference
graph edge reference
CLI reference
configuration reference
known limitations
```

Generated docs are acceptable only after source docs exist in code.

## 11. Roadmap Phases

### Phase A - Productionize MVP

```text
stabilize schemas
improve target ids
improve graph confidence
add output validation
performance baseline
packaging
```

### Phase B - CI and Diff

```text
baseline compare
threshold evaluator
quality gate
CSV reporter
SARIF for violations
Markdown summary
```

### Phase C - Semantic Depth

```text
better calls edges
inheritance metrics
public API metrics
type dependency cycles
architecture/layer rules
```

### Phase D - Advanced Metric Catalog

```text
token-based duplication
NPath
Halstead
Maintainability Index
LCOM
WMC
package metrics
```

### Phase E - Scale and Distribution

```text
incremental cache
large repo performance
NuGet package
global dotnet tool
CI examples
documentation site
```

## 12. Explicitly Not First

These are not first-release work:

```text
perfect Sonar/NDepend parity
HTML dashboards
IDE plugin
coverage integration
mutation testing
runtime profiling
LLM interpretation of results
auto-refactoring suggestions
```

If one of these becomes urgent, it needs a separate product decision, not silent expansion of the toolkit.

## 13. One-line Summary

The full toolkit is a later product. The first product is an autoresearch data contract with graph, chunks, stable ids, diagnostics, and a small set of ranking metrics.
