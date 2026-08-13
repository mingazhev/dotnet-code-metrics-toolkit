using System.Diagnostics;
using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Core;
using CodeMetricsToolkit.Tests.SchemaValidation;
using CodeMetricsToolkit.Tests.Snapshots;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed class CliAnalyzeTests
{
    [Fact]
    public async Task AnalyzeCommandWritesSchemaValidOutputForSimpleProject()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        AssertMandatoryArtifactsExist(output.Path);
        SchemaAssertions.OutputDirectoryValidates(output.Path);
        AssertArtifactsUseCanonicalNewlines(output.Path);

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement root = summary.RootElement;

        Assert.Equal(1, root.GetProperty("fileCount").GetInt32());
        Assert.Equal(1, root.GetProperty("typeCount").GetInt32());
        Assert.True(root.GetProperty("memberCount").GetInt32() > 0);
        JsonElement analysisHealth = root.GetProperty("analysisHealth");
        Assert.Equal("trusted", analysisHealth.GetProperty("analysisQuality").GetString());
        Assert.Equal("msbuild", analysisHealth.GetProperty("semanticModel").GetString());
        Assert.True(analysisHealth.GetProperty("trustedDiagnostics").GetBoolean());
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
        Assert.Equal(ToolkitInfo.Version, manifest.RootElement.GetProperty("toolVersion").GetString());
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

    private static void AssertArtifactsUseCanonicalNewlines(string outputPath)
    {
        foreach (var artifactPath in Directory.EnumerateFiles(outputPath))
        {
            var bytes = File.ReadAllBytes(artifactPath);
            Assert.DoesNotContain((byte)'\r', bytes);

            if (bytes.Length > 0)
            {
                Assert.Equal((byte)'\n', bytes[^1]);
            }
        }
    }

    [Fact]
    public async Task AnalyzeCommandHandlesDirectoryWithMultipleProjects()
    {
        using var output = TemporaryDirectory.Create();
        var assetsPath = Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets");

        var exitCode = await RunCliAsync("analyze", assetsPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.True(summary.RootElement.GetProperty("projectCount").GetInt32() > 1);
        Assert.True(summary.RootElement.GetProperty("diagnosticCount").GetInt32() > 0);
    }

    [Fact]
    public async Task AnalyzeCommandDoesNotReopenAlreadyLoadedProjectReferences()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        var projectA = Path.Combine(input.Path, "ProjectA");
        var projectB = Path.Combine(input.Path, "ProjectB");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);
        File.WriteAllText(
            Path.Combine(projectA, "ProjectA.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../ProjectB/ProjectB.csproj" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(projectB, "ProjectB.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(projectA, "A.cs"),
            "public sealed class A { public ProjectB.B Value { get; } = new(); }");
        File.WriteAllText(
            Path.Combine(projectB, "B.cs"),
            "namespace ProjectB; public sealed class B { }");

        var exitCode = await RunCliAsync(
            "analyze",
            input.Path,
            "--output",
            output.Path,
            "--no-restore");

        Assert.Equal(0, exitCode);
        using var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement health = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("trusted", health.GetProperty("analysisQuality").GetString());
        Assert.Equal(2, summary.RootElement.GetProperty("projectCount").GetInt32());
        Assert.Contains(
            ReadNdjson(Path.Combine(output.Path, "metrics.ndjson")),
            metric => HasPropertyValue(metric, "metricId", "class_coupling"));
    }

    [Fact]
    public async Task AnalyzeCommandExcludesGeneratedFilesByDefault()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        CreateGeneratedFileSample(input.Path);

        var exitCode = await RunCliAsync("analyze", input.Path, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(1, summary.RootElement.GetProperty("fileCount").GetInt32());
        Assert.DoesNotContain(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("Generated.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandMergesPartialTypeGraphNode()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("PartialTypesProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

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
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SemanticGraphProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

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
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--include-chunk-text");

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
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAsync(
            "analyze",
            projectPath,
            "--output",
            output.Path,
            "--syntax-only");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
        JsonElement[] graphNodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        JsonElement[] chunks = ReadNdjson(Path.Combine(output.Path, "chunks.ndjson"));

        Assert.Equal("syntax", manifest.RootElement.GetProperty("mode").GetString());
        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "type") && HasPropertyValue(node, "targetIdStability", "syntax_fallback"));
        Assert.Contains(graphNodes, node => HasPropertyValue(node, "kind", "member") && HasPropertyValue(node, "targetIdStability", "syntax_fallback"));
        Assert.Contains(chunks, chunk => HasPropertyValue(chunk, "targetKind", "member") && HasPropertyValue(chunk, "targetIdStability", "syntax_fallback"));

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        Assert.DoesNotContain(metrics, metric => HasPropertyValue(metric, "metricId", "operation_count"));
        Assert.DoesNotContain(metrics, metric => HasPropertyValue(metric, "metricId", "class_coupling"));
    }

    [Fact]
    public async Task AnalyzeCommandClassifiesLinesAndAggregatesUniqueFileLoc()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(input.Path, "LineSample.cs"),
            string.Join(
                '\n',
                "// ordinary",
                "",
                "/// <summary>",
                "/// Docs.",
                "/// </summary>",
                "public class Sample // mixed",
                "{",
                "    public void M() { } /* mixed */",
                "}"));

        var exitCode = await RunCliAsync(
            "analyze",
            input.Path,
            "--syntax-only",
            "--output",
            output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        const string fileTargetId = "file:LineSample.cs";

        Assert.Equal(9, MetricInt(metrics, fileTargetId, "lines_of_code"));
        Assert.Equal(4, MetricInt(metrics, fileTargetId, "non_comment_lines_of_code"));
        Assert.Equal(1, MetricInt(metrics, fileTargetId, "blank_line_count"));
        Assert.Equal(4, MetricInt(metrics, fileTargetId, "comment_only_line_count"));
        Assert.Equal(6, MetricInt(metrics, fileTargetId, "commented_line_count"));
        Assert.Equal(2, MetricInt(metrics, fileTargetId, "mixed_code_comment_line_count"));
        Assert.Equal(3, MetricInt(metrics, fileTargetId, "documentation_comment_line_count"));
        Assert.Equal(4d / 9d, MetricDouble(metrics, fileTargetId, "token_line_ratio"), 12);
        Assert.Equal(
            "number",
            GetMetric(metrics, fileTargetId, "token_line_ratio")
                .GetProperty("valueKind")
                .GetString());
        Assert.Equal(9, MetricInt(metrics, "solution:root", "lines_of_code"));
        Assert.Equal(6d / 9d, MetricDouble(metrics, "solution:root", "commented_line_ratio"), 12);

        JsonElement[] graphNodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        Assert.Contains(graphNodes, node =>
            HasPropertyValue(node, "id", "solution:root") &&
            HasPropertyValue(node, "kind", "solution"));
    }

    [Fact]
    public async Task AnalyzeCommandEmitsTrustedSemanticGraphAndCfgMetrics()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("ExpandedMetricsProject");

        var exitCode = await RunCliAsync(
            "analyze",
            projectPath,
            "--output",
            output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        const string metricsSampleId = "type:ExpandedMetricsProject/T:ExpandedMetricsProject.MetricsSample";
        const string cycleAId = "type:ExpandedMetricsProject/T:ExpandedMetricsProject.CycleA";
        const string methodAId = "member:ExpandedMetricsProject/M:ExpandedMetricsProject.MetricsSample.A(System.Int32)";
        const string methodBId = "member:ExpandedMetricsProject/M:ExpandedMetricsProject.MetricsSample.B(System.Int32)";
        const string selfMethodId = "member:ExpandedMetricsProject/M:ExpandedMetricsProject.MetricsSample.Self(System.Int32)";
        const string computeId = "member:ExpandedMetricsProject/M:ExpandedMetricsProject.MetricsSample.ComputeAsync(System.Int32)";

        Assert.Equal(1, MetricInt(metrics, metricsSampleId, "inheritance_depth"));
        Assert.True(MetricInt(metrics, metricsSampleId, "class_coupling") >= 3);
        Assert.Equal(4, MetricInt(metrics, metricsSampleId, "public_api_count"));
        Assert.Equal(2, MetricInt(metrics, metricsSampleId, "documented_public_api_count"));
        Assert.Equal(0.5, MetricDouble(metrics, metricsSampleId, "public_api_documentation_ratio"));

        Assert.Equal(1, MetricInt(metrics, computeId, "allocation_count"));
        Assert.Equal(1, MetricInt(metrics, computeId, "await_count"));
        Assert.True(MetricInt(metrics, computeId, "operation_count") > 0);
        Assert.True(MetricInt(metrics, computeId, "basic_block_count") > 0);
        Assert.Equal(
            MetricInt(metrics, computeId, "basic_block_count"),
            MetricInt(metrics, computeId, "reachable_basic_block_count") +
                MetricInt(metrics, computeId, "unreachable_basic_block_count"));
        Assert.True(MetricInt(metrics, computeId, "cfg_cyclomatic_complexity") >= 1);

        Assert.Equal(1, MetricInt(metrics, methodAId, "outgoing_call_count"));
        Assert.Equal(1, MetricInt(metrics, methodAId, "incoming_call_count"));
        Assert.Equal(2, MetricInt(metrics, methodAId, "recursive_component_size"));
        Assert.Equal(2, MetricInt(metrics, methodBId, "recursive_component_size"));
        Assert.Equal(1, MetricInt(metrics, selfMethodId, "outgoing_call_count"));
        Assert.Equal(1, MetricInt(metrics, selfMethodId, "incoming_call_count"));
        Assert.Equal(1, MetricInt(metrics, selfMethodId, "recursive_component_size"));
        Assert.Equal(2, MetricInt(metrics, cycleAId, "dependency_component_size"));
        Assert.Equal(1, MetricInt(metrics, cycleAId, "transitive_type_dependency_count"));

        JsonElement[] fileLoc = metrics
            .Where(metric =>
                HasPropertyValue(metric, "metricId", "lines_of_code") &&
                HasPropertyValue(metric, "targetKind", "file"))
            .ToArray();
        JsonElement projectNode = Assert.Single(
            ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes"),
            node => HasPropertyValue(node, "kind", "project"));
        var projectTargetId = projectNode.GetProperty("id").GetString()!;
        var fileLocTotal = fileLoc.Sum(metric => metric.GetProperty("numericValue").GetInt32());

        Assert.Equal(fileLocTotal, MetricInt(metrics, projectTargetId, "lines_of_code"));
        Assert.Equal(fileLocTotal, MetricInt(metrics, "solution:root", "lines_of_code"));
    }

    [Fact]
    public async Task AnalyzeCommandEmitsComplexityMetricsAndStableHotspots()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("ComplexityProject");
        const string scoreTargetId = "member:ComplexityProject/M:ComplexityProject.DecisionSamples.Score(ComplexityProject.Order,System.Collections.Generic.IReadOnlyList{System.Int32})";

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--top", "3");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
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
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("BrokenProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] diagnostics = ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson"));

        Assert.Contains(diagnostics, diagnostic => HasPropertyValue(diagnostic, "id", "CS1002") && HasTag(diagnostic, "syntax"));
        Assert.Contains(diagnostics, diagnostic => HasPropertyValue(diagnostic, "id", "CS0246") && HasTag(diagnostic, "compiler"));
    }

    [Fact]
    public async Task AnalyzeCommandEmitsNullableDiagnostics()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("NullableDiagnosticsProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

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
        using var output = TemporaryDirectory.Create();
        var assetsPath = Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets");

        var exitCode = await RunCliAsync(
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

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(2, summary.RootElement.GetProperty("fileCount").GetInt32());
        Assert.DoesNotContain(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("Domain.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandAcceptsSemanticAndNoRestoreOptions()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAsync(
            "analyze",
            projectPath,
            "--output",
            output.Path,
            "--semantic",
            "--no-restore");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
        Assert.Equal("semantic", manifest.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task AnalyzeCommandCanIsolateInputFromAmbientBuildFiles()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path, "--isolate-input");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement analysisHealth = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("trusted", analysisHealth.GetProperty("analysisQuality").GetString());
        Assert.Equal(Path.GetFullPath(projectPath), summary.RootElement.GetProperty("rootPath").GetString());
        Assert.Contains(
            analysisHealth.GetProperty("messages").EnumerateArray(),
            message => message.GetString() == "Input was analyzed from an isolated temporary copy.");
        Assert.DoesNotContain(
            analysisHealth.GetProperty("messages").EnumerateArray(),
            message => message.GetString()!.Contains("Ambient MSBuild", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandRejectsPartialSemanticCoverageAsDegraded()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        CreatePartialCoverageSample(input.Path);

        var exitCode = await RunCliAllowFailureAsync(
            "analyze",
            input.Path,
            "--output",
            output.Path,
            "--no-restore");

        Assert.Equal(CliExitCodes.AnalysisRejected, exitCode);
        using var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement health = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("degraded", health.GetProperty("analysisQuality").GetString());
        Assert.False(health.GetProperty("trustedDiagnostics").GetBoolean());
        Assert.Contains(
            health.GetProperty("messages").EnumerateArray(),
            message => message.GetString()!.Contains(
                "Semantic analysis covered 1 of 2 discovered source files",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandRejectsExternalLinkedSourceAsDegraded()
    {
        using var input = TemporaryDirectory.Create();
        using var outside = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        var linkedSourcePath = Path.Combine(outside.Path, "Linked.cs");
        File.WriteAllText(linkedSourcePath, "internal sealed class Linked { }");
        File.WriteAllText(Path.Combine(input.Path, "Included.cs"), "internal sealed class Included { }");
        File.WriteAllText(
            Path.Combine(input.Path, "LinkedSource.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="{linkedSourcePath}" Link="Linked.cs" />
              </ItemGroup>
            </Project>
            """);

        var exitCode = await RunCliAllowFailureAsync(
            "analyze",
            input.Path,
            "--output",
            output.Path,
            "--no-restore");

        Assert.Equal(CliExitCodes.AnalysisRejected, exitCode);
        using var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement health = summary.RootElement.GetProperty("analysisHealth");
        Assert.False(health.GetProperty("trustedDiagnostics").GetBoolean());
        Assert.Contains(
            health.GetProperty("messages").EnumerateArray(),
            message => message.GetString()!.Contains(
                "authored source file(s) outside the analysis root",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListMetricsAndExplainCommandsExposeMetricCatalog()
    {
        using var listOutput = new StringWriter();
        using var listError = new StringWriter();
        var listExitCode = await CliApplication.RunAsync(["list-metrics"], listOutput, listError, CancellationToken.None);

        using var explainOutput = new StringWriter();
        using var explainError = new StringWriter();
        var explainExitCode = await CliApplication.RunAsync(["explain", "diagnostic_count"], explainOutput, explainError, CancellationToken.None);

        Assert.Equal(0, listExitCode);
        Assert.Equal(0, explainExitCode);
        Assert.Equal(
            50,
            listOutput.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("diagnostic_count@1.0.0", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("cfg_cyclomatic_complexity@1.0.0", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Formula: number of diagnostics whose span overlaps the target", explainOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeCommandEmitsCriticalProjectLoadDiagnostic()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        CreateInvalidProjectSample(input.Path);

        using var commandOutput = new StringWriter();
        using var commandError = new StringWriter();
        var exitCode = await CliApplication.RunAsync(
            ["analyze", input.Path, "--output", output.Path],
            commandOutput,
            commandError,
            CancellationToken.None);

        Assert.Equal(CliExitCodes.AnalysisRejected, exitCode);
        Assert.Contains("--allow-degraded", commandError.ToString(), StringComparison.Ordinal);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] diagnostics = ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson"));

        Assert.Contains(diagnostics, diagnostic =>
            HasPropertyValue(diagnostic, "id", "project_load_failed") &&
            HasPropertyValue(diagnostic, "severity", "critical") &&
            HasTag(diagnostic, "project_load"));

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement analysisHealth = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("degraded", analysisHealth.GetProperty("analysisQuality").GetString());
        Assert.False(analysisHealth.GetProperty("trustedDiagnostics").GetBoolean());
        JsonElement[] degradedHotspots = summary.RootElement
            .GetProperty("hotspots")
            .EnumerateArray()
            .Select(hotspot => hotspot.Clone())
            .ToArray();

        Assert.NotEmpty(degradedHotspots);
        Assert.All(
            degradedHotspots,
            hotspot => Assert.DoesNotContain(
                hotspot.GetProperty("components").EnumerateArray(),
                component => HasPropertyValue(component, "metricId", "diagnostic_count")));

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        Assert.DoesNotContain(metrics, metric => HasPropertyValue(metric, "metricId", "diagnostic_count"));
    }

    [Fact]
    public async Task AnalyzeCommandRejectsEmptySourceSetUnlessExplicitlyAllowed()
    {
        using var input = TemporaryDirectory.Create();
        using var rejectedOutput = TemporaryDirectory.Create();
        using var allowedOutput = TemporaryDirectory.Create();

        using var commandOutput = new StringWriter();
        using var commandError = new StringWriter();
        var rejectedExitCode = await CliApplication.RunAsync(
            ["analyze", input.Path, "--output", rejectedOutput.Path],
            commandOutput,
            commandError,
            CancellationToken.None);
        var allowedExitCode = await RunCliAsync(
            "analyze",
            input.Path,
            "--output",
            allowedOutput.Path,
            "--allow-degraded",
            "--allow-empty");

        Assert.Equal(CliExitCodes.AnalysisRejected, rejectedExitCode);
        Assert.Contains("--allow-empty", commandError.ToString(), StringComparison.Ordinal);
        AssertMandatoryArtifactsExist(rejectedOutput.Path);
        Assert.Equal(CliExitCodes.Success, allowedExitCode);
    }

    [Fact]
    public async Task HelpAndVersionOptionsAreSuccessfulAndUseProductVersion()
    {
        foreach (var helpArgs in new[]
                 {
                     Array.Empty<string>(),
                     new[] { "--help" },
                     new[] { "-h" },
                     new[] { "help" },
                     new[] { "help", "analyze" },
                     new[] { "analyze", "--help" }
                 })
        {
            using var helpOutput = new StringWriter();
            using var helpError = new StringWriter();

            var helpExitCode = await CliApplication.RunAsync(
                helpArgs,
                helpOutput,
                helpError,
                CancellationToken.None);

            Assert.Equal(CliExitCodes.Success, helpExitCode);
            Assert.NotEmpty(helpOutput.ToString());
            Assert.Empty(helpError.ToString());
        }

        using var versionOutput = new StringWriter();
        using var versionError = new StringWriter();
        var versionExitCode = await CliApplication.RunAsync(
            ["--version"],
            versionOutput,
            versionError,
            CancellationToken.None);

        Assert.Equal(CliExitCodes.Success, versionExitCode);
        Assert.Equal(ToolkitInfo.Version + Environment.NewLine, versionOutput.ToString());
        Assert.Empty(versionError.ToString());
    }

    [Fact]
    public async Task ValidateOutputCommandCatchesMissingRequiredArtifact()
    {
        using var output = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(output.Path, "manifest.json"), "{}");

        var exitCode = await RunCliAllowFailureAsync("validate-output", output.Path);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ValidateOutputCommandAcceptsGeneratedArtifacts()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var analyzeExitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);
        var validateExitCode = await RunCliAsync("validate-output", output.Path);

        Assert.Equal(0, analyzeExitCode);
        Assert.Equal(0, validateExitCode);
    }

    [Fact]
    public async Task AnalyzeCommandCompletesMediumRepoSmokeWithinThreshold()
    {
        using var output = TemporaryDirectory.Create();
        var assetsPath = Path.Combine(SchemaAssertions.RepositoryRoot(), "tests", "CodeMetricsToolkit.TestAssets");
        var stopwatch = Stopwatch.StartNew();

        var exitCode = await RunCliAsync("analyze", assetsPath, "--output", output.Path, "--top", "5");

        stopwatch.Stop();

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Analysis took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task SnapshotNormalizerRemovesVolatileManifestFields()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAsync("analyze", projectPath, "--output", output.Path);

        Assert.Equal(0, exitCode);

        var normalizedManifest = ArtifactSnapshotNormalizer.NormalizeJsonFile(Path.Combine(output.Path, "manifest.json"));
        using var normalized = JsonDocument.Parse(normalizedManifest);

        Assert.Equal("<root>", normalized.RootElement.GetProperty("rootPath").GetString());
        Assert.Equal("<timestamp>", normalized.RootElement.GetProperty("startedAt").GetString());
        Assert.Equal(0, normalized.RootElement.GetProperty("durationMs").GetInt32());
    }

    private static async Task<int> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(args, output, error, CancellationToken.None);

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
        using var document = JsonDocument.Parse(File.ReadAllText(artifactPath));

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
                using var document = JsonDocument.Parse(line);

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

    private static int MetricInt(JsonElement[] metrics, string targetId, string metricId)
    {
        return GetMetric(metrics, targetId, metricId)
            .GetProperty("numericValue")
            .GetInt32();
    }

    private static double MetricDouble(JsonElement[] metrics, string targetId, string metricId)
    {
        return GetMetric(metrics, targetId, metricId)
            .GetProperty("numericValue")
            .GetDouble();
    }

    private static void CreateGeneratedFileSample(string rootPath)
    {
        File.WriteAllText(
            Path.Combine(rootPath, "GeneratedSample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
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
                <TargetFramework>net10.0</TargetFramework>
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

    private static void CreatePartialCoverageSample(string rootPath)
    {
        File.WriteAllText(
            Path.Combine(rootPath, "PartialCoverage.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="Excluded.cs" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(rootPath, "Included.cs"),
            "internal sealed class Included { }");
        File.WriteAllText(
            Path.Combine(rootPath, "Excluded.cs"),
            "internal sealed class Excluded { }");
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
