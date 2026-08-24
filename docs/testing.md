# Testing strategy

CodeMetricsToolkit treats metric output as a versioned product contract. A metric is not complete when its collector merely emits a plausible value.

Every catalog metric must pass both gates:

1. A unit test exercises the collector or projector formula, including relevant trust and empty-input behavior.
2. `GoldenMetricContractTests` analyzes the checked-in `ExpandedMetricsProject`, validates every artifact schema and cross-artifact invariant, and compares one exact target/value/version/kind/unit expectation for every metric ID.

`MetricCatalogCoverageTests` compares `MetricCatalog.All` with the explicitly unit-tested metric families. The golden test independently compares the catalog with `expected-metrics.json`. Adding a metric to the catalog therefore fails both gates until both levels are updated.

## Coverage map

| Product area | Unit/contract tests | End-to-end fixture coverage |
| --- | --- | --- |
| Physical, token and comment LOC | `LineFactsCollectorTests`, `SyntaxMetricProjectorTests` | exact file/project/solution results in `ExpandedMetricsProject` |
| Syntax complexity and aggregates | `ControlFlowFactsCollectorTests`, `SyntaxMetricProjectorTests` | exact member/file targets in the golden contract; `ComplexityProject` stress sample |
| Type/call graphs, SCCs and transitive counts | `GraphMetricProjectorTests` | exact semantic IDs and graph-derived values in the golden contract; `SemanticGraphProject` relationship sample |
| Inheritance, coupling and API documentation | `TypeSemanticFactsCollectorTests`, `SemanticMetricProjectorTests` | exact type/project values in the golden contract |
| IOperation and CFG | `OperationFactsCollectorTests`, `SemanticMetricProjectorTests` | exact operations, allocations, awaits, blocks, edges and CFG complexity in the golden contract |
| Diagnostics | `DiagnosticMetricProjectorTests` | exact zero-diagnostic contract row; diagnostic-producing fixtures in `NullableDiagnosticsProject` and `BrokenProject` |
| Hotspot ranking | `HotspotRankerTests` | exact stable target/rank in the golden contract; richer ordering assertions in `ComplexityProject` |
| Graph/chunk artifacts | schema, invariant and CLI tests | real artifacts from all analyzed fixture projects |
| Atomic publication and output validation | `ArtifactDirectoryPublisherTests`, `OutputValidatorTests`, schema tests | every CLI integration validates the published directory |
| Input selection, isolation and degraded analysis | discovery and CLI tests | dedicated multi-project, broken, generated and nullable fixture scenarios |
| Scoring profiles and provenance | `ScoringEngineTests` | `AnalyzerScoringIntegrationTests` runs the analyzer on `ExpandedMetricsProject`, validates the emitted artifacts, and asserts an exact score plus complete provenance |

The golden file is intentionally reviewed source, not regenerated during a test. A formula or Roslyn-version change must produce an explicit expectation diff and, when semantics change, a metric version change.
