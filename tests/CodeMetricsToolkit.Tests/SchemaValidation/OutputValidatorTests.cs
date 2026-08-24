using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMetricsToolkit.Core.Validation;

namespace CodeMetricsToolkit.Tests.SchemaValidation;

public sealed class OutputValidatorTests
{
    [Fact]
    public void DefaultValidationLimitsMatchDocumentedSecurityBoundary()
    {
        Assert.Equal(128L * 1024 * 1024, ValidationInputLimits.Default.MaxJsonArtifactBytes);
        Assert.Equal(1024L * 1024 * 1024, ValidationInputLimits.Default.MaxNdjsonArtifactBytes);
        Assert.Equal(8 * 1024 * 1024, ValidationInputLimits.Default.MaxNdjsonLineCharacters);
        Assert.Equal(1_000_000, ValidationInputLimits.Default.MaxNdjsonLines);
        Assert.Equal(1_000_000, ValidationInputLimits.Default.MaxNdjsonRecords);
        Assert.Equal(64, ValidationInputLimits.Default.MaxJsonDepth);
    }

    [Fact]
    public void ValidFixturePassesRuntimeValidation()
    {
        using var output = TemporaryOutput.Create();

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SyntacticallyValidButSchemaInvalidArtifactIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("summary.json"), root => root["projectCount"] = "one");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("summary.json does not conform", StringComparison.Ordinal) &&
                error.Contains("/projectCount", StringComparison.Ordinal));
    }

    [Fact]
    public void MetricValueKindThatDoesNotMatchItsValueFieldIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateFirstNdjsonRecord(
            output.File("metrics.ndjson"),
            root => root["valueKind"] = "string");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("metrics.ndjson:1 does not conform", StringComparison.Ordinal) &&
                error.Contains("metric-result.schema.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeValidationIsSafeAcrossParallelCalls()
    {
        using var output = TemporaryOutput.Create();
        Task<OutputValidationResult>[] validations = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => OutputValidator.Validate(output.Path, CancellationToken.None)))
            .ToArray();

        OutputValidationResult[] results = await Task.WhenAll(validations);

        Assert.All(
            results,
            result => Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors)));
    }

    [Fact]
    public void InvalidManifestTimestampIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("manifest.json"), root => root["startedAt"] = "not-a-timestamp");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("manifest.json does not conform", StringComparison.Ordinal) &&
                error.Contains("/startedAt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("metricResultCount", 2, "metrics.ndjson")]
    [InlineData("diagnosticCount", 0, "diagnostics.ndjson")]
    public void SummaryRecordCountMismatchIsRejected(
        string summaryProperty,
        int declaredCount,
        string artifactName)
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("summary.json"), root => root[summaryProperty] = declaredCount);

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains($"summary.{summaryProperty}", StringComparison.Ordinal) &&
                error.Contains(artifactName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fileCount", "file")]
    [InlineData("typeCount", "type")]
    [InlineData("memberCount", "member")]
    public void SummaryGraphNodeCountMismatchIsRejected(string summaryProperty, string nodeKind)
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("summary.json"), root => root[summaryProperty] = 2);

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            $"summary.{summaryProperty} is 2 but graph.json contains 1 {nodeKind} node(s).",
            result.Errors);
    }

    [Fact]
    public void SummaryProjectCountMismatchIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("summary.json"), root => root["projectCount"] = 2);

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            "summary.projectCount is 2 but graph.json contains 1 project node(s).",
            result.Errors);
    }

    [Fact]
    public void RequiredArtifactSymlinkIsRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var output = TemporaryOutput.Create();
        var artifactPath = output.File("metrics.ndjson");
        var targetPath = output.File("metrics.real.ndjson");
        File.Move(artifactPath, targetPath);
        File.CreateSymbolicLink(artifactPath, targetPath);

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            "Required artifact must be a regular file, not a symbolic link, reparse point, or special file: metrics.ndjson",
            result.Errors);
    }

    [Fact]
    public void SummaryRootPathThatDiffersFromManifestIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("summary.json"), root => root["rootPath"] = "/different/repo");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            "summary.rootPath '/different/repo' does not match manifest.rootPath '/repo'.",
            result.Errors);
    }

    [Theory]
    [InlineData("metrics.ndjson", "member", "member:missing")]
    [InlineData("metrics.ndjson", "project", "project:missing")]
    [InlineData("chunks.ndjson", "member", "member:missing")]
    public void ArtifactTargetThatIsMissingFromGraphIsRejected(
        string artifactName,
        string targetKind,
        string targetId)
    {
        using var output = TemporaryOutput.Create();
        UpdateFirstNdjsonRecord(
            output.File(artifactName),
            root =>
            {
                root["targetKind"] = targetKind;
                root["targetId"] = targetId;
            });

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            $"{artifactName}:1 targetId '{targetId}' references a missing graph node.",
            result.Errors);
    }

    [Theory]
    [InlineData("metrics.ndjson")]
    [InlineData("chunks.ndjson")]
    public void ArtifactTargetKindThatDiffersFromGraphIsRejected(string artifactName)
    {
        using var output = TemporaryOutput.Create();
        UpdateFirstNdjsonRecord(
            output.File(artifactName),
            root => root["targetKind"] = "type");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.StartsWith(
                $"{artifactName}:1 targetKind 'type' does not match graph node kind 'member'",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("metrics.ndjson")]
    [InlineData("chunks.ndjson")]
    public void ArtifactTargetStabilityThatDiffersFromGraphIsRejected(string artifactName)
    {
        using var output = TemporaryOutput.Create();
        UpdateFirstNdjsonRecord(
            output.File(artifactName),
            root => root["targetIdStability"] = "syntax_fallback");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.StartsWith(
                $"{artifactName}:1 targetIdStability 'syntax_fallback' does not match " +
                "graph node targetIdStability 'semantic'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GraphNodeWithoutTargetStabilityIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("graph.json"), root =>
        {
            JsonObject firstNode = root["nodes"]!.AsArray()[0]!.AsObject();
            Assert.True(firstNode.Remove("targetIdStability"));
        });

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("graph.json does not conform", StringComparison.Ordinal) &&
                error.Contains("targetIdStability", StringComparison.Ordinal));
    }

    [Fact]
    public void GraphEdgeWithMissingEndpointIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("graph.json"), root =>
        {
            JsonArray edges = root["edges"]!.AsArray();
            edges[0]!.AsObject()["to"] = "type:missing";
        });

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            "graph.edges[0].to references missing node 'type:missing'.",
            result.Errors);
    }

    [Fact]
    public void ManifestWithUnexpectedArtifactFilenameIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("manifest.json"), root =>
        {
            root["artifacts"]!.AsObject()["summary"] = "custom-summary.json";
        });

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            "manifest.artifacts.summary must be 'summary.json' but was 'custom-summary.json'.",
            result.Errors);
    }

    [Fact]
    public void ArtifactSchemaVersionMismatchIsRejected()
    {
        using var output = TemporaryOutput.Create();
        UpdateJson(output.File("summary.json"), root => root["schemaVersion"] = "0.2.0");

        OutputValidationResult result = OutputValidator.Validate(output.Path, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            "summary.json schemaVersion '0.2.0' does not match manifest schemaVersion '0.1.0'.",
            result.Errors);
    }

    [Fact]
    public void ValidationRejectsJsonAndNdjsonFilesBeforeConfiguredByteLimits()
    {
        using var jsonOutput = TemporaryOutput.Create();
        var manifestLength = new FileInfo(jsonOutput.File("manifest.json")).Length;

        OutputValidationResult jsonResult = OutputValidator.Validate(
            jsonOutput.Path,
            Limits(jsonBytes: manifestLength - 1),
            CancellationToken.None);

        Assert.False(jsonResult.IsValid);
        Assert.Contains(
            $"manifest.json is {manifestLength} bytes and exceeds the maximum of " +
            $"{manifestLength - 1} bytes.",
            jsonResult.Errors);

        using var ndjsonOutput = TemporaryOutput.Create();
        var metricsLength = new FileInfo(ndjsonOutput.File("metrics.ndjson")).Length;

        OutputValidationResult ndjsonResult = OutputValidator.Validate(
            ndjsonOutput.Path,
            Limits(ndjsonBytes: metricsLength - 1),
            CancellationToken.None);

        Assert.False(ndjsonResult.IsValid);
        Assert.Contains(
            $"metrics.ndjson is {metricsLength} bytes and exceeds the maximum of " +
            $"{metricsLength - 1} bytes.",
            ndjsonResult.Errors);
    }

    [Fact]
    public void ValidationRejectsNdjsonLineLineCountAndRecordCountLimits()
    {
        using var longLineOutput = TemporaryOutput.Create();
        OutputValidationResult longLine = OutputValidator.Validate(
            longLineOutput.Path,
            Limits(lineCharacters: 32),
            CancellationToken.None);
        Assert.Contains(
            "metrics.ndjson:1 exceeds the maximum line length of 32 characters.",
            longLine.Errors);

        using var lineCountOutput = TemporaryOutput.Create();
        File.WriteAllText(lineCountOutput.File("diagnostics.ndjson"), "\n\n\n");
        OutputValidationResult lineCount = OutputValidator.Validate(
            lineCountOutput.Path,
            Limits(lines: 2),
            CancellationToken.None);
        Assert.Contains(
            "diagnostics.ndjson exceeds the maximum line count of 2.",
            lineCount.Errors);

        using var recordCountOutput = TemporaryOutput.Create();
        var metric = File.ReadAllText(recordCountOutput.File("metrics.ndjson"));
        File.AppendAllText(recordCountOutput.File("metrics.ndjson"), metric);
        OutputValidationResult recordCount = OutputValidator.Validate(
            recordCountOutput.Path,
            Limits(records: 1),
            CancellationToken.None);
        Assert.Contains(
            "metrics.ndjson exceeds the maximum record count of 1.",
            recordCount.Errors);
    }

    [Fact]
    public void ValidationReportsInvalidUtf8InNdjsonAsAContractError()
    {
        using var output = TemporaryOutput.Create();
        File.WriteAllBytes(output.File("metrics.ndjson"), [0x7b, 0xff, 0x7d, 0x0a]);

        OutputValidationResult result = OutputValidator.Validate(
            output.Path,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("metrics.ndjson is not valid UTF-8.", result.Errors);
    }

    [Fact]
    public void ValidationUsesConfiguredJsonMaximumDepth()
    {
        using var output = TemporaryOutput.Create();

        OutputValidationResult result = OutputValidator.Validate(
            output.Path,
            Limits(jsonDepth: 2),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.StartsWith("manifest.json is invalid JSON", StringComparison.Ordinal) &&
                error.Contains("maximum configured depth", StringComparison.OrdinalIgnoreCase));
    }

    private static ValidationInputLimits Limits(
        long jsonBytes = 10_000,
        long ndjsonBytes = 10_000,
        int lineCharacters = 10_000,
        int lines = 100,
        int records = 100,
        int jsonDepth = 64)
    {
        return new ValidationInputLimits(
            jsonBytes,
            ndjsonBytes,
            lineCharacters,
            lines,
            records,
            jsonDepth);
    }

    private static void UpdateJson(string path, Action<JsonObject> update)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        update(root);
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void UpdateFirstNdjsonRecord(string path, Action<JsonObject> update)
    {
        var lines = File.ReadAllLines(path);
        var recordIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        Assert.True(recordIndex >= 0, $"No NDJSON record was found in {path}.");

        JsonObject root = JsonNode.Parse(lines[recordIndex])!.AsObject();
        update(root);
        lines[recordIndex] = root.ToJsonString();
        File.WriteAllLines(path, lines);
    }

    private sealed class TemporaryOutput : IDisposable
    {
        private TemporaryOutput(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryOutput Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeMetricsToolkit-output-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);

            var fixturePath = System.IO.Path.Combine(
                SchemaAssertions.RepositoryRoot(),
                "tests",
                "CodeMetricsToolkit.Tests",
                "Fixtures",
                "ExpectedOutput",
                "ValidMinimal");

            foreach (var sourcePath in Directory.EnumerateFiles(fixturePath))
            {
                System.IO.File.Copy(
                    sourcePath,
                    System.IO.Path.Combine(path, System.IO.Path.GetFileName(sourcePath)));
            }

            return new TemporaryOutput(path);
        }

        public string File(string fileName)
        {
            return System.IO.Path.Combine(Path, fileName);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
