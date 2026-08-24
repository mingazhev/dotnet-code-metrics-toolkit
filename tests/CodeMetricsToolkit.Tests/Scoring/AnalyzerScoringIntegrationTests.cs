using System.Security.Cryptography;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Scoring;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Scoring;

public sealed class AnalyzerScoringIntegrationTests
{
    [Fact]
    public async Task ReadyProjectFlowsFromAnalyzerArtifactsIntoExactScoreAndProvenance()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = Path.Combine(
            SchemaAssertions.RepositoryRoot(),
            "tests",
            "CodeMetricsToolkit.TestAssets",
            "ExpandedMetricsProject");
        var profilePath = Path.Combine(projectPath, "scoring-profile.json");
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["analyze", projectPath, "--output", output.Path, "--no-restore"],
            standardOutput,
            standardError,
            CancellationToken.None);

        Assert.True(exitCode == 0, standardError.ToString());
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        ScoringResult result = await ScoringEngine.EvaluateAsync(
            output.Path,
            profilePath,
            CancellationToken.None);

        Assert.Equal(
            ["debt", "max_target_gap", "score", "squared_debt", "violating_targets"],
            result.Values.Keys);
        Assert.Equal(17, result.Values["debt"]);
        Assert.Equal(91, result.Values["squared_debt"]);
        Assert.Equal(4, result.Values["violating_targets"]);
        Assert.Equal(8, result.Values["max_target_gap"]);
        Assert.Equal(26.1, result.Values["score"], 12);

        ScoringProvenance provenance = result.Provenance;
        Assert.Equal("1.0.0", provenance.ProfileSchemaVersion);
        Assert.Equal("expanded-metrics-e2e", provenance.ProfileId);
        Assert.Equal("1.0.0", provenance.ProfileVersion);
        Assert.Equal(Sha256(profilePath), provenance.ProfileSha256);
        Assert.Equal("0.1.0", provenance.ExpectedArtifactContractVersion);
        Assert.Equal("0.1.0", provenance.ArtifactSchemaVersion);
        Assert.Equal(Sha256(Path.Combine(output.Path, "manifest.json")), provenance.ManifestSha256);
        Assert.Equal(Sha256(Path.Combine(output.Path, "summary.json")), provenance.SummarySha256);
        Assert.Equal(Sha256(Path.Combine(output.Path, "metrics.ndjson")), provenance.MetricsSha256);
        Assert.Equal(Sha256(Path.Combine(output.Path, "graph.json")), provenance.GraphSha256);
        Assert.Equal(Path.GetFullPath(projectPath), provenance.RootPath);
        Assert.Equal("semantic", provenance.AnalysisMode);
        Assert.Equal(["semantic", "syntax_fallback"], provenance.TargetIdStabilities);
        Assert.Equal(["semantic"], provenance.AllowedAnalysisModes);
        Assert.Equal(["semantic"], provenance.AllowedTargetIdStabilities);
        Assert.Equal("trusted", provenance.AnalysisQuality);
        Assert.True(provenance.TrustedDiagnostics);
        Assert.Equal(["**/*.cs"], provenance.Selection.IncludeFilePaths);
        Assert.Empty(provenance.Selection.ExcludeFilePaths);
        Assert.True(provenance.Selection.RequireTrusted);
        Assert.Equal(7, provenance.Selection.MinTargetCount);
        Assert.Equal("score", provenance.Primary.Key);
        Assert.Equal("minimize", provenance.Primary.Direction);

        ScoringOperationProvenance threshold = provenance.Operations[0];
        Assert.Equal(0, threshold.Index);
        Assert.Equal("thresholdDebt", threshold.Operation);
        Assert.Equal(
            ["cyclomatic_complexity@1.0.0", "member_length@1.0.0"],
            threshold.Inputs);
        Assert.Equal(
            ["debt", "squared_debt", "violating_targets", "max_target_gap"],
            threshold.Outputs);
        Assert.Equal("member", threshold.TargetKind);
        Assert.Equal(7, threshold.SelectedTargetCount);

        ScoringOperationProvenance weighted = provenance.Operations[1];
        Assert.Equal(1, weighted.Index);
        Assert.Equal("weightedSum", weighted.Operation);
        Assert.Equal(["debt", "squared_debt"], weighted.Inputs);
        Assert.Equal(["score"], weighted.Outputs);
        Assert.Null(weighted.TargetKind);
        Assert.Null(weighted.SelectedTargetCount);
    }

    private static string Sha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "codemetrics-scoring-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
