using CodeMetricsToolkit.Scoring;
using static CodeMetricsToolkit.Tests.Scoring.ScoringTestSupport;

namespace CodeMetricsToolkit.Tests.Scoring;

public sealed class ScoringNamespaceTargetTests
{
    [Fact]
    public async Task ScoresNamespaceTargetsByTheirSourceDeclarationPaths()
    {
        const string targetId = "namespace:project/N:Library.Api";
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            MetricForTarget(
                "namespace",
                targetId,
                "Project/Project.csproj",
                "outgoing_namespace_dependency_count",
                "1.0.0",
                4,
                stability: "syntax_fallback"));
        artifacts.WriteGraph(
            "0.2.0",
            new GraphFixtureTarget(
                targetId,
                "namespace",
                "Project/Project.csproj",
                ["src/Api.cs", "src/ApiExtensions.cs"],
                TargetIdStability: "syntax_fallback"));
        await using Stream profile = ProfileStream(
            """
            {
              "schemaVersion": "1.0.0",
              "id": "namespace-dependencies",
              "version": "1.0.0",
              "artifactContractVersion": "0.2.0",
              "allowedAnalysisModes": ["semantic"],
              "allowedTargetIdStabilities": ["syntax_fallback"],
              "selectors": {
                "includeFilePaths": ["src/**/*.cs"],
                "requireTrusted": true,
                "minTargetCount": 1
              },
              "primary": { "key": "debt", "direction": "minimize" },
              "operations": [
                {
                  "operation": "thresholdDebt",
                  "targetKind": "namespace",
                  "thresholds": [
                    { "metricId": "outgoing_namespace_dependency_count", "metricVersion": "1.0.0", "maximum": 1 }
                  ],
                  "outputs": {
                    "gapSum": "debt",
                    "squaredGapSum": "squared_debt",
                    "violatingTargetCount": "violations",
                    "maxTargetGap": "max_gap"
                  }
                }
              ]
            }
            """);

        ScoringResult result = await ScoringEngine.EvaluateAsync(artifacts.Path, profile);

        Assert.Equal(3, result.Values["debt"]);
        Assert.Equal(9, result.Values["squared_debt"]);
        Assert.Equal(1, result.Values["violations"]);
        Assert.Equal(3, result.Values["max_gap"]);
        Assert.Equal(1, result.Provenance.Operations[0].SelectedTargetCount);
    }
}
