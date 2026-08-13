using CodeMetricsToolkit.Scoring;
using Json.Schema;
using System.Text.Json;

namespace CodeMetricsToolkit.Tests.Scoring;

public sealed partial class ScoringEngineTests
{
    [Fact]
    public async Task RejectsUnknownOperationBeforeReadingArtifacts()
    {
        var profileJson = ValidProfile().Replace(
            "thresholdDebt",
            "shellCommand",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("Unknown operation 'shellCommand'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMissingThresholdOutputKey()
    {
        var profileJson = System.Text.RegularExpressions.Regex.Replace(
            ValidProfile(),
            ",\\s*\"maxTargetGap\":\\s*\"max_target_gap\"",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("maxTargetGap", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsProfileVersionWithoutMajorMinorPatch()
    {
        var profileJson = ValidProfile().Replace(
            "\"version\": \"1.0.0\"",
            "\"version\": \"1.0\"",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("major.minor.patch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsUnsupportedArtifactContractInProfile()
    {
        var profileJson = ValidProfile().Replace(
            "\"artifactContractVersion\": \"0.1.0\"",
            "\"artifactContractVersion\": \"9.9.9\"",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("artifactContractVersion '9.9.9'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsArtifactsThatDoNotMatchProfileContract()
    {
        var firstMetric = Metric(
            "member:a",
            "src/A.cs",
            "cyclomatic_complexity",
            "1.0.0",
            15).Replace("\"schemaVersion\":\"0.1.0\"", "\"schemaVersion\":\"9.9.9\"", StringComparison.Ordinal);
        var secondMetric = Metric(
            "member:a",
            "src/A.cs",
            "member_length",
            "1.0.0",
            55).Replace("\"schemaVersion\":\"0.1.0\"", "\"schemaVersion\":\"9.9.9\"", StringComparison.Ordinal);
        using var artifacts = TestArtifacts.CreateWithOptions(
            analysisQuality: "trusted",
            trustedDiagnostics: true,
            artifactSchemaVersion: "9.9.9",
            firstMetric,
            secondMetric);
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Contains("profile requires artifactContractVersion '0.1.0'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsValueKindAndValueFieldMismatch()
    {
        var invalidMetric = Metric(
            "member:a",
            "src/A.cs",
            "cyclomatic_complexity",
            "1.0.0",
            15).Replace("\"valueKind\":\"integer\"", "\"valueKind\":\"string\"", StringComparison.Ordinal);
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            invalidMetric,
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Contains("valueKind 'string' requires one stringValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMultipleValueFields()
    {
        var invalidMetric = Metric(
            "member:a",
            "src/A.cs",
            "cyclomatic_complexity",
            "1.0.0",
            15).Replace("\"filePath\"", "\"stringValue\":\"15\",\"filePath\"", StringComparison.Ordinal);
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            invalidMetric,
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Contains("exactly one of numericValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsFractionalIntegerBeyondDoubleExactRange()
    {
        var invalidMetric = Metric(
            "member:a",
            "src/A.cs",
            "cyclomatic_complexity",
            "1.0.0",
            15).Replace(
                "\"valueKind\":\"integer\",\"numericValue\":15",
                "\"valueKind\":\"integer\",\"numericValue\":9007199254740992.5",
                StringComparison.Ordinal);
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            invalidMetric,
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.InvalidArtifacts, exception.FailureKind);
        Assert.Contains("requires an integral numericValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsDuplicateOutputKey()
    {
        var profileJson = ValidProfile().Replace(
            "\"squaredGapSum\": \"squared_debt\"",
            "\"squaredGapSum\": \"debt\"",
            StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("Duplicate output key 'debt'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsWeightedSumDependencyDeclaredBeforeItsInput()
    {
        var thresholdOperation = ThresholdOperation();
        var weightedOperation = WeightedOperation();
        var profileJson = Profile(weightedOperation + "," + thresholdOperation);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("not an output of an earlier operation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsNonFiniteProfileNumber()
    {
        var profileJson = ValidProfile().Replace("\"maximum\": 10", "\"maximum\": 1e999", StringComparison.Ordinal);
        await using Stream profile = ProfileStream(profileJson);

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync("does-not-exist", profile));

        Assert.Equal(ScoringFailureKind.InvalidProfile, exception.FailureKind);
        Assert.Contains("must be a finite number", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsNonFiniteComputedOutput()
    {
        using var artifacts = TestArtifacts.Create(
            analysisQuality: "trusted",
            Metric("member:a", "src/A.cs", "cyclomatic_complexity", "1.0.0", 8e307),
            Metric("member:a", "src/A.cs", "member_length", "1.0.0", 55));
        await using Stream profile = ProfileStream(ValidProfile());

        ScoringException exception = await Assert.ThrowsAsync<ScoringException>(
            () => ScoringEngine.EvaluateAsync(artifacts.Path, profile));

        Assert.Equal(ScoringFailureKind.PreconditionsNotMet, exception.FailureKind);
        Assert.Contains("non-finite squaredGapSum", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoringProfileExampleValidatesAgainstSchema()
    {
        var repositoryRoot = CodeMetricsToolkit.Tests.SchemaValidation.SchemaAssertions.RepositoryRoot();
        var schemaPath = System.IO.Path.Combine(
            repositoryRoot,
            "src",
            "CodeMetricsToolkit.Scoring",
            "Schemas",
            "scoring-profile.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        using var profile = JsonDocument.Parse(ValidProfile());

        EvaluationResults results = schema.Evaluate(profile.RootElement);

        Assert.True(results.IsValid);
    }

}
