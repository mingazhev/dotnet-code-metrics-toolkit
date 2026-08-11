# CodeMetricsToolkit.Scoring

`CodeMetricsToolkit.Scoring` is an optional policy layer for validated
CodeMetricsToolkit artifacts. It is a separate package: the analyzer does not depend on
it, and raw metrics remain usable without a score.

The engine supports a deliberately small, deterministic profile language:
`thresholdDebt` produces additive debt values and `weightedSum` combines prior outputs.
Profiles must pin the artifact contract and every metric version they consume.

They must also state which analysis modes and target-id stability classes are accepted.
That requirement prevents a profile calibrated on semantic symbols from silently scoring
syntax-fallback targets.

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

Consumers must score only after `codemetrics analyze` has completed. Concurrent reads
during output replacement are outside the supported publication model.
