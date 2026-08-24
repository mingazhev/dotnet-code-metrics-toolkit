# CodeMetricsToolkit.Scoring

`CodeMetricsToolkit.Scoring` is an optional policy layer for validated
CodeMetricsToolkit artifacts. It is a separate package: the analyzer does not depend on
it, and raw metrics remain usable without a score.

The engine supports a deliberately small, deterministic profile language:
`thresholdDebt` produces additive debt values and `weightedSum` combines prior outputs.
Profiles must pin the artifact contract and every metric version they consume.

They must also state which analysis modes and target-id stability classes are accepted.
`allowedTargetIdStabilities` is applied to the targets each `thresholdDebt` operation will
score after file selectors, not to structural solution/project rows that Core always
emits as `syntax_fallback`. That requirement prevents a profile calibrated on semantic
symbols from silently scoring syntax-fallback members or types.

```json
{
  "schemaVersion": "1.0.0",
  "id": "example-quality-debt",
  "version": "1.0.0",
  "artifactContractVersion": "0.1.0",
  "allowedAnalysisModes": ["semantic"],
  "allowedTargetIdStabilities": ["semantic"],
  "selectors": {
    "requireTrusted": true,
    "minTargetCount": 1
  },
  "primary": {
    "key": "score",
    "direction": "minimize"
  },
  "operations": [
    {
      "operation": "thresholdDebt",
      "targetKind": "member",
      "thresholds": [
        {
          "metricId": "cyclomatic_complexity",
          "metricVersion": "1.0.0",
          "maximum": 10
        }
      ],
      "outputs": {
        "gapSum": "gap",
        "squaredGapSum": "squaredGap",
        "violatingTargetCount": "violations",
        "maxTargetGap": "maxGap"
      }
    },
    {
      "operation": "weightedSum",
      "terms": [
        { "key": "gap", "weight": 1.0 },
        { "key": "squaredGap", "weight": 0.05 },
        { "key": "violations", "weight": 1.0 },
        { "key": "maxGap", "weight": 1.0 }
      ],
      "output": "score"
    }
  ]
}
```

```csharp
using CodeMetricsToolkit.Scoring;

ScoringResult result = await ScoringEngine.EvaluateAsync(
    artifactDirectory: "artifacts/codemetrics",
    profilePath: "quality-profile.json");

double objective = result.Values["score"];
string metricsHash = result.Provenance.MetricsSha256;
```

The package includes `schemas/scoring-profile.schema.json`. Scoring fails closed when
analysis trust, artifact versions, metric versions, target declarations, or generation
consistency do not satisfy the profile.

Scoring also enforces hard resource limits before or while reading inputs: 1 MiB for a
profile, 128 MiB each for `manifest.json`, `summary.json`, and `graph.json`, and 1 GiB
for `metrics.ndjson`. Required artifacts must be regular files; symbolic links and
reparse points are rejected. Metrics are streamed with limits of 8 Mi characters per line,
1,000,000 physical lines, and 1,000,000 non-empty metric records. JSON nesting is
limited to 64. Inputs beyond these limits fail with `InvalidProfile` or
`InvalidArtifacts`; data is never silently truncated.

Path selectors use normalized, artifact-root-relative paths. `*` and `?` never cross a
directory separator, `**` does, and `**/` may match zero directories. For example,
`*.cs` selects only root files while `**/*.cs` selects C# files at any depth. A leading
`./` and Windows `\` separators are normalized before matching.

Consumers must score only after `codemetrics analyze` has completed. Concurrent reads
during output replacement are outside the supported publication model.
