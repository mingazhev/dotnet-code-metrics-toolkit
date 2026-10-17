**План очистки и развития дизайна — 18 октября 2026**

Приоритет: понятные обязанности, точные имена, единые определения метрик и
проверяемые контракты. Расширение каталога — после этой работы. Это план:
production-код и тесты при исследовании не изменялись и не запускались.

Основа исследования — текущий `dotnet-code-metrics-toolkit` на `e62a56f` и
`code-metrics-toolkit` в worktree `goal_astra` на `1be9fc3`, включая историю
предыдущего структурного рефакторинга. Обычная копия соседнего репозитория в
`Repos/SideProjects/code-metrics-toolkit` осталась на майском `0f1d19d`; брать
изменения только из неё было бы ошибкой.

Прочитаны локальные сессии:

| Сессия | Содержание и результат, зафиксированные в истории |
| --- | --- |
| `019e043e-d3ae-7880-9c94-98dda9154e64`, 8 мая | Ручное ревью, embedded schema validation и исправление документации |
| `01a14b14-af7f-70e3-892c-6d8f4c744995`, 17–18 октября | Очистка, исправления, дополнительный проход `code-that-fits-in-your-head`; PR #2 |
| `01a14b4b-dcef-78c0-8e5f-65ee40332b41`, 18 октября | Partial-методы, пути, type tests, проверка графа и предложения метрик; PR #3 |
| `01a14b5e-63e2-7232-96d6-5477bae577f7`, 18 октября | Сравнение двух репозиториев и выбор текущего как основной базы |

Выводы сессий сверены с исходниками и коммитами. Заявления о прошедших тогда
тестах не считаются результатом нового запуска здесь. Ниже «есть в коде» означает
подтверждённую реализацию; воспроизведение дефектов тестами входит в план.

**Что полезнее всего перенести**

| Из соседнего репозитория | Применение здесь |
| --- | --- |
| `a76cd9a`: метаданные результата берутся из каталога | `MetricResultFactory` всё ещё принимает отдельно ID, version и unit; проекторы повторяют литералы |
| `d07f51e`: ranking использует рассчитанные метрики | `HotspotRanker` заново считает агрегаты, зависимости и диагностику; это основной кандидат на удаление дублирования |
| `043ecb6`: владелец коллекции диагностик с индексом | Здесь передаются пара `List` + `HashSet`, а дубликат вызывает линейный поиск |
| `4f9d88f`, `01e78d0`, `1be9fc3`, `a073156` | Исправления partial declarations, logical members, type tests и многострочных токенов ещё применимы |
| `ca4a653` | Исключение собственной временной копии из обхода отсутствует; лимиты лишь ограничивают последствия |
| `adb855e`, `172fae1`, `dfc1bda` | Папки соответствуют обязанностям; принятые границы и причины отказа от лишних абстракций сохраняются в документации и коммитах |

Соседние исправления переносить по смыслу. Здесь более богатые semantic/operation
facts, другой набор метрик, Roslyn 5.6.0 и дополнительные ограничения входов.
Механический cherry-pick может потерять эти свойства.

**Порядок работ**

1. **Зафиксировать исходное поведение и восстановить достоверную документацию.**

   Выполнить существующие locked restore, Release build/test и package smoke из
   `.github/workflows/dotnet.yml`. В сравнительной сессии restore останавливался на
   NuGet audit; повторно проверить это перед реализацией. Если блокировка остаётся,
   устранить зависимости отдельным изменением, сохранив аудит и locked restore.

   Сохранить нормализованные артефакты semantic и syntax-only анализа для
   `ExpandedMetricsProject`, partial- и shared-source фикстур. Нормализовать только
   заведомо переменные поля через существующий snapshot support, сохраняя версии,
   trust flags, значения, идентификаторы и порядок ranking.

   В `CURRENT_STATE.md` указаны 19 метрик, тогда как каталог содержит **50**.
   Утверждение о 119 прошедших тестах также не привязано к свежему запуску. Убрать
   недатированные заявления о готовности либо связать их с конкретным коммитом и
   проверкой. Использовать каталог и CI как источники фактов, без нового генератора
   документации.

2. **Дать каждому определению метрики одного владельца. Высший приоритет дизайна.**

   Основание: `Reporting/MetricResultFactory.cs:7`,
   `Reporting/SyntaxMetricProjector.cs:84`, `Reporting/HotspotRanker.cs:82`,
   `Reporting/HotspotRanker.cs:292`, `Reporting/GraphMetricProjector.cs:20`.

   Сначала перевести `MetricResultFactory` на существующий `MetricDescriptor`:
   ID, version, unit и допустимый target kind должны определяться каталогом.
   Сохранить различие между целочисленным результатом и явно числовым ratio,
   включая значение ratio `1.0`. Не вводить registry плагинов или отдельный
   интерфейс для каждой метрики.

   Затем изменить последовательность в `CodeMetricsAnalyzer`: facts → основные
   metric rows → ranking → запись. Ranker получает готовые значения компонентов;
   его обязанности — веса, эмпирические percentiles, порядок и объяснение ранга.
   Удалить повторные Max/p95, построение type dependencies и пересечение diagnostic
   spans. При выборе `diagnostic_count` различать суммарную строку и tagged slices.

   Есть существенное отличие от reference: `GraphMetricProjector` не публикует
   метрики при degraded analysis, а ranker сейчас повторно считает зависимости.
   Поэтому перенос не является автоматически эквивалентным. До изменения
   зафиксировать trusted, syntax-only и degraded случаи. Рекомендуемое поведение:
   недоступный компонент исключается с явной нормализацией весов, а не превращается
   в измеренный ноль. Если это меняет текущий ranking, оформить отдельное изменение
   его семантики и версии, не прятать его в рефакторинге.

   Scope: каталог, factory, четыре metric projector, ranker, analyzer,
   `MetricCatalogCoverageTests`, projector/ranker tests и golden expectations.
   Приёмка: одна формула на компонент, компонент hotspot равен соответствующей
   metric row, версии и units берутся из каталога, чисто структурная часть даёт
   прежние артефакты. Существующий публичный вызов `Rank` учитывать при миграции.

3. **Перенести воспроизводимые исправления отдельно от механических переименований.**

   Каждый пункт — небольшой коммит с регрессией, сначала показывающей проблему.

   | Случай | Где проблема видна сейчас | Минимальное исправление и проверка |
   | --- | --- | --- |
   | Partial type в нескольких файлах | `SyntaxMetricProjector.cs:31` группирует merged type по основному `FilePath`; ranker повторяет эту логику | Считать объявления из `Declarations` для file-level `type_count`: две декларации одного типа в одном файле дают 2, в двух файлах — по 1; граф сохраняет один логический type target |
   | Partial method | `SyntaxFactsCollector.cs:271` суммирует число деклараций до merge members | `member_count` типа выводить из объединённых member targets; проверить definition + implementation, наличие тела и согласованность graph/metrics/hotspots |
   | `is string` и явные type patterns | `ControlFlowFactsCollector.cs:143` и `:197` не покрывают все случаи из reference | Перенести однозначные type-test случаи и негативные примеры именованных констант; проверить decision count, CC, cognitive complexity и вложенность |
   | Raw/verbatim multiline string | `LineFactsCollector.cs:28` и `SyntaxFactsCollector.CountTokenLines` считают только первую строку токена | Один общий расчёт строк, занятых span токена, для файла и типа; проверить multiline/raw/verbatim, пустую строку внутри литерала, границы span и производные ratios |
   | Временная копия попадает внутрь входного дерева | `InputIsolator.cs:51` создаёт каталог до обхода; `CopyDirectory` его не исключает | Исключить именно созданный `tempRoot`; проверить вложенный temp на изолированной fixture, очистку после ошибки и сохранение всех лимитов |

   Для `type_count` принять уже записанную в formula единицу «объявление», как в
   reference `4f9d88f`; исправить противоречащий ей description «type targets».
   Не применять deduplication по паре type/file: это другая метрика. У
   `member_count` единица, напротив, — логический member target.

   Для multiline literals отдельно согласовать `blank_line_count`: текущий код
   проверяет whitespace и отсутствие comment trivia, но не token occupancy.
   После расширения token lines нельзя случайно получить противоречие с
   документированным определением blank lines.

   Scope: существующие collectors, проекторы, `PartialTypesProject`,
   `ControlFlowFactsCollectorTests`, `LineFactsCollectorTests`,
   `SyntaxMetricProjectorTests`, `HotspotRankerTests`, `DiscoverySafetyTests` и
   CLI/golden tests. Сохранить semantic identity и различие с syntax fallback.

   Эти исправления могут менять значения, агрегаты и ranking. Явно перечислить
   затронутые metric versions, golden expectations и scoring profiles. Если
   меняется семантика, версию менять согласно `docs/testing.md`; исправление
   реализации документированной формулы как минимум требует объяснения изменения
   baseline и changelog. Не обновлять golden автоматически ради зелёных тестов.

4. **Согласовать имена и расположение кода с обязанностями.**

   | Сейчас | Предлагаемое имя или место | Причина |
   | --- | --- | --- |
   | `Contracts/*` в namespace `CodeMetricsToolkit.Abstractions` | `CodeMetricsToolkit.Core.Contracts` | Проекта Abstractions больше нет; константами владеет Core |
   | `SyntaxAnalysisFacts` | `AnalysisFacts` | Содержит semantic facts, graph, diagnostics и health |
   | `SyntaxFactsCollector` | `AnalysisFactsCollector` | Координирует syntax и semantic pipeline |
   | `MethodLength` | `MemberLength` | Учитываются также properties, indexers и operators |
   | `NonCommentLinesOfCode` | `TokenLineCount` | Это уже публичное понятие `token_line_count`, не универсальное NCLOC |
   | Roslyn collectors в `Metrics`, формулы в `Reporting` | Collectors рядом со сбором facts; каталог и metric projections в `Metrics`; artifact projection/writing в `Reporting` | По имени каталога можно предсказать место изменения |
   | `Syntax.TargetIds` | `Contracts.TargetIds` | Идентичность используется и сборщиками, и проектированием артефактов |

   Сохранить проекты `Core`, `Cli`, `Scoring`. Внутри Core достаточно существующих
   понятий `Analysis`, `Discovery`, `Facts`, `Metrics`, `Reporting`, `Validation`,
   `Contracts`. Roslyn-specific code можно оставить в `Syntax` с соответствующими
   именами; orchestration/workspace/process loading переместить под `Analysis`.
   Не создавать отдельный проект для каждой папки.

   `SyntaxFactsCollector` имеет несколько реальных обязанностей: загрузка,
   нормализация declarations в targets и сбор graph edges. Workspace уже выделен.
   После удаления повторных расчётов оценить одно естественное выделение — сбор
   рёбер из declarations. Не дробить остальные последовательные шаги лишь по
   числу строк или значению complexity.

   Scope: namespaces/usings, все callers, tests, `Support/TestFacts`, architecture
   docs и ссылки. JSON metric IDs и artifact names при этом не менять. Многие
   типы публичные, хотя Core не пакуется отдельно: перед rename проверить
   поддерживаемые C# entry points и потребителей. Изменение CLR API фиксировать
   явно; не строить постоянный слой алиасов без реального потребителя.

5. **Упростить владение состоянием и убрать противоречивые входы.**

   - `AnalysisDiagnosticCollector.Add` (`:59`) принимает согласованную пару
     `List`/`HashSet` от callers и ищет дубликат циклом. Перенести список и индекс
     key → position к одному внутреннему владельцу. Сохранить порядок, объединение
     tags, ключ идентичности и лимит записей. Проверить повторные syntax/compiler
     diagnostics и граничный размер коллекции.
   - `ProjectFileMemberships` — many-to-many модель, но nullable fallback на
     `FileFacts.ProjectKey` повторяется в `SyntaxMetricProjector.cs:48` и
     `GraphProjector.cs:90`. Нормализовать membership один раз. Production collector
     уже его заполняет; тестовые facts привести к той же модели. Если ручное
     публичное создание facts поддерживается, сохранить его через одну
     нормализацию на входе. Проверить `SharedSourceSolution` и отсутствие двойного
     подсчёта физического файла в solution totals.
   - `ArtifactWriter.WriteAsync` принимает и `AnalyzeRequest`, и отдельный
     `includeChunkText` (`:28`). Удалить второй источник настройки: chunks и manifest
     должны читать `request.IncludeChunkText`. Единственный production caller
     сейчас передаёт согласованные значения — это дефект конструкции API, а не
     доказанный сбой CLI. Согласовать повторное составление summary между writer
     и analyzer при работе с этим участком.
   - Удалить подтверждённо неиспользуемые внутренние остатки после проверки всех
     callers: `CreateDefaultReferences`, неиспользуемый `fullSpan` в
     `CreateFileFacts`, лишний context в member declaration, если отсутствие
     использования подтверждается. `WorkspaceHadFailures` сохранить: здесь он
     используется при выборе источников анализа.
   - Правила исключения директорий дублируются в discovery/isolation. Сверить
     их намерения и дать общий владелец одинаковой политике; не объединять сами
     обходы, поскольку копирование имеет отдельные бюджеты и обработку ошибок.
   - Сборщики ссылаются на `ValidationInputLimits.DefaultMaxNdjsonRecords`.
     Общий предел артефакта должен иметь соответствующее имя и владельца в
     контракте, а локальные ingest-ограничения оставаться у читателей.

   Приёмка: невозможно передать расходящиеся настройки chunks/manifest;
   memberships и диагностики не требуют ручного поддержания парного состояния;
   существующие rollback, limits, cancellation и error reporting сохранены.

6. **Закрепить структуру тестами и рабочими правилами.**

   Файлы `ScoringInputLimitTests.cs` и `ScoringProfileValidationTests.cs` сейчас
   объявляют части `ScoringEngineTests`. Сделать именованные наборы тестов по
   контрактам, вынеся только реально общий существующий fixture-support из
   основного класса. Не менять поведенческие assertions на проверки mock-вызовов.

   В архитектурной документации зафиксировать направление:
   CLI → orchestration → discovery/loading → facts → metrics → ranking → artifacts;
   Scoring → завершённые artifacts. Защитить важные границы небольшими проверками
   на существующей тестовой базе: Scoring не получает зависимость от Core/Roslyn,
   facts не зависят от writer/CLI, вычисление метрик не выполняет файловый I/O.
   Проверка границ должна учитывать реальные зависимости, а не только имена папок.

   После каждого смыслового изменения — соответствующие unit/contract tests;
   после завершённого этапа — Release suite, package smoke и проверка артефактов.
   Общий formatter сначала использовать для инвентаризации: массовое форматирование
   не смешивать с исправлением формул. Возможное выделение analyze parser из CLI
   оставить на повторное ревью после основных изменений: дублирующего batch parser
   здесь нет, универсальный parser «на будущее» не нужен.

   Коммиты должны объяснять проблему, изменение и намеренно сохранённые границы.
   Условие окончания — проход всех затронутых обязанностей без новых обоснованных
   проблем, зелёные существующие проверки и объяснённые изменения контрактов.
   «Нулевые smells» и «идеальный SOLID» не являются проверяемыми гарантиями.

**Что уже сделано здесь и не требует повторного переноса**

- Удалён отдельный Abstractions project; остался только неверный namespace.
- Workspace loading, restore и process output уже разделены.
- Повторное открытие проектов, загруженных через references, уже предотвращается.
- Ошибка dotted names вроде `..App` исправлена проверкой целого сегмента `..`.
  Обработку rooted relative-path результата на Windows стоит включить в проверку
  границ; это дополнительный случай, не повод повторять весь reference patch.
- Есть globstar zero-depth, защита от filesystem links, очистка при сбое копирования,
  отмена restore, schema validation и обработка ошибок чтения.
- Текущий проект сильнее reference по публикации с откатом, cross-artifact
  validation, ограничению входов, отдельному Scoring и CI на трёх ОС.

Сохранить эти свойства. Независимость Scoring оправдывает некоторые небольшие
похожие DTO/константы/проверки: объединение пакетов ради удаления нескольких строк
создало бы более дорогую зависимость.

**Метрики: что действительно отсутствует**

Пересчёт каталога по исходникам: здесь **50**, в reference **20** метрик. Все
четыре имени, присутствующие только в reference, уже имеют соответствия здесь:

| Reference | Текущий ID |
| --- | --- |
| `non_comment_lines_of_code` | `token_line_count` |
| `method_length` | `member_length` |
| `dependency_cycle_count` | `dependency_cycle_membership` |
| `transitive_incoming_type_dependency_count` | `transitive_type_dependent_count` |

Соответствие назначения не гарантирует одинаковые значения: формулы, исправления
и требования доверия различаются. Готовых новых семейств для простого переноса нет.
Наследование, coupling, public API/documentation, calls/recursion и часть CFG уже
реализованы здесь; прежний список пожеланий нельзя принимать за текущий backlog.

После очистки предлагается такой порядок:

| Очерёдность | Дополнение | Польза для дизайна и границы работы |
| --- | --- | --- |
| 1 | Зависимости проектов и namespaces: incoming/outgoing counts, component membership/size | Показывает структуру модулей. Сначала проверить полноту type edges, включая поля и другие usage sites: нынешний `type_coupling` не доказывает полноту `uses_type` graph. Понадобятся project references, namespace identity, schemas, validator, fixtures и версии контракта |
| 2 | Дублирование: блоки, строки, доля с координатами | Находит повторение реализации. Это отдельный алгоритм нормализации и сопоставления токенов, с явными минимумом размера, перекрытиями и ограничениями стоимости; не небольшой projector |
| 3 | Небольшие агрегаты по запросу потребителя | `weighted_member_complexity` и project/solution p95/max длины и сложности можно получить из имеющихся facts после исправления partial counts. Зафиксировать population и не суммировать перекрывающиеся spans |
| Позже | Конкретный вариант LCOM | Требует связей member → field, правил для properties/static/constructors и случаев без полей. Не считать его автоматическим доказательством нарушения SRP |

Архитектурные запреты — отдельные правила потребителя: например, запрещённая
зависимость `Scoring → Core`. Core должен публиковать наблюдения; не добавлять
универсальный «балл чистоты». Текущий Scoring не исполняет правила графа, поэтому
не выдавать такую возможность за существующую.

Сравнение с baseline полезно для самого цикла рефакторинга, но это возможность
продукта, а не новая метрика. Начать с сравнения артефактов в проверках; отдельную
CLI-команду делать при появлении потребителя. Сравнивать значения при совместимых
версиях, mode, trust и population, учитывая added/removed targets. Относительное
изменение `hotspot_rank` само по себе не доказывает улучшение кода.

Не включать в первую волну batch analysis, Git/coverage ingestion, unused-symbol
search, Halstead/MI или новые встроенные веса hotspots. Они требуют отдельного
сценария и не решают найденное дублирование и неточные обязанности.

Научиться переносить прежние пороги Sonar/NDepend только после сверки определений.
[Sonar описывает duplication через строки и блоки](https://docs.sonarsource.com/sonarqube-server/user-guide/code-metrics/metrics-definition#duplication),
а [NDepend документирует несколько вариантов LCOM и namespace coupling](https://www.ndepend.com/docs/code-metrics).
Предложенный приоритет — оценка для этого проекта, а не рекомендация этих продуктов.
Подробный существующий backlog находится в [metric-expansion.md](metric-expansion.md).

Для каждого нового семейства обязательны определение population, единица, версия,
trust/fallback semantics, unit + exact golden tests, schemas/invariants при
изменении структуры и измерение стоимости на реалистичном входе. Проверки уже
описаны в [testing.md](../testing.md); их следует расширять, а не заменять.

**Ограничения исследования**

Это сравнение сессий, истории и кода, без нового тестового или performance-прогона.
API-вопрос о partial declarations проверялся через Context7 (`/dotnet/roslyn`),
который не нашёл подходящего материала; официальный fallback —
[Roslyn IMethodSymbol.PartialImplementationPart](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.imethodsymbol.partialimplementationpart?view=roslyn-dotnet-5.6.0).
Точное поведение на используемом Roslyn 5.6.0 закрепить regression fixtures.

Главные риски реализации: незаметное изменение metric/ranking semantics,
совместимость публичных C# имён, потеря many-to-many membership и ослабление
trust/limits при переносе более старого кода. Каждый из них привязан выше к
конкретным проверкам; до их выполнения план не является заявлением об исправлении.
