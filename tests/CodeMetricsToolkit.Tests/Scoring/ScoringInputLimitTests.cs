using System.Text;
using CodeMetricsToolkit.Scoring;

namespace CodeMetricsToolkit.Tests.Scoring;

public sealed partial class ScoringEngineTests
{
    [Fact]
    public void DefaultScoringLimitsMatchDocumentedSecurityBoundary()
    {
        Assert.Equal(1024 * 1024, ScoringInputLimits.Default.MaxProfileBytes);
        Assert.Equal(128 * 1024 * 1024, ScoringInputLimits.Default.MaxJsonArtifactBytes);
        Assert.Equal(1024L * 1024 * 1024, ScoringInputLimits.Default.MaxMetricsBytes);
        Assert.Equal(8 * 1024 * 1024, ScoringInputLimits.Default.MaxMetricLineCharacters);
        Assert.Equal(1_000_000, ScoringInputLimits.Default.MaxMetricLines);
        Assert.Equal(1_000_000, ScoringInputLimits.Default.MaxMetricRecords);
        Assert.Equal(64, ScoringInputLimits.Default.MaxJsonDepth);
    }

    [Fact]
    public async Task RejectsProfileStreamsThatExceedTheConfiguredByteLimit()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        var profileJson = ValidProfile();
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                artifacts.Path,
                profile,
                Limits(profileBytes: 16),
                CancellationToken.None));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Equal(
            $"The scoring profile is {Encoding.UTF8.GetByteCount(profileJson)} bytes and " +
            "exceeds the maximum of 16 bytes.",
            exception.Message);

        var profilePath = System.IO.Path.Combine(artifacts.Path, "bounded-profile.json");
        File.WriteAllText(profilePath, profileJson);
        ScoringException fileException = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                artifacts.Path,
                profilePath,
                Limits(profileBytes: 16),
                CancellationToken.None));
        Assert.Equal(exception.Message, fileException.Message);
    }

    [Fact]
    public async Task RejectsJsonAndMetricsArtifactsBeforeConfiguredByteLimits()
    {
        using var jsonArtifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        await using Stream jsonProfile = ProfileStream(ValidProfile());

        ScoringException json = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                jsonArtifacts.Path,
                jsonProfile,
                Limits(jsonBytes: 1),
                CancellationToken.None));
        Assert.Equal(ScoringFailureKind.InvalidArtifacts, json.FailureKind);
        Assert.Contains("manifest.json", json.Message, StringComparison.Ordinal);
        Assert.Contains("exceeds the maximum of 1 bytes", json.Message, StringComparison.Ordinal);

        using var metricArtifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        await using Stream metricProfile = ProfileStream(ValidProfile());

        ScoringException metrics = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                metricArtifacts.Path,
                metricProfile,
                Limits(metricsBytes: 1),
                CancellationToken.None));
        Assert.Equal(ScoringFailureKind.InvalidArtifacts, metrics.FailureKind);
        Assert.Contains("metrics.ndjson", metrics.Message, StringComparison.Ordinal);
        Assert.Contains("exceeds the maximum of 1 bytes", metrics.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMetricsLineRecordAndJsonDepthLimits()
    {
        using var lineArtifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        await using Stream lineProfile = ProfileStream(ValidProfile());
        ScoringException line = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                lineArtifacts.Path,
                lineProfile,
                Limits(lineCharacters: 32),
                CancellationToken.None));
        Assert.Contains(
            "metrics.ndjson:1 exceeds the maximum line length of 32 characters",
            line.Message,
            StringComparison.Ordinal);

        using var lineCountArtifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        File.AppendAllText(
            System.IO.Path.Combine(lineCountArtifacts.Path, "metrics.ndjson"),
            "\n\n");
        await using Stream lineCountProfile = ProfileStream(ValidProfile());
        ScoringException lineCount = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                lineCountArtifacts.Path,
                lineCountProfile,
                Limits(lines: 2),
                CancellationToken.None));
        Assert.Contains(
            "metrics.ndjson exceeds the maximum line count of 2",
            lineCount.Message,
            StringComparison.Ordinal);

        using var recordArtifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        await using Stream recordProfile = ProfileStream(ValidProfile());
        ScoringException records = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                recordArtifacts.Path,
                recordProfile,
                Limits(records: 1),
                CancellationToken.None));
        Assert.Contains(
            "metrics.ndjson exceeds the maximum record count of 1",
            records.Message,
            StringComparison.Ordinal);

        using var depthArtifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 1));
        await using Stream depthProfile = ProfileStream(ValidProfile());
        ScoringException depth = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                depthArtifacts.Path,
                depthProfile,
                Limits(jsonDepth: 2),
                CancellationToken.None));
        Assert.Equal(ScoringFailureKind.InvalidProfile, depth.FailureKind);
        Assert.Equal("The scoring profile is not valid JSON.", depth.Message);
    }

    [Fact]
    public async Task RejectsInvalidUtf8MetricsAsInvalidArtifacts()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 1));
        File.WriteAllBytes(
            System.IO.Path.Combine(artifacts.Path, "metrics.ndjson"),
            [0x7b, 0xff, 0x7d, 0x0a]);
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(() =>
            ScoringEngine.EvaluateAsync(
                artifacts.Path,
                profile,
                CancellationToken.None));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Equal("metrics.ndjson is not valid UTF-8.", exception.Message);
    }

}
