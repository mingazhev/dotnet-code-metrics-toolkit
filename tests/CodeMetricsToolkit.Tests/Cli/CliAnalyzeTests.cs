using System.Diagnostics;
using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Tests.SchemaValidation;
using CodeMetricsToolkit.Tests.Snapshots;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed class CliAnalyzeTests
{
    [Fact]
    public async Task AnalyzeCommandWritesSchemaValidOutputForSimpleProject()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        AssertMandatoryArtifactsExist(output.Path);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement root = summary.RootElement;

        Assert.Equal(1, root.GetProperty("fileCount").GetInt32());
        Assert.Equal(1, root.GetProperty("typeCount").GetInt32());
        Assert.True(root.GetProperty("memberCount").GetInt32() > 0);
        JsonElement analysisHealth = root.GetProperty("analysisHealth");
        Assert.Equal("trusted", analysisHealth.GetProperty("analysisQuality").GetString());
        Assert.Equal("msbuild", analysisHealth.GetProperty("semanticModel").GetString());
        Assert.True(analysisHealth.GetProperty("trustedDiagnostics").GetBoolean());
        Assert.Contains(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("\"metricId\":\"method_length\"", StringComparison.Ordinal));
        Assert.Contains(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("\"targetIdStability\":\"syntax_fallback\"", StringComparison.Ordinal));

        JsonElement[] graphNodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        JsonElement[] graphEdges = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "edges");
        JsonElement[] chunks = ReadNdjson(Path.Combine(output.Path, "chunks.ndjson"));

        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "file"));
        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "type") && HasPropertyValue(node, "targetIdStability", "semantic"));
        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "member") && HasPropertyValue(node, "targetIdStability", "semantic"));
        Assert.Contains(graphEdges, edge => HasPropertyValue(edge, "kind", "declares"));
        Assert.Contains(graphEdges, edge => HasPropertyValue(edge, "kind", "contains"));
        Assert.Contains(chunks, chunk => HasPropertyValue(chunk, "targetKind", "member") && chunk.GetProperty("textHash").GetString()!.StartsWith("sha256:", StringComparison.Ordinal));
        Assert.DoesNotContain(chunks, chunk => chunk.TryGetProperty("text", out _));

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        Assert.Contains(metrics, metric => HasPropertyValue(metric, "metricId", "diagnostic_count"));
        Assert.Contains(metrics, metric => HasPropertyValue(metric, "metricId", "member_count"));
    }

    [Fact]
    public async Task AnalyzeCommandHandlesDirectoryWithMultipleProjects()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string assetsPath = Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets");

        int exitCode = await RunCliAsync("analyze", assetsPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.True(summary.RootElement.GetProperty("projectCount").GetInt32() > 1);
        Assert.True(summary.RootElement.GetProperty("diagnosticCount").GetInt32() > 0);
    }

    [Fact]
    public async Task AnalyzeCommandExcludesGeneratedFilesByDefault()
    {
        using TemporaryDirectory input = TemporaryDirectory.Create();
        using TemporaryDirectory output = TemporaryDirectory.Create();
        CreateGeneratedFileSample(input.Path);

        int exitCode = await RunCliAsync("analyze", input.Path, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(1, summary.RootElement.GetProperty("fileCount").GetInt32());
        Assert.DoesNotContain(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("Generated.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandMergesPartialTypeGraphNode()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("PartialTypesProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] graphNodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        JsonElement[] graphEdges = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "edges");
        const string partialTypeId = "type:PartialTypesProject/T:PartialTypesProject.PartialOrder";

        JsonElement partialType = Assert.Single(graphNodes, node => HasPropertyValue(node, "id", partialTypeId));
        Assert.Equal(2, partialType.GetProperty("declarations").GetArrayLength());
        Assert.Equal(2, graphEdges.Count(edge =>
            HasPropertyValue(edge, "kind", "declares") &&
            HasPropertyValue(edge, "to", partialTypeId)));
    }

    [Fact]
    public async Task AnalyzeCommandEmitsSemanticRelationshipEdges()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SemanticGraphProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] graphEdges = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "edges");

        Assert.Contains(graphEdges, edge =>
            HasPropertyValue(edge, "kind", "inherits") &&
            HasPropertyValue(edge, "from", "type:SemanticGraphProject/T:SemanticGraphProject.OrderService") &&
            HasPropertyValue(edge, "to", "type:SemanticGraphProject/T:SemanticGraphProject.ServiceBase"));
        Assert.Contains(graphEdges, edge =>
            HasPropertyValue(edge, "kind", "implements") &&
            HasPropertyValue(edge, "from", "type:SemanticGraphProject/T:SemanticGraphProject.OrderService") &&
            HasPropertyValue(edge, "to", "type:SemanticGraphProject/T:SemanticGraphProject.IOrderService"));
        Assert.Contains(graphEdges, edge => HasPropertyValue(edge, "kind", "uses_type"));
        Assert.Contains(graphEdges, edge => HasPropertyValue(edge, "kind", "calls"));

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        const string orderServiceId = "type:SemanticGraphProject/T:SemanticGraphProject.OrderService";
        Assert.Equal(7, GetMetric(metrics, orderServiceId, "outgoing_type_dependency_count").GetProperty("numericValue").GetInt32());
        Assert.Equal(0, GetMetric(metrics, orderServiceId, "dependency_cycle_count").GetProperty("numericValue").GetInt32());
    }

    [Fact]
    public async Task AnalyzeCommandCanIncludeChunkText()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--include-chunk-text");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] chunks = ReadNdjson(Path.Combine(output.Path, "chunks.ndjson"));

        Assert.Contains(chunks, chunk =>
            HasPropertyValue(chunk, "chunkKind", "member_body") &&
            chunk.TryGetProperty("text", out JsonElement text) &&
            text.GetString()!.Contains("LastResult = left + right;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandCanForceSyntaxFallback()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--syntax-only");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
        JsonElement[] graphNodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        JsonElement[] chunks = ReadNdjson(Path.Combine(output.Path, "chunks.ndjson"));

        Assert.Equal("syntax", manifest.RootElement.GetProperty("mode").GetString());
        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "type") && HasPropertyValue(node, "targetIdStability", "syntax_fallback"));
        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "member") && HasPropertyValue(node, "targetIdStability", "syntax_fallback"));
        Assert.Contains(chunks, chunk => HasPropertyValue(chunk, "targetKind", "member") && HasPropertyValue(chunk, "targetIdStability", "syntax_fallback"));
    }

    [Fact]
    public async Task AnalyzeCommandEmitsComplexityMetricsAndStableHotspots()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("ComplexityProject");
        const string scoreTargetId = "member:ComplexityProject/M:ComplexityProject.DecisionSamples.Score(ComplexityProject.Order,System.Collections.Generic.IReadOnlyList{System.Int32})";

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--top", "3");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement[] hotspots = summary.RootElement
            .GetProperty("hotspots")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();

        Assert.Equal(23, GetMetric(metrics, scoreTargetId, "cyclomatic_complexity").GetProperty("numericValue").GetInt32());
        Assert.Equal(29, GetMetric(metrics, scoreTargetId, "cognitive_complexity").GetProperty("numericValue").GetInt32());
        Assert.Equal(2, GetMetric(metrics, scoreTargetId, "nesting_depth").GetProperty("numericValue").GetInt32());
        Assert.Contains(metrics, metric => HasPropertyValue(metric, "metricId", "hotspot_rank") && HasPropertyValue(metric, "targetKind", "member"));
        Assert.Contains(metrics, metric => HasPropertyValue(metric, "metricId", "hotspot_rank") && HasPropertyValue(metric, "targetKind", "type"));
        Assert.Contains(metrics, metric => HasPropertyValue(metric, "metricId", "hotspot_rank") && HasPropertyValue(metric, "targetKind", "file"));

        Assert.Equal(3, hotspots.Length);
        Assert.Equal(scoreTargetId, hotspots[0].GetProperty("targetId").GetString());
        Assert.Equal(1, hotspots[0].GetProperty("rank").GetInt32());
        Assert.Equal(0.9, hotspots[0].GetProperty("rankScore").GetDouble());
        Assert.Contains(
            hotspots[0].GetProperty("reasons").EnumerateArray(),
            reason => reason.GetString() == "cyclomatic_complexity=23 p1 w0.25");

        JsonElement[] components = hotspots[0]
            .GetProperty("components")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();

        JsonElement methodLength = Assert.Single(components, component => HasPropertyValue(component, "metricId", "method_length"));
        JsonElement cyclomatic = Assert.Single(components, component => HasPropertyValue(component, "metricId", "cyclomatic_complexity"));
        Assert.Equal(0.15, methodLength.GetProperty("weight").GetDouble());
        Assert.True(cyclomatic.GetProperty("weight").GetDouble() > methodLength.GetProperty("weight").GetDouble());
        Assert.Contains(components, component => HasPropertyValue(component, "metricId", "diagnostic_count"));
    }

    [Fact]
    public async Task AnalyzeCommandDoesNotCrashOnBrokenProjectAndEmitsCompilerDiagnostics()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("BrokenProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] diagnostics = ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson"));

        Assert.Contains(diagnostics, diagnostic => HasPropertyValue(diagnostic, "id", "CS1002") && HasTag(diagnostic, "syntax"));
        Assert.Contains(diagnostics, diagnostic => HasPropertyValue(diagnostic, "id", "CS0246") && HasTag(diagnostic, "compiler"));
    }

    [Fact]
    public async Task AnalyzeCommandEmitsNullableDiagnostics()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("NullableDiagnosticsProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] diagnostics = ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson"));

        Assert.Contains(diagnostics, diagnostic => HasPropertyValue(diagnostic, "id", "CS8602") && HasTag(diagnostic, "nullable"));
        Assert.Contains(diagnostics, diagnostic => HasPropertyValue(diagnostic, "id", "CS8603") && HasTag(diagnostic, "nullable"));

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        JsonElement nullableFileDiagnosticCount = Assert.Single(metrics, metric =>
            HasPropertyValue(metric, "targetId", "file:NullableCases.cs") &&
            HasPropertyValue(metric, "metricId", "diagnostic_count") &&
            !metric.TryGetProperty("tags", out _));

        Assert.Equal(4, nullableFileDiagnosticCount.GetProperty("numericValue").GetInt32());
        Assert.Contains(metrics, metric =>
            HasPropertyValue(metric, "targetId", "file:NullableCases.cs") &&
            HasPropertyValue(metric, "metricId", "diagnostic_count") &&
            HasTag(metric, "nullable") &&
            metric.GetProperty("numericValue").GetInt32() == 4);
    }

    [Fact]
    public async Task AnalyzeCommandSupportsIncludeAndExcludeGlobs()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string assetsPath = Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets");

        int exitCode = await RunCliAsync(
            "analyze",
            assetsPath,
            "--output",
            output.Path,
            "--include",
            "SemanticGraphProject/*.cs",
            "--exclude",
            "**/Domain.cs");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(2, summary.RootElement.GetProperty("fileCount").GetInt32());
        Assert.DoesNotContain(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("Domain.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandAcceptsSemanticNoRestoreAndMaxDegreeOptions()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int exitCode = await RunCliAsync(
            "analyze",
            projectPath,
            "--output",
            output.Path,
            "--semantic",
            "--no-restore",
            "--max-degree-of-parallelism",
            "2");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
        Assert.Equal("semantic", manifest.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task AnalyzeCommandCanIsolateInputFromAmbientBuildFiles()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--isolate-input");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement analysisHealth = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("trusted", analysisHealth.GetProperty("analysisQuality").GetString());
        Assert.DoesNotContain(
            analysisHealth.GetProperty("messages").EnumerateArray(),
            message => message.GetString()!.Contains("Ambient MSBuild", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListMetricsAndExplainCommandsExposeMetricCatalog()
    {
        using var listOutput = new StringWriter();
        using var listError = new StringWriter();
        int listExitCode = await CliApplication.RunAsync(["list-metrics"], listOutput, listError, CancellationToken.None);

        using var explainOutput = new StringWriter();
        using var explainError = new StringWriter();
        int explainExitCode = await CliApplication.RunAsync(["explain", "diagnostic_count"], explainOutput, explainError, CancellationToken.None);

        Assert.Equal(0, listExitCode);
        Assert.Equal(0, explainExitCode);
        Assert.Contains("diagnostic_count@1.0.0", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Formula: number of diagnostics whose span overlaps the target", explainOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeCommandEmitsCriticalProjectLoadDiagnostic()
    {
        using TemporaryDirectory input = TemporaryDirectory.Create();
        using TemporaryDirectory output = TemporaryDirectory.Create();
        CreateInvalidProjectSample(input.Path);

        int exitCode = await RunCliAsync("analyze", input.Path, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] diagnostics = ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson"));

        Assert.Contains(diagnostics, diagnostic =>
            HasPropertyValue(diagnostic, "id", "project_load_failed") &&
            HasPropertyValue(diagnostic, "severity", "critical") &&
            HasTag(diagnostic, "project_load"));

        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement analysisHealth = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("degraded", analysisHealth.GetProperty("analysisQuality").GetString());
        Assert.False(analysisHealth.GetProperty("trustedDiagnostics").GetBoolean());

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        Assert.DoesNotContain(metrics, metric => HasPropertyValue(metric, "metricId", "diagnostic_count"));
    }

    [Fact]
    public async Task ValidateOutputCommandCatchesMissingRequiredArtifact()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(output.Path, "manifest.json"), "{}");

        int exitCode = await RunCliAllowFailureAsync("validate-output", output.Path);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ValidateOutputCommandAcceptsGeneratedArtifacts()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int analyzeExitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);
        int validateExitCode = await RunCliAsync("validate-output", output.Path);

        Assert.Equal(0, analyzeExitCode);
        Assert.Equal(0, validateExitCode);
    }

    [Fact]
    public async Task AnalyzeCommandCompletesMediumRepoSmokeWithinThreshold()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string assetsPath = Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets");
        var stopwatch = Stopwatch.StartNew();

        int exitCode = await RunCliAsync("analyze", assetsPath, "--output", output.Path, "--top", "5");

        stopwatch.Stop();

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Analysis took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SnapshotNormalizerRemovesVolatileManifestFields()
    {
        using TemporaryDirectory output = TemporaryDirectory.Create();
        string projectPath = TestAssetPath("SimpleProject");

        int exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);

        string normalizedManifest = ArtifactSnapshotNormalizer.NormalizeJsonFile(Path.Combine(output.Path, "manifest.json"));
        using JsonDocument normalized = JsonDocument.Parse(normalizedManifest);

        Assert.Equal("<root>", normalized.RootElement.GetProperty("rootPath").GetString());
        Assert.Equal("<timestamp>", normalized.RootElement.GetProperty("startedAt").GetString());
        Assert.Equal(0, normalized.RootElement.GetProperty("durationMs").GetInt32());
    }

    private static async Task<int> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(args, output, error, CancellationToken.None);

        Assert.True(exitCode == 0, error.ToString());
        return exitCode;
    }

    private static async Task<int> RunCliAllowFailureAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        return await CliApplication.RunAsync(args, output, error, CancellationToken.None);
    }

    private static string TestAssetPath(string assetName)
    {
        return Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets", assetName);
    }

    private static void AssertMandatoryArtifactsExist(string outputPath)
    {
        Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(outputPath, "summary.json")));
        Assert.True(File.Exists(Path.Combine(outputPath, "metrics.ndjson")));
        Assert.True(File.Exists(Path.Combine(outputPath, "diagnostics.ndjson")));
        Assert.True(File.Exists(Path.Combine(outputPath, "graph.json")));
        Assert.True(File.Exists(Path.Combine(outputPath, "chunks.ndjson")));
    }

    private static JsonElement[] ReadJsonArray(string artifactPath, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(artifactPath));

        return document.RootElement
            .GetProperty(propertyName)
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
    }

    private static JsonElement[] ReadNdjson(string artifactPath)
    {
        return File.ReadLines(artifactPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using JsonDocument document = JsonDocument.Parse(line);

                return document.RootElement.Clone();
            })
            .ToArray();
    }

    private static bool HasPropertyValue(JsonElement element, string propertyName, string expectedValue)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            string.Equals(property.GetString(), expectedValue, StringComparison.Ordinal);
    }

    private static bool HasTag(JsonElement element, string expectedTag)
    {
        return element.TryGetProperty("tags", out JsonElement tags) &&
            tags.EnumerateArray().Any(tag => tag.GetString() == expectedTag);
    }

    private static JsonElement GetMetric(JsonElement[] metrics, string targetId, string metricId)
    {
        return Assert.Single(metrics, metric =>
            HasPropertyValue(metric, "targetId", targetId) &&
            HasPropertyValue(metric, "metricId", metricId));
    }

    private static void CreateGeneratedFileSample(string rootPath)
    {
        File.WriteAllText(
            Path.Combine(rootPath, "GeneratedSample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(rootPath, "Real.cs"),
            """
            namespace GeneratedSample;

            public sealed class Real
            {
                public int Value() => 42;
            }
            """);

        File.WriteAllText(
            Path.Combine(rootPath, "Generated.g.cs"),
            """
            namespace GeneratedSample;

            public sealed class Generated
            {
                public int Value() => 13;
            }
            """);
    }

    private static void CreateInvalidProjectSample(string rootPath)
    {
        File.WriteAllText(
            Path.Combine(rootPath, "InvalidProject.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
            """);

        File.WriteAllText(
            Path.Combine(rootPath, "ValidSyntax.cs"),
            """
            namespace InvalidProject;

            public sealed class ValidSyntax
            {
                public int Value() => 42;
            }
            """);
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
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "codemetrics-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(path);

            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
