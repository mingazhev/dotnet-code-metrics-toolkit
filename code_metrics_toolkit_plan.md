# План разработки набора инструментов для сбора метрик кода C#

## 1. Цель

Разработать библиотеку/CLI-инструмент, который принимает путь к папке с решением или проектами C# и считает формальные метрики качества кода.

Autoresearch будет отдельным слоем. Этот набор инструментов должен только:

- находить решения, проекты и исходные файлы;
- строить модель кода;
- считать метрики;
- отдавать результаты в машинно-читаемом виде;
- сохранять результаты для последующего анализа;
- поддерживать расширение через новые классы-метрики.

Главный архитектурный принцип: **один класс — одна метрика**.

---

## 2. Границы первого релиза

### 2.1. Что входит

Первый релиз должен поддерживать:

- C#-проекты;
- `.sln`;
- папку с несколькими `.csproj`;
- SDK-style проекты;
- Roslyn-based анализ;
- метрики на уровнях:
  - solution;
  - project;
  - file;
  - namespace;
  - type;
  - member;
- JSON/NDJSON/CSV export;
- CLI-запуск;
- программный API.

### 2.2. Что не входит

На первом этапе не нужно делать:

- полноценный autoresearch;
- LLM-анализ;
- web UI;
- IDE-плагин;
- глубокую поддержку F#/VB;
- точную совместимость с NDepend/SonarQube;
- mutation testing;
- coverage parsing;
- runtime profiling;
- сложную визуализацию.

---

## 3. Общая архитектура

Рекомендуемая структура решения:

```text
CodeMetricsToolkit.sln

src/
  CodeMetricsToolkit.Abstractions/
  CodeMetricsToolkit.Core/
  CodeMetricsToolkit.Roslyn/
  CodeMetricsToolkit.Metrics/
  CodeMetricsToolkit.Reporting/
  CodeMetricsToolkit.Cli/

tests/
  CodeMetricsToolkit.Tests/
  CodeMetricsToolkit.TestAssets/
```

Назначение проектов:

```text
Abstractions  — контракты, модели, enum'ы.
Core          — orchestration, pipeline, registry, cache.
Roslyn        — загрузка решений и построение Roslyn-модели.
Metrics       — классы конкретных метрик.
Reporting     — JSON, CSV, Markdown, SARIF export.
Cli           — command-line interface.
Tests         — unit/integration/snapshot tests.
```

---

## 4. Базовые доменные модели

### 4.1. Metric scope

```csharp
public enum MetricScope
{
    Solution,
    Project,
    File,
    Namespace,
    Type,
    Member
}
```

### 4.2. Metric value type

```csharp
public enum MetricValueKind
{
    Integer,
    Decimal,
    Boolean,
    String,
    Duration,
    Percentage
}
```

### 4.3. Metric result

```csharp
public sealed record MetricResult
{
    public required string MetricId { get; init; }
    public required string MetricName { get; init; }
    public required MetricScope Scope { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public required string ProjectPath { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public required MetricValueKind ValueKind { get; init; }
    public required object Value { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object?> Tags { get; init; } = new();
}
```

### 4.4. Metric descriptor

```csharp
public sealed record MetricDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MetricScope[] SupportedScopes { get; init; }
    public required MetricValueKind ValueKind { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
    public bool RequiresCompilation { get; init; }
    public bool RequiresSemanticModel { get; init; }
}
```

---

## 5. Интерфейс метрики

Нужно сделать единый контракт для всех метрик.

```csharp
public interface ICodeMetric
{
    MetricDescriptor Descriptor { get; }

    ValueTask<IReadOnlyList<MetricResult>> CalculateAsync(
        CodeAnalysisContext context,
        CancellationToken cancellationToken);
}
```

Каждая метрика должна быть отдельным классом:

```text
LinesOfCodeMetric
NonCommentLinesOfCodeMetric
CyclomaticComplexityMetric
CognitiveComplexityMetric
NestingDepthMetric
ClassCouplingMetric
DepthOfInheritanceMetric
DuplicatedLinesMetric
NullableWarningsMetric
```

---

## 6. Analysis context

`CodeAnalysisContext` должен быть read-only объектом, который передаётся всем метрикам.

```csharp
public sealed class CodeAnalysisContext
{
    public required string RootPath { get; init; }
    public required SolutionModel Solution { get; init; }
    public required IReadOnlyList<ProjectModel> Projects { get; init; }
    public required IReadOnlyList<DocumentModel> Documents { get; init; }
    public required RoslynAnalysisModel Roslyn { get; init; }
    public required AnalysisOptions Options { get; init; }
}
```

Внутри `RoslynAnalysisModel`:

```csharp
public sealed class RoslynAnalysisModel
{
    public required AdhocWorkspace Workspace { get; init; }
    public required IReadOnlyList<Project> Projects { get; init; }
    public required IReadOnlyDictionary<DocumentId, SyntaxTree> SyntaxTrees { get; init; }
    public required IReadOnlyDictionary<DocumentId, SemanticModel?> SemanticModels { get; init; }
    public required IReadOnlyDictionary<ProjectId, Compilation?> Compilations { get; init; }
}
```

---

## 7. Конфигурация запуска

### 7.1. Analysis options

```csharp
public sealed record AnalysisOptions
{
    public required string RootPath { get; init; }
    public string[] IncludePatterns { get; init; } = ["**/*.cs"];
    public string[] ExcludePatterns { get; init; } =
    [
        "**/bin/**",
        "**/obj/**",
        "**/.git/**",
        "**/Generated/**",
        "**/*.g.cs",
        "**/*.Designer.cs"
    ];

    public bool LoadSolutionWithMSBuild { get; init; } = true;
    public bool RestoreBeforeAnalysis { get; init; } = false;
    public bool IncludeGeneratedCode { get; init; } = false;
    public bool ContinueOnProjectLoadError { get; init; } = true;
    public bool CalculateSemanticMetrics { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = 0;
}
```

### 7.2. CLI пример

```bash
codemetrics analyze ./src \
  --format json \
  --output ./artifacts/metrics.json \
  --include "**/*.cs" \
  --exclude "**/bin/**" \
  --exclude "**/obj/**"
```

---

## 8. Pipeline анализа

Pipeline должен быть явным и тестируемым.

```text
1. Read options
2. Discover solutions/projects
3. Load workspace
4. Build syntax trees
5. Build semantic models
6. Build symbol index
7. Build dependency graph
8. Run metric calculators
9. Normalize results
10. Export results
```

Рекомендуемый класс:

```csharp
public sealed class CodeMetricsAnalyzer
{
    private readonly IProjectDiscovery _projectDiscovery;
    private readonly IWorkspaceLoader _workspaceLoader;
    private readonly IMetricRegistry _metricRegistry;
    private readonly IMetricRunner _metricRunner;
    private readonly IMetricReporter _reporter;

    public Task<AnalysisReport> AnalyzeAsync(
        AnalysisOptions options,
        CancellationToken cancellationToken);
}
```

---

## 9. Этап 1 — каркас решения

### Цель

Создать минимальную структуру проектов и контрактов.

### Задачи

1. Создать `.sln`.
2. Создать проекты `Abstractions`, `Core`, `Roslyn`, `Metrics`, `Reporting`, `Cli`, `Tests`.
3. Добавить nullable reference types.
4. Включить analyzers.
5. Включить warnings as errors для собственных проектов.
6. Добавить базовые модели:
   - `MetricDescriptor`;
   - `MetricResult`;
   - `MetricScope`;
   - `MetricValueKind`;
   - `AnalysisOptions`;
   - `AnalysisReport`.
7. Добавить интерфейсы:
   - `ICodeMetric`;
   - `IMetricRegistry`;
   - `IMetricRunner`;
   - `IWorkspaceLoader`;
   - `IProjectDiscovery`;
   - `IMetricReporter`.

### Критерии готовности

- Решение собирается.
- Есть пустой CLI.
- Есть unit test на создание `MetricResult`.
- Есть smoke test на регистрацию fake-метрики.

---

## 10. Этап 2 — discovery проектов

### Цель

Научиться принимать путь к папке, `.sln` или `.csproj`.

### Задачи

1. Реализовать `ProjectDiscovery`.
2. Поддержать входные варианты:
   - путь к `.sln`;
   - путь к `.csproj`;
   - путь к папке.
3. Для папки искать:
   - сначала `.sln`;
   - потом `.csproj`;
   - потом `.cs` как fallback.
4. Исключать `bin`, `obj`, `.git`.
5. Сохранять найденные проекты в `SolutionModel`.
6. Сохранять ошибки discovery отдельно от ошибок анализа.

### Рекомендуемые классы

```text
ProjectDiscovery
SolutionFileFinder
ProjectFileFinder
SourceFileFinder
PathFilter
GlobMatcher
```

### Критерии готовности

- Папка с одним `.sln` корректно определяется.
- Папка с несколькими `.csproj` корректно определяется.
- Папка без проектов, но с `.cs`, анализируется в fallback-режиме.
- Исключения `bin/obj` работают.

---

## 11. Этап 3 — загрузка Roslyn workspace

### Цель

Получить syntax trees, semantic models и compilations.

### Задачи

1. Подключить Roslyn packages.
2. Реализовать `RoslynWorkspaceLoader`.
3. Поддержать загрузку через `MSBuildWorkspace`.
4. Реализовать fallback через `AdhocWorkspace`.
5. Собирать diagnostics загрузки.
6. Кэшировать `SyntaxTree`.
7. Кэшировать `SemanticModel`.
8. Кэшировать `Compilation`.
9. Добавить режим syntax-only.

### Рекомендуемые классы

```text
RoslynWorkspaceLoader
MSBuildWorkspaceFactory
AdhocWorkspaceFactory
SyntaxTreeProvider
SemanticModelProvider
CompilationProvider
WorkspaceLoadDiagnosticCollector
```

### Критерии готовности

- Загружается обычный SDK-style `.csproj`.
- Загружается `.sln` с несколькими проектами.
- При ошибке проекта анализ продолжается.
- Syntax-only метрики работают без compilation.
- Semantic метрики пропускаются, если compilation недоступен.

---

## 12. Этап 4 — индекс кода

### Цель

Построить удобные индексы поверх Roslyn.

### Задачи

1. Собрать список документов.
2. Собрать список namespace declarations.
3. Собрать список type declarations.
4. Собрать список member declarations.
5. Связать declarations с symbol'ами.
6. Рассчитать line spans.
7. Построить `SymbolIndex`.
8. Построить `DependencyIndex`.

### Рекомендуемые классы

```text
DocumentIndexBuilder
NamespaceIndexBuilder
TypeIndexBuilder
MemberIndexBuilder
SymbolIndexBuilder
DependencyIndexBuilder
LineSpanCalculator
GeneratedCodeDetector
```

### Модели

```csharp
public sealed record CodeEntity
{
    public required string Id { get; init; }
    public required MetricScope Scope { get; init; }
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public ISymbol? Symbol { get; init; }
    public SyntaxNode? SyntaxNode { get; init; }
}
```

### Критерии готовности

- Все классы находятся.
- Все методы находятся.
- Partial-типы объединяются по symbol id.
- Generated-файлы можно исключить.
- Entity id стабилен между запусками.

---

## 13. Этап 5 — registry и runner метрик

### Цель

Сделать расширяемую систему запуска метрик.

### Задачи

1. Реализовать `MetricRegistry`.
2. Реализовать ручную регистрацию метрик.
3. Реализовать DI-регистрацию.
4. Реализовать фильтрацию по metric id.
5. Реализовать фильтрацию по scope.
6. Реализовать параллельный запуск.
7. Реализовать обработку ошибок одной метрики без падения всего анализа.
8. Добавить timing каждой метрики.
9. Добавить результат `MetricExecutionDiagnostic`.

### Рекомендуемые классы

```text
MetricRegistry
MetricRunner
MetricExecutionContext
MetricExecutionDiagnostic
MetricResultNormalizer
MetricFailurePolicy
```

### Критерии готовности

- Можно запустить одну метрику.
- Можно запустить все метрики.
- Ошибка одной метрики не ломает отчёт.
- В отчёте есть время выполнения каждой метрики.

---

## 14. Этап 6 — базовые size-метрики

### Цель

Реализовать простые syntax-only метрики.

### Метрики

```text
LinesOfCodeMetric
NonCommentLinesOfCodeMetric
BlankLinesMetric
CommentLinesMetric
FileCountMetric
ProjectCountMetric
TypeCountMetric
MemberCountMetric
MethodCountMetric
PropertyCountMetric
FieldCountMetric
ParameterCountMetric
LocalVariableCountMetric
```

### Правила реализации

`LinesOfCodeMetric`:

- уровень: file, type, member, project, solution;
- считать физические строки;
- учитывать line span syntax node;
- project/solution считать агрегацией.

`NonCommentLinesOfCodeMetric`:

- уровень: file, type, member, project, solution;
- исключать пустые строки;
- исключать строки только с комментариями;
- не пытаться идеально учитывать inline-комментарии.

`ParameterCountMetric`:

- уровень: member;
- считать параметры методов, конструкторов, local functions, lambdas;
- индексаторы считать отдельно.

### Критерии готовности

- Метрики работают без semantic model.
- Есть тесты на обычный класс.
- Есть тесты на comments/blank lines.
- Есть тесты на partial class.
- Есть snapshot JSON.

---

## 15. Этап 7 — complexity-метрики

### Цель

Реализовать метрики сложности control flow.

### Метрики

```text
CyclomaticComplexityMetric
CognitiveComplexityMetric
NestingDepthMetric
NPathComplexityMetric
SwitchArmCountMetric
BooleanExpressionComplexityMetric
TryCatchComplexityMetric
```

### 15.1. Cyclomatic Complexity

Начальное правило:

```text
complexity = 1 + decision_points
```

Decision points для C#:

```text
if
else if
for
foreach
while
do
case
switch arm
catch
&&
||
??
?:
pattern and
pattern or
```

Отдельно решить политику для:

```text
null propagation
coalesce assignment
local functions
lambdas
query syntax
```

Рекомендуемый класс:

```csharp
public sealed class CyclomaticComplexityMetric : ICodeMetric
{
    public MetricDescriptor Descriptor { get; }

    public ValueTask<IReadOnlyList<MetricResult>> CalculateAsync(
        CodeAnalysisContext context,
        CancellationToken cancellationToken);
}
```

### 15.2. Cognitive Complexity

Упрощённый первый вариант:

```text
+1 за ветвление
+ nesting level за вложенность
+1 за recursion
+1 за break/continue/goto
```

Полная совместимость с Sonar не нужна в первом релизе. Главное — стабильность собственной формулы.

### 15.3. Nesting Depth

Считать максимальную глубину:

```text
if
for
foreach
while
do
switch
try
catch
finally
using statement
lock
```

### 15.4. NPath Complexity

Первый вариант можно ограничить методами. Для слишком сложных методов вводить cap:

```text
max_npath = 1_000_000
```

### Критерии готовности

- CC считается на методах.
- CC агрегируется на type/project/solution.
- Cognitive Complexity стабилен на тестовых примерах.
- Nesting Depth работает на вложенных if/loops.
- Слишком большой NPath не переполняет integer.

---

## 16. Этап 8 — OO-метрики

### Цель

Реализовать метрики типов и наследования.

### Метрики

```text
DepthOfInheritanceMetric
NumberOfChildrenMetric
WeightedMethodsPerClassMetric
LackOfCohesionOfMethodsMetric
PublicMemberCountMetric
PublicApiSizeMetric
InterfaceImplementationCountMetric
AbstractnessMetric
```

### 16.1. Depth of Inheritance

Нужен semantic model.

Правила:

- считать цепочку `BaseType`;
- `object` можно не учитывать;
- внешние типы учитывать опционально;
- при цикле или ошибке symbol'а возвращать diagnostic.

### 16.2. Number of Children

Нужен индекс всех типов решения.

Правила:

- считать прямых наследников;
- интерфейсы считать отдельно;
- partial-типы не дублировать.

### 16.3. Weighted Methods per Class

Первый вариант:

```text
WMC = sum(CyclomaticComplexity(method))
```

### 16.4. LCOM

Первый вариант:

```text
LCOM = 1 - average(method_field_usage_overlap)
```

Более простой вариант для MVP:

```text
LCOM_HS = non_shared_method_pairs - shared_method_pairs
LCOM_HS = max(LCOM_HS, 0)
```

Где:

```text
shared_method_pairs     — пары методов, использующие хотя бы одно общее поле.
non_shared_method_pairs — пары методов без общих полей.
```

### Критерии готовности

- DIT работает на простой иерархии.
- NOC работает внутри решения.
- WMC использует результат CC.
- LCOM работает на классах с полями.
- Интерфейсы и abstract-типы обрабатываются корректно.

---

## 17. Этап 9 — coupling-метрики

### Цель

Считать зависимости между типами, namespace'ами и проектами.

### Метрики

```text
ClassCouplingMetric
AfferentCouplingMetric
EfferentCouplingMetric
InstabilityMetric
FanInMetric
FanOutMetric
ProjectDependencyCountMetric
NamespaceDependencyCountMetric
DependencyCycleCountMetric
LayerViolationMetric
```

### 17.1. Class Coupling

Считать уникальные внешние типы, используемые классом.

Источники зависимостей:

```text
base type
implemented interfaces
field types
property types
method return types
parameter types
local variable types
object creation
generic arguments
attributes
extension methods
static member access
```

### 17.2. Afferent/Efferent Coupling

На уровне project/namespace/type:

```text
Ca = incoming dependencies
Ce = outgoing dependencies
I  = Ce / (Ca + Ce)
```

Если `Ca + Ce = 0`, instability можно считать `0`.

### 17.3. Dependency cycles

Построить directed graph:

```text
node = project | namespace | type
edge = dependency
```

Алгоритмы:

```text
Tarjan SCC
Kosaraju SCC
```

Для MVP достаточно Tarjan SCC.

### 17.4. Layer violations

Добавить конфиг:

```yaml
layers:
  - name: Domain
    pattern: "*.Domain"
    mayDependOn: []
  - name: Application
    pattern: "*.Application"
    mayDependOn: ["Domain"]
  - name: Infrastructure
    pattern: "*.Infrastructure"
    mayDependOn: ["Application", "Domain"]
  - name: Web
    pattern: "*.Web"
    mayDependOn: ["Application"]
```

Метрика должна возвращать:

```text
count
edges
source
target
rule
```

### Критерии готовности

- Coupling считается на типах.
- Ca/Ce считаются на namespace и project.
- Instability считается из Ca/Ce.
- Циклы находятся.
- Layer rules работают из конфигурации.

---

## 18. Этап 10 — duplication-метрики

### Цель

Найти грубое дублирование без тяжёлого clone detector.

### Метрики

```text
DuplicatedLineCountMetric
DuplicatedLineDensityMetric
DuplicatedBlockCountMetric
DuplicatedTokenBlockCountMetric
DuplicatedFileCountMetric
```

### 18.1. Line-based duplication

Правила:

- нормализовать whitespace;
- игнорировать пустые строки;
- игнорировать строки только с комментариями;
- искать последовательности длиной `N`.

Начальные параметры:

```text
min_block_lines = 6
min_occurrences = 2
```

### 18.2. Token-based duplication

Позже добавить token normalization:

```text
identifier -> ID
string literal -> STR
numeric literal -> NUM
char literal -> CHAR
```

Начальные параметры:

```text
min_block_tokens = 100
```

### Критерии готовности

- Line duplication работает.
- Density считается в процентах.
- Generated-файлы исключаются.
- Token duplication можно включить флагом.
- Есть отчёт по duplicate groups.

---

## 19. Этап 11 — analyzer diagnostics metrics

### Цель

Считать warnings и diagnostics от компилятора/Roslyn analyzers.

### Метрики

```text
CompilerWarningCountMetric
CompilerErrorCountMetric
AnalyzerWarningCountMetric
NullableWarningCountMetric
ObsoleteApiUsageCountMetric
SuppressedWarningCountMetric
DiagnosticDensityMetric
```

### Правила

1. Получить diagnostics из compilation.
2. Отдельно считать:
   - compiler diagnostics;
   - analyzer diagnostics;
   - nullable diagnostics;
   - obsolete diagnostics.
3. Группировать по severity.
4. Группировать по diagnostic id.
5. Считать density на KLOC.

### Nullable warnings

Первый вариант:

```text
CS8600–CS8699
CS8714
CS8762
CS8765
CS8774
```

### Suppressed warnings

Считать:

```text
#pragma warning disable
SuppressMessageAttribute
NoWarn
.editorconfig severity = none
```

Для MVP достаточно `#pragma warning disable`.

### Критерии готовности

- Compiler warnings считаются.
- Nullable warnings выделяются отдельно.
- Density считается.
- Suppressions считаются хотя бы по pragma.
- Diagnostics имеют file/line.

---

## 20. Этап 12 — maintainability metrics

### Цель

Добавить композитные метрики.

### Метрики

```text
HalsteadVolumeMetric
HalsteadDifficultyMetric
HalsteadEffortMetric
MaintainabilityIndexMetric
TechnicalDebtProxyMetric
HotspotScoreMetric
```

### 20.1. Halstead

Считать на member-level.

Нужно выделить:

```text
n1 — distinct operators
n2 — distinct operands
N1 — total operators
N2 — total operands
```

Формулы:

```text
vocabulary = n1 + n2
length     = N1 + N2
volume     = length * log2(vocabulary)
difficulty = (n1 / 2) * (N2 / n2)
effort     = volume * difficulty
```

Защититься от деления на ноль.

### 20.2. Maintainability Index

Первый вариант:

```text
MI = max(0, (171 - 5.2 * ln(HV) - 0.23 * CC - 16.2 * ln(LOC)) * 100 / 171)
```

Где:

```text
HV  — Halstead Volume
CC  — Cyclomatic Complexity
LOC — Lines of Code
```

Если `HV <= 0` или `LOC <= 0`, вернуть diagnostic.

### 20.3. Hotspot Score

Собственная метрика риска:

```text
Hotspot = normalize(CC) +
          normalize(Cognitive) +
          normalize(LOC) +
          normalize(Coupling) +
          normalize(Duplication)
```

Нужна не для quality gate, а для ранжирования мест для autoresearch.

### Критерии готовности

- Halstead считается на методах.
- MI считается на методах.
- MI агрегируется на type/project.
- Hotspot Score сортирует самые рискованные участки.
- Все формулы задокументированы.

---

## 21. Этап 13 — dependency/package metrics

### Цель

Считать метрики зависимостей NuGet и project references.

### Метрики

```text
ProjectReferenceCountMetric
PackageReferenceCountMetric
TransitivePackageCountMetric
PackageVersionSkewMetric
CentralPackageManagementUsageMetric
DeprecatedPackageCountMetric
VulnerablePackageCountMetric
```

### MVP

Сначала сделать только static parsing:

```text
PackageReference
ProjectReference
Directory.Packages.props
packages.lock.json
```

Позже добавить вызовы:

```bash
dotnet list package --outdated
dotnet list package --deprecated
dotnet list package --vulnerable
```

### Критерии готовности

- PackageReference считается.
- ProjectReference считается.
- Directory.Packages.props учитывается.
- Результат есть на project и solution.
- External CLI-вызовы отключаются флагом.

---

## 22. Этап 14 — reporters

### Цель

Сделать экспорт результатов.

### Форматы

```text
JSON
NDJSON
CSV
Markdown
SARIF-lite
```

### JSON структура

```json
{
  "tool": "CodeMetricsToolkit",
  "rootPath": "/repo",
  "startedAt": "2026-05-06T10:00:00Z",
  "durationMs": 12345,
  "summary": {
    "projectCount": 4,
    "documentCount": 120,
    "metricCount": 32
  },
  "metrics": [],
  "diagnostics": []
}
```

### CSV колонки

```text
metric_id
metric_name
scope
target_id
target_name
project_path
file_path
start_line
end_line
value_kind
value
unit
```

### Markdown report

Markdown должен быть вторичным форматом. Его удобно использовать для человека, но autoresearch должен читать JSON/NDJSON.

### Критерии готовности

- JSON экспорт стабилен.
- NDJSON подходит для stream processing.
- CSV открывается в spreadsheet.
- Markdown даёт краткий summary.
- Reporter покрыт snapshot tests.

---

## 23. Этап 15 — CLI

### Цель

Сделать удобный запуск из терминала и CI.

### Команды

```bash
codemetrics analyze <path>
codemetrics list-metrics
codemetrics explain <metric-id>
codemetrics validate-config <path>
```

### Опции analyze

```bash
--output <path>
--format json|ndjson|csv|md
--metric <id>
--scope solution|project|file|namespace|type|member
--include <glob>
--exclude <glob>
--configuration Debug|Release
--no-restore
--syntax-only
--fail-on-threshold
--config <path>
```

### Критерии готовности

- CLI возвращает non-zero exit code при критических ошибках.
- `list-metrics` показывает все метрики.
- `explain` показывает формулу.
- `analyze` пишет файл.
- CLI можно запускать в CI.

---

## 24. Этап 16 — thresholds и quality gate

### Цель

Добавить проверку порогов, но не смешивать её с подсчётом метрик.

### Конфиг

```yaml
thresholds:
  - metric: cyclomatic_complexity
    scope: member
    max: 15
    severity: warning

  - metric: cognitive_complexity
    scope: member
    max: 20
    severity: warning

  - metric: duplicated_line_density
    scope: project
    max: 5
    severity: error
```

### Классы

```text
ThresholdRule
ThresholdEvaluator
ThresholdViolation
QualityGateResult
```

### Критерии готовности

- Thresholds читаются из YAML/JSON.
- Нарушения попадают в report.
- CLI может вернуть exit code 2 при gate failure.
- Metrics и gates остаются разными слоями.

---

## 25. Этап 17 — baseline и diff

### Цель

Поддержать анализ изменений между запусками.

### Возможности

```text
baseline save
baseline compare
new metrics
changed metrics
removed targets
regression detection
```

### Команды

```bash
codemetrics analyze ./src --output current.json
codemetrics compare baseline.json current.json --output diff.md
```

### Сравнивать по

```text
metric_id
target_id
scope
```

### Критерии готовности

- Можно сравнить два JSON-отчёта.
- Рост CC подсвечивается.
- Рост LOC подсвечивается.
- Новые циклы подсвечиваются.
- Удалённые targets не считаются регрессией.

---

## 26. Этап 18 — производительность и кэширование

### Цель

Сделать анализ пригодным для больших решений.

### Задачи

1. Измерять время загрузки workspace.
2. Измерять время каждой метрики.
3. Кэшировать syntax tree.
4. Кэшировать semantic model.
5. Кэшировать symbol index.
6. Кэшировать dependency graph.
7. Добавить parallel execution.
8. Добавить cancellation.
9. Добавить memory budget.
10. Добавить incremental mode по hash файлов.

### Кэш-ключ

```text
tool_version
metric_id
metric_version
file_path
file_hash
project_path
project_hash
options_hash
```

### Критерии готовности

- Повторный запуск быстрее.
- Изменение одного файла инвалидирует только нужные результаты.
- Большое решение не падает по памяти.
- Cancellation работает.

---

## 27. Этап 19 — тестовая стратегия

### 27.1. Unit tests

Покрыть:

```text
metric formulas
syntax visitors
line counting
path filtering
result normalization
threshold evaluator
```

### 27.2. Integration tests

Покрыть:

```text
single project
multi-project solution
project references
partial classes
generated files
nullable warnings
dependency cycles
```

### 27.3. Snapshot tests

Фиксировать:

```text
JSON report
CSV report
Markdown report
metric descriptors
CLI output
```

### 27.4. Golden samples

Создать тестовые проекты:

```text
SimpleProject
ComplexityProject
InheritanceProject
CouplingProject
DuplicationProject
DiagnosticsProject
PackagesProject
```

### Критерии готовности

- Каждая метрика имеет unit tests.
- Каждый reporter имеет snapshot tests.
- Есть end-to-end test CLI.
- Test assets маленькие и стабильные.

---

## 28. Этап 20 — документация для метрик

### Цель

Каждая метрика должна быть объяснима.

Для каждой метрики создать markdown-документ:

```text
docs/metrics/cyclomatic_complexity.md
docs/metrics/cognitive_complexity.md
docs/metrics/lines_of_code.md
```

Шаблон документа:

```md
# Metric Name

## Id

## Scope

## Formula

## Requires semantic model

## Interpretation

## Known limitations

## Examples

## Related metrics
```

Также добавить генератор:

```text
MetricDocumentationGenerator
```

Он должен строить docs из `MetricDescriptor`.

---

## 29. Этап 21 — подготовка к autoresearch

### Цель

Сделать так, чтобы будущий autoresearch мог использовать метрики как входные сигналы.

### Требования

1. Результаты должны быть стабильными.
2. Entity id должен быть стабильным.
3. Формулы должны быть версионированы.
4. Метрики должны иметь explainable output.
5. Hotspots должны быть ранжируемыми.
6. Отчёт должен содержать source location.
7. Метрики должны поддерживать baseline diff.
8. Должна быть возможность получить top-N targets.

### Дополнительные модели

```csharp
public sealed record CodeHotspot
{
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public required MetricScope Scope { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<MetricContribution> Contributions { get; init; }
}
```

```csharp
public sealed record MetricContribution
{
    public required string MetricId { get; init; }
    public required double RawValue { get; init; }
    public required double NormalizedValue { get; init; }
    public required double Weight { get; init; }
}
```

### Критерии готовности

- Можно получить top-100 hotspots.
- Можно объяснить score каждого hotspot.
- Можно сравнить hotspots между baseline и current.
- Autoresearch может читать один JSON без дополнительного анализа проекта.

---

## 30. Рекомендуемый порядок реализации метрик

### MVP batch

```text
ProjectCountMetric
FileCountMetric
LinesOfCodeMetric
NonCommentLinesOfCodeMetric
TypeCountMetric
MemberCountMetric
MethodCountMetric
ParameterCountMetric
CyclomaticComplexityMetric
NestingDepthMetric
```

### Batch 2

```text
CognitiveComplexityMetric
ClassCouplingMetric
DepthOfInheritanceMetric
NumberOfChildrenMetric
WeightedMethodsPerClassMetric
AfferentCouplingMetric
EfferentCouplingMetric
InstabilityMetric
```

### Batch 3

```text
DuplicatedLineCountMetric
DuplicatedLineDensityMetric
DependencyCycleCountMetric
CompilerWarningCountMetric
NullableWarningCountMetric
SuppressedWarningCountMetric
```

### Batch 4

```text
HalsteadVolumeMetric
HalsteadDifficultyMetric
HalsteadEffortMetric
MaintainabilityIndexMetric
LackOfCohesionOfMethodsMetric
PublicApiSizeMetric
PackageReferenceCountMetric
HotspotScoreMetric
```

---

## 31. Приоритеты для autoresearch

Для будущего autoresearch наиболее полезны не все метрики, а те, которые помогают выбрать участок кода.

Сначала сделать эти:

```text
CyclomaticComplexityMetric
CognitiveComplexityMetric
LinesOfCodeMetric
ClassCouplingMetric
DependencyCycleCountMetric
DuplicatedLineDensityMetric
NullableWarningCountMetric
CompilerWarningCountMetric
HotspotScoreMetric
```

Потом добавить:

```text
LCOM
WMC
DIT
Ca/Ce
Instability
Maintainability Index
Halstead
Package metrics
```

---

## 32. Минимальный MVP

MVP должен уметь:

```bash
codemetrics analyze ./SomeSolution --output metrics.json
```

И вернуть:

```text
solution summary
project summary
file metrics
type metrics
member metrics
diagnostics
execution timings
```

Минимальные метрики MVP:

```text
project_count
file_count
type_count
member_count
lines_of_code
non_comment_lines_of_code
method_length
parameter_count
cyclomatic_complexity
nesting_depth
```

---

## 33. Definition of Done для каждой метрики

Каждая новая метрика считается готовой, если есть:

1. отдельный класс;
2. `MetricDescriptor`;
3. formula documentation;
4. unit tests;
5. edge case tests;
6. snapshot result;
7. known limitations;
8. stable metric id;
9. metric version;
10. пример результата.

Шаблон класса:

```csharp
public sealed class SomeMetric : ICodeMetric
{
    public MetricDescriptor Descriptor { get; } = new()
    {
        Id = "some_metric",
        Name = "Some Metric",
        SupportedScopes = [MetricScope.Member],
        ValueKind = MetricValueKind.Integer,
        Unit = "count",
        Description = "..."
    };

    public async ValueTask<IReadOnlyList<MetricResult>> CalculateAsync(
        CodeAnalysisContext context,
        CancellationToken cancellationToken)
    {
        // implementation
    }
}
```

---

## 34. Нейминг metric id

Использовать стабильный snake_case.

Примеры:

```text
lines_of_code
non_comment_lines_of_code
blank_lines
comment_lines
cyclomatic_complexity
cognitive_complexity
nesting_depth
npath_complexity
class_coupling
afferent_coupling
efferent_coupling
instability
depth_of_inheritance
number_of_children
weighted_methods_per_class
lack_of_cohesion_of_methods
duplicated_line_count
duplicated_line_density
compiler_warning_count
nullable_warning_count
maintainability_index
hotspot_score
```

---

## 35. Версионирование метрик

Формулы метрик будут меняться. Поэтому у каждой метрики должен быть version.

```csharp
public sealed record MetricDescriptor
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Name { get; init; }
}
```

Пример:

```text
cyclomatic_complexity@1.0.0
cognitive_complexity@0.1.0
hotspot_score@0.1.0
```

При изменении формулы повышать version.

---

## 36. Ошибки и diagnostics

Не бросать исключения наружу из метрик, кроме отмены.

Диагностики:

```text
workspace_load_failed
project_load_failed
compilation_failed
semantic_model_unavailable
metric_failed
unsupported_syntax
integer_overflow_guard
invalid_threshold_config
```

Модель:

```csharp
public sealed record AnalysisDiagnostic
{
    public required string Id { get; init; }
    public required string Message { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public string? ProjectPath { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public Exception? Exception { get; init; }
}
```

---

## 37. Конфигурационный файл

Пример `codemetrics.yml`:

```yaml
analysis:
  include:
    - "**/*.cs"
  exclude:
    - "**/bin/**"
    - "**/obj/**"
    - "**/*.g.cs"
    - "**/*.Designer.cs"
  includeGeneratedCode: false
  calculateSemanticMetrics: true

metrics:
  include:
    - lines_of_code
    - cyclomatic_complexity
    - cognitive_complexity
    - class_coupling
    - hotspot_score

thresholds:
  - metric: cyclomatic_complexity
    scope: member
    max: 15
    severity: warning

  - metric: cognitive_complexity
    scope: member
    max: 20
    severity: warning

report:
  format: json
  output: ./artifacts/metrics.json
```

---

## 38. Риски

### 38.1. Roslyn workspace load

Проблема:

```text
MSBuildWorkspace может не загрузить часть проектов.
```

Решение:

```text
fallback syntax-only mode
load diagnostics
continue on error
clear report of skipped metrics
```

### 38.2. Разные трактовки метрик

Проблема:

```text
CC/MI/LCOM отличаются между инструментами.
```

Решение:

```text
фиксировать собственную формулу
версировать формулу
документировать ограничения
```

### 38.3. Производительность

Проблема:

```text
semantic analysis дорогой.
```

Решение:

```text
syntax-first
lazy semantic model
parallel metrics
cache
incremental mode
```

### 38.4. Generated code

Проблема:

```text
generated code искажает метрики.
```

Решение:

```text
exclude patterns
GeneratedCodeAttribute
auto-generated comment
config flag
```

---

## 39. Roadmap по итерациям

### Итерация 1

```text
Solution skeleton
Metric interfaces
Project discovery
Syntax-only loading
JSON reporter
CLI analyze
LOC metrics
```

### Итерация 2

```text
Roslyn semantic loading
Entity index
Cyclomatic Complexity
Nesting Depth
Method length
Parameter count
```

### Итерация 3

```text
Cognitive Complexity
Type metrics
Inheritance metrics
Class Coupling
Dependency graph
```

### Итерация 4

```text
Duplication metrics
Diagnostics metrics
Nullable warnings
Thresholds
Quality gate
```

### Итерация 5

```text
Halstead
Maintainability Index
Hotspot Score
Baseline diff
Markdown report
```

### Итерация 6

```text
Package metrics
Incremental cache
Performance tuning
Documentation generator
CI integration
```

---

## 40. Финальный ожидаемый результат

После реализации набора инструментов должен быть доступен такой сценарий:

```bash
codemetrics analyze ./repo \
  --config codemetrics.yml \
  --output artifacts/metrics.json
```

На выходе:

```text
metrics.json
metrics.ndjson
metrics.csv
summary.md
diagnostics.json
```

Autoresearch сможет использовать:

```text
metrics.json       — полные данные;
summary.md         — человекочитаемый отчёт;
hotspot_score      — ранжирование зон риска;
baseline diff      — поиск регрессий;
source locations   — привязка к коду.
```

Главная цель набора — не доказать качество кода, а дать стабильные, формальные и воспроизводимые признаки, по которым autoresearch сможет выбирать участки для дальнейшего анализа.
