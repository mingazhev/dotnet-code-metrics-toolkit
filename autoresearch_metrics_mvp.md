# Autoresearch Metrics MVP

Execution checklist: `implementation_checklist.md`.
Development workflow: `development_workflow.md`.

## 1. Назначение

Этот документ заменяет широкий план "toolkit метрик" для первой реализации.

Цель MVP: дать autoresearch стабильный машинный контракт, по которому он может:

1. выбрать участки кода для дальнейшего анализа;
2. собрать контекст вокруг выбранного участка;
3. объяснить, почему участок попал в top-N;
4. сравнить текущий запуск с baseline;
5. продолжить работу при частично сломанной загрузке проекта.

MVP не должен быть клоном NDepend, SonarQube или универсальным каталогом метрик. Его задача уже: подготовить формальные признаки, граф и source ranges, которые полезны LLM-based autoresearch.

## 2. Use Cases Autoresearch

### 2.1. Выбор top-N целей

Autoresearch запускает CLI:

```bash
codemetrics analyze ./repo --output ./artifacts/autoresearch
```

Затем читает `summary.json`, `metrics.ndjson`, `graph.json`, `chunks.ndjson` и получает top-N targets:

```text
member targets с высоким риском
type targets с высокой связностью
files с концентрацией сложных members
dependency cycles
compiler/nullable diagnostic hotspots
```

### 2.2. Сбор контекста для LLM

По `target_id` autoresearch должен уметь получить:

```text
source file
start/end line
parent type
parent namespace
related members
called/used symbols, если доступны
nearby chunks
метрики, которые объясняют выбор
```

### 2.3. Baseline Diff

MVP должен сохранять достаточно стабильные идентификаторы, чтобы последующий инструмент мог сравнить:

```text
new target
removed target
changed metric value
new cycle
new diagnostics
hotspot rank movement
```

Собственная команда `compare` не обязательна для первого MVP, но формат должен быть совместим с future diff.

## 3. Non-goals MVP

В MVP не входит:

- Halstead metrics;
- Maintainability Index;
- LCOM;
- WMC;
- NPath Complexity;
- layer violations;
- package metrics;
- mutation testing;
- coverage parsing;
- runtime profiling;
- SARIF;
- Markdown report;
- web UI;
- IDE plugin;
- точная совместимость с NDepend;
- полный call graph для dynamic dispatch;
- точный semantic analysis для всех edge cases C#.

Эти пункты можно вернуть в extended toolkit roadmap, но нельзя тащить в первый релиз под autoresearch.

## 4. Обязательные Outputs

Команда `analyze` пишет директорию с артефактами:

```text
artifacts/autoresearch/
  summary.json
  metrics.ndjson
  graph.json
  chunks.ndjson
  diagnostics.ndjson
  manifest.json
```

### 4.1. manifest.json

```json
{
  "schemaVersion": "0.1.0",
  "tool": "CodeMetricsToolkit",
  "toolVersion": "0.1.0",
  "rootPath": "/repo",
  "startedAt": "2026-05-06T10:00:00Z",
  "completedAt": "2026-05-06T10:00:12Z",
  "durationMs": 12345,
  "mode": "semantic",
  "artifacts": {
    "summary": "summary.json",
    "metrics": "metrics.ndjson",
    "graph": "graph.json",
    "chunks": "chunks.ndjson",
    "diagnostics": "diagnostics.ndjson"
  }
}
```

`mode`:

```text
syntax
semantic
partial_semantic
```

### 4.2. summary.json

```json
{
  "schemaVersion": "0.1.0",
  "rootPath": "/repo",
  "projectCount": 4,
  "fileCount": 120,
  "typeCount": 640,
  "memberCount": 4210,
  "metricResultCount": 18420,
  "diagnosticCount": 17,
  "hotspots": [
    {
      "targetId": "member:MyApp.Api/M:MyApp.Services.OrderService.PlaceOrderAsync(MyApp.PlaceOrderRequest,System.Threading.CancellationToken)",
      "targetKind": "member",
      "targetName": "OrderService.PlaceOrderAsync",
      "rank": 1,
      "rankScore": 0.94,
      "filePath": "src/MyApp/Services/OrderService.cs",
      "startLine": 42,
      "endLine": 138,
      "reasons": [
        "cyclomatic_complexity p98",
        "cognitive_complexity p97",
        "method_length p95"
      ]
    }
  ]
}
```

### 4.3. metrics.ndjson

Один metric result на строку.

```json
{"schemaVersion":"0.1.0","metricId":"cyclomatic_complexity","metricVersion":"1.0.0","targetId":"member:MyApp/M:MyApp.Foo.Bar(System.String)","targetKind":"member","valueKind":"integer","numericValue":12,"unit":"count","filePath":"src/Foo.cs","startLine":10,"endLine":40}
```

Запрещено использовать произвольный `object value` как публичный контракт. Для стабильного JSON доступны только:

```text
numericValue
stringValue
boolValue
```

Ровно одно из этих полей должно быть заполнено.

### 4.4. graph.json

Граф является обязательным output, а не внутренним индексом.

```json
{
  "schemaVersion": "0.1.0",
  "nodes": [
    {
      "id": "file:src/MyApp/Services/OrderService.cs",
      "kind": "file",
      "name": "OrderService.cs",
      "filePath": "src/MyApp/Services/OrderService.cs"
    },
    {
      "id": "type:MyApp/T:MyApp.Services.OrderService",
      "kind": "type",
      "name": "MyApp.Services.OrderService",
      "filePath": "src/MyApp/Services/OrderService.cs",
      "startLine": 8,
      "endLine": 210
    },
    {
      "id": "member:MyApp/M:MyApp.Services.OrderService.PlaceOrderAsync(MyApp.PlaceOrderRequest,System.Threading.CancellationToken)",
      "kind": "member",
      "name": "OrderService.PlaceOrderAsync",
      "filePath": "src/MyApp/Services/OrderService.cs",
      "startLine": 42,
      "endLine": 138
    }
  ],
  "edges": [
    {
      "from": "file:src/MyApp/Services/OrderService.cs",
      "to": "type:MyApp/T:MyApp.Services.OrderService",
      "kind": "declares",
      "confidence": "exact"
    },
    {
      "from": "type:MyApp/T:MyApp.Services.OrderService",
      "to": "member:MyApp/M:MyApp.Services.OrderService.PlaceOrderAsync(MyApp.PlaceOrderRequest,System.Threading.CancellationToken)",
      "kind": "contains",
      "confidence": "exact"
    }
  ]
}
```

MVP edge kinds:

```text
declares
contains
inherits
implements
uses_type
calls
```

`calls` разрешен как best-effort. Для dynamic dispatch, reflection и unresolved symbols ставить `confidence = "partial"` или не создавать edge.

### 4.5. chunks.ndjson

Chunk нужен autoresearch для сборки LLM-контекста.

```json
{"schemaVersion":"0.1.0","chunkId":"chunk:member:MyApp/M:MyApp.Foo.Bar(System.String)","targetId":"member:MyApp/M:MyApp.Foo.Bar(System.String)","targetKind":"member","chunkKind":"member_body","filePath":"src/Foo.cs","startLine":10,"endLine":40,"tokenEstimate":420,"textHash":"sha256:...","relatedTargetIds":["type:MyApp/T:MyApp.IFoo"]}
```

По умолчанию chunk не обязан содержать полный исходный код. Autoresearch может прочитать file range из репозитория. Опция `--include-chunk-text` может добавить поле `text`, но это не базовый режим.

MVP chunk kinds:

```text
file_header
type_declaration
member_body
constructor_body
property_body
diagnostic_span
```

### 4.6. diagnostics.ndjson

```json
{"schemaVersion":"0.1.0","id":"project_load_failed","severity":"warning","message":"Project could not be loaded with MSBuildWorkspace","projectPath":"src/Legacy/Legacy.csproj"}
```

Diagnostics должны быть first-class output. Метрика не должна валить весь анализ, кроме `OperationCanceledException`.

## 5. Stable Target IDs

Стабильные id важнее красивых display names. Они нужны для baseline diff и повторяемости top-N.

### 5.1. Форматы

```text
solution:<root-hash>
project:<assembly-name>|<repo-relative-csproj-path>
file:<repo-relative-path>
type:<assembly-name>/<xml-doc-id>
member:<assembly-name>/<xml-doc-id>
chunk:<target-id>#<chunk-kind>
```

Для type/member использовать Roslyn `ISymbol.GetDocumentationCommentId()`, если semantic model доступна.

Примеры:

```text
type:MyApp/T:MyApp.Services.OrderService
member:MyApp/M:MyApp.Services.OrderService.PlaceOrderAsync(MyApp.PlaceOrderRequest,System.Threading.CancellationToken)
member:MyApp/P:MyApp.Services.OrderService.Clock
member:MyApp/M:MyApp.Services.OrderService.#ctor(MyApp.IClock)
```

### 5.2. Fallback IDs

Если semantic model недоступна:

```text
type:<project-path-hash>/<namespace>.<type-name>@<file-path>
member:<project-path-hash>/<containing-type>.<member-name>#<parameter-count>@<file-path>:<start-line>
```

Fallback id менее стабилен. В каждом result нужно указать:

```json
{
  "targetIdStability": "semantic"
}
```

Допустимые значения:

```text
semantic
syntax_fallback
line_fallback
```

### 5.3. Partial Types

Partial type имеет один `type` node и несколько declaration spans:

```json
{
  "id": "type:MyApp/T:MyApp.PartialOrder",
  "kind": "type",
  "declarations": [
    {"filePath":"src/A.cs","startLine":10,"endLine":40},
    {"filePath":"src/B.cs","startLine":5,"endLine":30}
  ]
}
```

Метрики, зависящие от source span, должны явно указывать, агрегируют ли они partial declarations.

## 6. MVP Metrics

Метрика готова только если у нее есть:

```text
metric id
metric version
formula documentation
target kinds
required analysis mode
unit tests
edge case tests
snapshot output
known limitations
example result
```

### 6.1. Required Metrics

```text
lines_of_code
non_comment_lines_of_code
method_length
parameter_count
cyclomatic_complexity
cognitive_complexity
nesting_depth
outgoing_type_dependency_count
dependency_cycle_count
diagnostic_count
```

`diagnostic_count` должен поддерживать tags:

```text
compiler
nullable
analyzer
```

### 6.2. Deferred Metrics

Вынести из MVP:

```text
Halstead
Maintainability Index
LCOM
WMC
NPath
layer violations
package metrics
line-based duplication
```

Duplication можно вернуть только как token-based duplication с нормализацией identifiers/literals. Line-based duplication в MVP не делать: он слишком шумный.

## 7. Complexity Specifications

### 7.1. Cyclomatic Complexity

Версия: `cyclomatic_complexity@1.0.0`.

Формула:

```text
complexity = 1 + decision_points
```

Decision points для MVP:

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

Политики:

```text
local function: отдельный member-like target, если есть стабильный source range
lambda: отдельный anonymous target не создавать в MVP, считать внутрь containing member
query syntax: не считать отдельными decision points в v1.0.0
null propagation: не считать decision point в v1.0.0
coalesce assignment: не считать decision point в v1.0.0
```

Любое изменение этих правил требует новой версии метрики.

### 7.2. Cognitive Complexity

По умолчанию взять SonarSource Cognitive Complexity как baseline спецификации. Если будет принято решение делать собственную формулу, оно должно быть записано отдельным ADR с причиной и golden samples.

MVP допускает частичную реализацию только если:

```text
metricVersion = "0.1.0"
description явно говорит "Sonar-inspired, not Sonar-compatible"
known limitations перечислены в explain output
```

## 8. Hotspot Ranking

Не вводить `hotspot_score` как магическое качество кода. В MVP нужен `hotspot_rank`: объяснимый ranking для выбора top-N.

### 8.1. Метод

Для каждого target kind отдельно:

```text
member hotspots считаются среди members
type hotspots считаются среди types
file hotspots считаются среди files
```

Для каждой компонентной метрики вычислить percentile rank:

```text
percentile = rank_desc(value) / target_count
risk_points = 1 - percentile
```

Затем применить явные веса.

### 8.2. Веса MVP

Для member:

```text
cognitive_complexity          0.30
cyclomatic_complexity         0.25
nesting_depth                 0.15
method_length                 0.15
diagnostic_count              0.10
parameter_count               0.05
```

Для type:

```text
max_member_cognitive          0.25
p95_member_cyclomatic         0.20
outgoing_type_dependency_count 0.20
lines_of_code                 0.15
member_count                  0.10
diagnostic_count              0.10
```

Для file:

```text
p95_member_cognitive          0.25
p95_member_cyclomatic         0.20
lines_of_code                 0.20
diagnostic_count              0.15
type_count                    0.10
member_count                  0.10
```

LOC не должен доминировать: его вес ограничен, потому что размер коррелирует со сложностью.

### 8.3. Explain Output

Каждый hotspot обязан содержать reasons:

```json
{
  "targetId": "member:MyApp/M:MyApp.Foo.Bar()",
  "rank": 3,
  "rankScore": 0.87,
  "components": [
    {"metricId":"cognitive_complexity","value":31,"percentile":0.98,"weight":0.30},
    {"metricId":"cyclomatic_complexity","value":17,"percentile":0.95,"weight":0.25}
  ]
}
```

## 9. Architecture

Принцип "один класс - одна метрика" можно оставить только как внешний API. Внутри нельзя делать независимый AST traversal для каждой метрики.

### 9.1. Shared Analysis Passes

```text
DiscoveryPass
WorkspaceLoadPass
SyntaxFactsPass
SemanticFactsPass
DiagnosticFactsPass
GraphBuildPass
MetricProjectionPass
HotspotRankingPass
ReportWritePass
```

### 9.2. Facts Models

```csharp
public sealed record MethodFacts
{
    public required string TargetId { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int LinesOfCode { get; init; }
    public required int NonCommentLinesOfCode { get; init; }
    public required int ParameterCount { get; init; }
    public required int DecisionPointCount { get; init; }
    public required int NestingDepth { get; init; }
}
```

Метрики читают facts и превращают их в `MetricResult`. Они не обязаны сами обходить syntax tree.

### 9.3. Workspace Type

Не фиксировать `AdhocWorkspace` в публичной модели. Использовать базовый `Workspace` или собственную абстракцию:

```csharp
public interface ILoadedWorkspace
{
    IReadOnlyList<Project> Projects { get; }
    IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
    AnalysisMode Mode { get; }
}
```

MSBuildWorkspace и syntax-only fallback должны быть деталями implementation layer.

### 9.4. Thread Safety

Параллелизм разрешен только для immutable facts и read-only Roslyn объектов. Метрика не должна мутировать shared context.

Контракт:

```text
AnalysisContext immutable
Facts immutable
MetricResult new object per metric
no shared mutable caches inside metrics
cancellation token checked in long loops
```

## 10. CLI Contract

MVP commands:

```bash
codemetrics analyze <path>
codemetrics list-metrics
codemetrics explain <metric-id>
codemetrics validate-output <artifact-dir>
```

MVP options:

```bash
--output <dir>
--include <glob>
--exclude <glob>
--syntax-only
--semantic
--no-restore
--include-generated
--max-degree-of-parallelism <n>
--top <n>
--include-chunk-text
```

Exit codes:

```text
0 success
1 fatal execution error
2 output validation failed
3 analysis completed with critical diagnostics
130 canceled
```

## 11. Implementation Plan

### Iteration 0 - Contract First

Duration: 2-3 days.

Deliverables:

```text
JSON schema drafts for manifest/summary/metric/graph/chunk/diagnostic
golden sample repository
snapshot expected outputs
stable target id ADR
complexity formula ADR
```

Done when:

```text
schemas are validated in tests
sample output can be read without project analysis
target id examples cover overloads/generics/partial types
```

### Iteration 1 - Syntax MVP

Duration: 1 week.

Deliverables:

```text
project/file discovery
syntax-only loading
file/type/member detection
stable syntax fallback ids
LOC/NLOC/method_length/parameter_count
metrics.ndjson
summary.json
diagnostics.ndjson
```

Done when:

```text
CLI analyzes a small C# project
output validates against schemas
partial project load does not crash
```

### Iteration 2 - Graph and Chunks

Duration: 1 week.

Deliverables:

```text
graph.json
chunks.ndjson
declares/contains edges
semantic ids when available
inherits/implements/uses_type best-effort edges
```

Done when:

```text
autoresearch can resolve top target to file range and related targets
partial types are represented without duplicate type nodes
```

### Iteration 3 - Complexity and Ranking

Duration: 1 week.

Deliverables:

```text
cyclomatic_complexity
cognitive_complexity v0.1 or Sonar-compatible v1.0
nesting_depth
hotspot_rank
explain output
```

Done when:

```text
top-N hotspots are deterministic on golden samples
ranking reasons are visible in summary.json
complexity formula docs ship with metrics
```

### Iteration 4 - Diagnostics and Hardening

Duration: 1 week.

Deliverables:

```text
compiler diagnostic count
nullable diagnostic count
project load diagnostics
output validation command
snapshot normalization for Roslyn version changes
performance smoke test on medium repo
```

Done when:

```text
tool handles broken MSBuildWorkspace with syntax fallback
all outputs remain schema-compatible
500K LOC performance target is measured, even if not fully optimized
```

## 12. Testing Strategy

### 12.1. Golden Repositories

```text
SimpleProject
PartialTypesProject
GenericsAndOverloadsProject
ComplexityProject
SemanticGraphProject
BrokenProject
NullableDiagnosticsProject
```

### 12.2. Snapshot Policy

Pin Roslyn version for MVP. Snapshot tests must normalize:

```text
absolute paths
timestamps
duration
line endings
diagnostic ordering
node ordering
edge ordering
```

If Roslyn version changes, update snapshots deliberately in one commit.

### 12.3. Required Tests

```text
schema validation tests
target id stability tests
metric formula tests
graph edge tests
chunk range tests
CLI e2e tests
fallback mode tests
hotspot ranking deterministic tests
```

## 13. Definition of Done MVP

MVP готов, когда:

1. `codemetrics analyze ./repo --output ./artifacts/autoresearch` creates all mandatory artifacts.
2. Artifacts validate against schemas.
3. Autoresearch can pick top-N targets from `summary.json`.
4. Every hotspot has source range and explanation.
5. Graph contains file/type/member nodes and required edge kinds.
6. Chunks map target ids to source ranges.
7. Semantic load failure falls back to syntax mode with diagnostics.
8. Metrics have formula docs and versions.
9. Snapshot tests pass on golden repositories.
10. Performance has at least one measured benchmark on a medium repository.

## 14. Hard Decisions

1. Graph is product output, not implementation detail.
2. Ranking is for prioritization, not proof of code quality.
3. Metrics without autoresearch use case stay out of MVP.
4. Formula documentation is part of metric implementation, not a later documentation phase.
5. Stable ids and schemas are built before metric catalog expansion.
6. Shared analysis passes are required before adding overlapping AST-based metrics.
