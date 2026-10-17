using System.Security.Cryptography;
using CodeMetricsToolkit.Scoring;
using static CodeMetricsToolkit.Tests.Scoring.ScoringTestSupport;

namespace CodeMetricsToolkit.Tests.Scoring;

public sealed class ScoringEngineTests
{
    private static readonly string[] ExpectedValueKeys =
    [
        "debt",
        "max_target_gap",
        "score",
        "squared_debt",
        "violating_targets"
    ];

    [Fact]
    public async Task EvaluatesThresholdDebtAndWeightedSumWithDeterministicProvenance()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55),
            Metric("member:b", "src/B.cs", "cyclomatic_complexity", "1.0.0", 8),
            Metric("member:b", "src/B.cs", "member_length", "1.0.0", 40),
            Metric("member:generated", "src/Generated/C.g.cs", "cyclomatic_complexity", "1.0.0", 100),
            Metric("member:generated", "src/Generated/C.g.cs", "member_length", "1.0.0", 100));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringResult result = await ScoringEngine.EvaluateAsync(artifacts.Path, profile);

        Assert.Equal(ExpectedValueKeys, result.Values.Keys);
        Assert.Equal(15, result.Values["debt"]);
        Assert.Equal(225, result.Values["squared_debt"]);
        Assert.Equal(1, result.Values["violating_targets"]);
        Assert.Equal(15, result.Values["max_target_gap"]);
        Assert.Equal(37.5, result.Values["score"]);
        Assert.Equal("score", result.Provenance.Primary.Key);
        Assert.Equal("minimize", result.Provenance.Primary.Direction);
        Assert.Equal("maintainability-baseline", result.Provenance.ProfileId);
        Assert.Equal("1.0.0", result.Provenance.ProfileVersion);
        Assert.Equal("trusted", result.Provenance.AnalysisQuality);
        Assert.Equal("semantic", result.Provenance.AnalysisMode);
        Assert.Equal(["semantic"], result.Provenance.TargetIdStabilities);
        Assert.Equal(["semantic"], result.Provenance.AllowedAnalysisModes);
        Assert.Equal(["semantic"], result.Provenance.AllowedTargetIdStabilities);
        Assert.Equal(2, result.Provenance.Operations[0].SelectedTargetCount);
        Assert.Equal(64, result.Provenance.ProfileSha256.Length);
        Assert.Equal(
            Sha256(System.IO.Path.Combine(artifacts.Path, "manifest.json")),
            result.Provenance.ManifestSha256);
        Assert.Equal(
            Sha256(System.IO.Path.Combine(artifacts.Path, "summary.json")),
            result.Provenance.SummarySha256);
        Assert.Equal(
            Sha256(System.IO.Path.Combine(artifacts.Path, "metrics.ndjson")),
            result.Provenance.MetricsSha256);
        Assert.Equal(
            Sha256(System.IO.Path.Combine(artifacts.Path, "graph.json")),
            result.Provenance.GraphSha256);
        Assert.Equal("0.2.0", result.Provenance.ExpectedArtifactContractVersion);
    }

    [Fact]
    public async Task RejectsAnalysisThatDoesNotMeetTrustedPrecondition()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "degraded",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("requires trusted analysis", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsTrustedAnalysisWhenDiagnosticsAreNotTrusted()
    {
        using var artifacts = TestArtifacts.CreateWithOptions(
            analysisQuality: "trusted",
            trustedDiagnostics: false,
            artifactSchemaVersion: "0.2.0",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("trustedDiagnostics=false", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAnalysisModeNotPinnedByProfile()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        var manifestPath = System.IO.Path.Combine(artifacts.Path, "manifest.json");
        File.WriteAllText(
            manifestPath,
            File.ReadAllText(manifestPath).Replace(
                "\"mode\":\"semantic\"",
                "\"mode\":\"partial_semantic\"",
                StringComparison.Ordinal));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("mode 'partial_semantic' is not allowed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticMemberPinAllowsScoredFileSyntaxFallback()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55),
            MetricForTarget(
                "file",
                "file:src/A.cs",
                "src/A.cs",
                "lines_of_code",
                "1.0.0",
                40,
                stability: "syntax_fallback"));
        await using Stream profile = ProfileStream(
            Profile(ThresholdOperation() + "," + FileThresholdOperation() + "," + WeightedOperation()));

        ScoringResult result = await ScoringEngine.EvaluateAsync(artifacts.Path, profile);

        Assert.Equal(15, result.Values["debt"]);
        Assert.Equal(0, result.Values["file_debt"]);
    }

    [Fact]
    public async Task AllowsSemanticPinWhenUnscoredStructuralRowsUseSyntaxFallback()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55),
            MetricForTarget(
                "solution",
                "solution:root",
                ".",
                "lines_of_code",
                "1.0.0",
                10,
                stability: "syntax_fallback"));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringResult result = await ScoringEngine.EvaluateAsync(artifacts.Path, profile);

        Assert.Equal(15, result.Values["debt"]);
        Assert.Equal(["semantic"], result.Provenance.AllowedTargetIdStabilities);
    }

    [Fact]
    public async Task RejectsTargetIdStabilityNotPinnedByProfile()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        var metricsPath = System.IO.Path.Combine(artifacts.Path, "metrics.ndjson");
        File.WriteAllText(
            metricsPath,
            File.ReadAllText(metricsPath).Replace(
                "\"targetIdStability\":\"semantic\"",
                "\"targetIdStability\":\"syntax_fallback\"",
                StringComparison.Ordinal));
        artifacts.WriteGraph(
            "0.2.0",
            new GraphFixtureTarget(
                "member:a",
                "member",
                "src/A.cs",
                ["src/A.cs"],
                TargetIdStability: "syntax_fallback"));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("syntax_fallback", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMetricWhoseTargetStabilityDiffersFromGraph()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        var metricsPath = System.IO.Path.Combine(artifacts.Path, "metrics.ndjson");
        File.WriteAllText(
            metricsPath,
            File.ReadAllText(metricsPath).Replace(
                "\"targetIdStability\":\"semantic\"",
                "\"targetIdStability\":\"syntax_fallback\"",
                StringComparison.Ordinal));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Contains(
            "targetIdStability 'syntax_fallback', but its graph node uses 'semantic'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsUntrustedDiagnosticsOnlyWhenProfileOptsOut()
    {
        using var artifacts = TestArtifacts.CreateWithOptions(
            analysisQuality: "trusted",
            trustedDiagnostics: false,
            artifactSchemaVersion: "0.2.0",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        var profileJson = ValidProfile().Replace(
            "\"requireTrusted\": true",
            "\"requireTrusted\": false",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringResult result = await ScoringEngine.EvaluateAsync(artifacts.Path, profile);

        Assert.Equal(15, result.Values["debt"]);
    }

    [Fact]
    public async Task RejectsSelectionBelowMinimumTargetCount()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile(minTargetCount: 2));

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("minTargetCount=2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsEmptyFileSelection()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "tests/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "tests/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("selected no 'member' targets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsUnknownMetric()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        var profileJson = ValidProfile().Replace(
            "cyclomatic_complexity",
            "not_a_metric",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("Unknown metric 'not_a_metric'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMetricVersionThatDoesNotExactlyMatchArtifact()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        var profileJson = ValidProfile().Replace(
            "\"metricVersion\": \"1.0.0\"",
            "\"metricVersion\": \"2.0.0\"",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("exact current version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsThresholdWithoutMetricVersion()
    {
        var profileJson = System.Text.RegularExpressions.Regex.Replace(
            ValidProfile(),
            "\\s*\"metricVersion\"\\s*:\\s*\"1\\.0\\.0\"\\s*,",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("metricVersion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMalformedMetricVersion()
    {
        var profileJson = ValidProfile().Replace(
            "\"metricVersion\": \"1.0.0\"",
            "\"metricVersion\": \"latest\"",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("major.minor.patch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMissingMetricForSelectedTarget()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55),
            Metric("member:b", "src/B.cs", "cyclomatic_complexity", "1.0.0", 8));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("missing required metric 'member_length@1.0.0'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntaggedThresholdUsesAggregateWhenTaggedMetricRowsAlsoExist()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "diagnostic_count", "1.0.0", 4),
            Metric("member:a", "src/A.cs", "diagnostic_count", "1.0.0", 100, "compiler"));
        await using Stream aggregateProfile = ProfileStream(
            Profile(DiagnosticThresholdOperation(), primaryKey: "debt"));

        ScoringResult aggregateResult = await ScoringEngine.EvaluateAsync(
            artifacts.Path,
            aggregateProfile);

        Assert.Equal(2, aggregateResult.Values["debt"]);

        await using Stream taggedProfile = ProfileStream(
            Profile(DiagnosticThresholdOperation("compiler"), primaryKey: "debt"));

        ScoringResult taggedResult = await ScoringEngine.EvaluateAsync(
            artifacts.Path,
            taggedProfile);

        Assert.Equal(98, taggedResult.Values["debt"]);
    }

    [Fact]
    public async Task DistinctTagSetsContainingTheSignatureSeparatorAreNotDuplicateMetrics()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55),
            Metric("member:a", "src/A.cs", "diagnostic_count", "1.0.0", 1, "left\u001fright"),
            Metric("member:a", "src/A.cs", "diagnostic_count", "1.0.0", 1, "left", "right"));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringResult result = await ScoringEngine.EvaluateAsync(artifacts.Path, profile);

        Assert.Equal(37.5, result.Values["score"]);
    }

    [Fact]
    public async Task ReorderedDuplicateTagSetsAreRejectedAsDuplicateMetrics()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 15),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55),
            Metric("member:a", "src/A.cs", "diagnostic_count", "1.0.0", 1, "left", "right"),
            Metric("member:a", "src/A.cs", "diagnostic_count", "1.0.0", 1, "right", "left"));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Contains("contains duplicate metric 'diagnostic_count@1.0.0'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileSelectorRejectsAggregatedTargetWhenAnyDeclarationIsOutsideIncludeScope()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            MetricForTarget(
                "type",
                "type:partial",
                "src/Included/PartA.cs",
                "lines_of_code",
                "1.0.0",
                100));
        artifacts.WriteGraph(
            "0.2.0",
            new GraphFixtureTarget(
                "type:partial",
                "type",
                "src/Included/PartA.cs",
                ["src/Included/PartA.cs", "src/Outside/PartB.cs"]));
        var profileJson = Profile(TypeThresholdOperation(), primaryKey: "debt").Replace(
            "[\"src/**/*.cs\"]",
            "[\"src/Included/**/*.cs\"]",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("selected no 'type' targets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteGraphDeclarationsAreRequiredOnlyForFileSelectors()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            MetricForTarget(
                "type",
                "type:incomplete",
                "src/Included/PartA.cs",
                "lines_of_code",
                "1.0.0",
                100));
        artifacts.WriteGraph(
            "0.2.0",
            new GraphFixtureTarget(
                "type:incomplete",
                "type",
                "src/Included/PartA.cs",
                ["src/Included/PartA.cs"],
                HasCompleteDeclarationPaths: false));
        await using Stream profile = ProfileStream(
            Profile(TypeThresholdOperation(), primaryKey: "debt"));

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("require complete graph declaration paths", exception.Message, StringComparison.Ordinal);

        var noSelectorsProfileJson = Profile(TypeThresholdOperation(), primaryKey: "debt")
            .Replace("[\"src/**/*.cs\"]", "[]", StringComparison.Ordinal)
            .Replace("[\"src/Generated/**\"]", "[]", StringComparison.Ordinal);
        await using Stream noSelectorsProfile = ProfileStream(noSelectorsProfileJson);

        ScoringResult result = await ScoringEngine.EvaluateAsync(
            artifacts.Path,
            noSelectorsProfile);

        Assert.Equal(50, result.Values["debt"]);
    }

    private static string Sha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
