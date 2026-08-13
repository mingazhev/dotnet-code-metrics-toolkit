using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMetricsToolkit.Scoring;

namespace CodeMetricsToolkit.Tests.Scoring;

public sealed partial class ScoringEngineTests
{
    private static readonly JsonSerializerOptions GraphJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
        Assert.Equal("0.1.0", result.Provenance.ExpectedArtifactContractVersion);
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
            artifactSchemaVersion: "0.1.0",
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
            "0.1.0",
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
            artifactSchemaVersion: "0.1.0",
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
            "0.1.0",
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
            "0.1.0",
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

    private static ScoringInputLimits Limits(
        int profileBytes = 100_000,
        int jsonBytes = 100_000,
        long metricsBytes = 100_000,
        int lineCharacters = 100_000,
        int lines = 100,
        int records = 100,
        int jsonDepth = 64)
    {
        return new ScoringInputLimits(
            profileBytes,
            jsonBytes,
            metricsBytes,
            lineCharacters,
            lines,
            records,
            jsonDepth);
    }

    private static MemoryStream ProfileStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static string ValidProfile(int minTargetCount = 1)
    {
        return Profile(ThresholdOperation() + "," + WeightedOperation(), minTargetCount);
    }

    private static string Profile(
        string operations,
        int minTargetCount = 1,
        string primaryKey = "score")
    {
        return $$"""
            {
              "schemaVersion": "1.0.0",
              "id": "maintainability-baseline",
              "version": "1.0.0",
              "artifactContractVersion": "0.1.0",
              "allowedAnalysisModes": ["semantic"],
              "allowedTargetIdStabilities": ["semantic"],
              "selectors": {
                "includeFilePaths": ["src/**/*.cs"],
                "excludeFilePaths": ["src/Generated/**"],
                "requireTrusted": true,
                "minTargetCount": {{minTargetCount}}
              },
              "primary": {
                "key": "{{primaryKey}}",
                "direction": "minimize"
              },
              "operations": [
                {{operations}}
              ]
            }
            """;
    }

    private static string ThresholdOperation()
    {
        return """
            {
              "operation": "thresholdDebt",
              "targetKind": "member",
              "thresholds": [
                {
                  "metricId": "cyclomatic_complexity",
                  "metricVersion": "1.0.0",
                  "maximum": 10,
                  "weight": 2
                },
                {
                  "metricId": "member_length",
                  "metricVersion": "1.0.0",
                  "maximum": 50
                }
              ],
              "outputs": {
                "gapSum": "debt",
                "squaredGapSum": "squared_debt",
                "violatingTargetCount": "violating_targets",
                "maxTargetGap": "max_target_gap"
              }
            }
            """;
    }

    private static string WeightedOperation()
    {
        return """
            {
              "operation": "weightedSum",
              "terms": [
                { "key": "debt", "weight": 1 },
                { "key": "squared_debt", "weight": 0.1 }
              ],
              "output": "score"
            }
            """;
    }

    private static string DiagnosticThresholdOperation(string? tag = null)
    {
        var tags = tag is null ? string.Empty : $", \"tags\": [\"{tag}\"]";

        return $$"""
            {
              "operation": "thresholdDebt",
              "targetKind": "member",
              "thresholds": [
                {
                  "metricId": "diagnostic_count",
                  "metricVersion": "1.0.0",
                  "maximum": 2{{tags}}
                }
              ],
              "outputs": {
                "gapSum": "debt",
                "squaredGapSum": "squared_debt",
                "violatingTargetCount": "violating_targets",
                "maxTargetGap": "max_target_gap"
              }
            }
            """;
    }

    private static string TypeThresholdOperation()
    {
        return """
            {
              "operation": "thresholdDebt",
              "targetKind": "type",
              "thresholds": [
                {
                  "metricId": "lines_of_code",
                  "metricVersion": "1.0.0",
                  "maximum": 50
                }
              ],
              "outputs": {
                "gapSum": "debt",
                "squaredGapSum": "squared_debt",
                "violatingTargetCount": "violating_targets",
                "maxTargetGap": "max_target_gap"
              }
            }
            """;
    }

    private static string Metric(
        string targetId,
        string filePath,
        string metricId,
        string metricVersion,
        double value,
        params string[] tags)
    {
        return MetricForTarget(
            "member",
            targetId,
            filePath,
            metricId,
            metricVersion,
            value,
            tags);
    }

    private static string MetricForTarget(
        string targetKind,
        string targetId,
        string filePath,
        string metricId,
        string metricVersion,
        double value,
        params string[] tags)
    {
        var tagsJson = tags.Length == 0
            ? string.Empty
            : $",\"tags\":{JsonSerializer.Serialize(tags)}";

        return $$"""
            {"schemaVersion":"0.1.0","metricId":"{{metricId}}","metricVersion":"{{metricVersion}}","targetId":"{{targetId}}","targetKind":"{{targetKind}}","targetIdStability":"semantic","valueKind":"integer","numericValue":{{value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}},"filePath":"{{filePath}}"{{tagsJson}}}
            """;
    }

    private static string Sha256(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private sealed class TestArtifacts : IDisposable
    {
        private TestArtifacts(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestArtifacts Create(string analysisQuality, params string[] metrics)
        {
            return CreateWithOptions(
                analysisQuality,
                trustedDiagnostics: true,
                artifactSchemaVersion: "0.1.0",
                metrics);
        }

        public static TestArtifacts CreateWithOptions(
            string analysisQuality,
            bool trustedDiagnostics,
            string artifactSchemaVersion,
            params string[] metrics)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"codemetrics-scoring-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            File.WriteAllText(
                System.IO.Path.Combine(path, "manifest.json"),
                $$"""{"schemaVersion":"{{artifactSchemaVersion}}","mode":"semantic"}""");
            File.WriteAllText(
                System.IO.Path.Combine(path, "summary.json"),
                $$"""
                {
                  "schemaVersion": "{{artifactSchemaVersion}}",
                  "rootPath": "/repo",
                  "metricResultCount": {{metrics.Length}},
                  "analysisHealth": {
                    "analysisQuality": "{{analysisQuality}}",
                    "trustedDiagnostics": {{trustedDiagnostics.ToString().ToLowerInvariant()}}
                  }
                }
                """);
            File.WriteAllLines(System.IO.Path.Combine(path, "metrics.ndjson"), metrics);

            var targets = new Dictionary<string, GraphFixtureTarget>(StringComparer.Ordinal);
            foreach (var metric in metrics)
            {
                using var document = JsonDocument.Parse(metric);
                JsonElement root = document.RootElement;
                var targetId = root.GetProperty("targetId").GetString()!;
                var targetKind = root.GetProperty("targetKind").GetString()!;
                var filePath = root.GetProperty("filePath").GetString()!;
                targets.TryAdd(
                    targetId,
                    new GraphFixtureTarget(targetId, targetKind, filePath, [filePath]));
            }

            WriteGraphFile(path, artifactSchemaVersion, targets.Values);

            return new TestArtifacts(path);
        }

        public void WriteGraph(
            string artifactSchemaVersion,
            params GraphFixtureTarget[] targets)
        {
            WriteGraphFile(Path, artifactSchemaVersion, targets);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }

        private static void WriteGraphFile(
            string artifactPath,
            string artifactSchemaVersion,
            IEnumerable<GraphFixtureTarget> targets)
        {
            var nodes = targets
                .OrderBy(target => target.TargetId, StringComparer.Ordinal)
                .Select(target => new
                {
                    id = target.TargetId,
                    kind = target.TargetKind,
                    name = target.TargetId,
                    targetIdStability = target.TargetIdStability,
                    filePath = target.PrimaryFilePath,
                    startLine = 1,
                    endLine = 1,
                    declarations = target.HasCompleteDeclarationPaths &&
                        target.TargetKind is "type" or "member"
                        ? target.FilePaths
                            .Select(filePath => new { filePath, startLine = 1, endLine = 1 })
                            .ToArray()
                        : null
                })
                .Cast<object>()
                .ToArray();
            var graphJson = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = artifactSchemaVersion,
                    nodes,
                    edges = Array.Empty<object>()
                },
                GraphJsonOptions);
            File.WriteAllText(System.IO.Path.Combine(artifactPath, "graph.json"), graphJson);
        }
    }

    private sealed record GraphFixtureTarget(
        string TargetId,
        string TargetKind,
        string PrimaryFilePath,
        IReadOnlyList<string> FilePaths,
        bool HasCompleteDeclarationPaths = true,
        string TargetIdStability = "semantic");
}
