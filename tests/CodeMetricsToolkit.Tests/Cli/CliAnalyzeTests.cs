using System.Text.Json;
using CodeMetricsToolkit.Core;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
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
            line => line.Contains("\"metricId\":\"member_length\"", StringComparison.Ordinal));
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
    public async Task AnalyzeCommandUsesEvaluatedCompileItemsForSharedProjectSources()
    {
        using var output = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(
            TestAssetPath("SharedSourceSolution"),
            "SharedSourceSolution.sln");

        var exitCode = await RunCliAsync(
            "analyze",
            solutionPath,
            "--output",
            output.Path,
            "--no-restore");

        Assert.Equal(0, exitCode);
        SchemaAssertions.OutputDirectoryValidates(output.Path);

        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        JsonElement[] nodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        JsonElement[] edges = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "edges");
        JsonElement projectA = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "project") &&
            HasPropertyValue(node, "name", "ProjectA"));
        JsonElement projectB = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "project") &&
            HasPropertyValue(node, "name", "ProjectB"));
        var projectAId = projectA.GetProperty("id").GetString()!;
        var projectBId = projectB.GetProperty("id").GetString()!;
        const string sharedFileId = "file:Shared.cs";

        Assert.Equal(6, MetricInt(metrics, projectAId, "lines_of_code"));
        Assert.Equal(6, MetricInt(metrics, projectBId, "lines_of_code"));
        Assert.Equal(9, MetricInt(metrics, "solution:root", "lines_of_code"));
        Assert.Equal(3, MetricInt(metrics, sharedFileId, "lines_of_code"));
        Assert.Equal(2, MetricInt(metrics, projectAId, "public_api_count"));
        Assert.Equal(2, MetricInt(metrics, projectBId, "public_api_count"));
        Assert.DoesNotContain(metrics, metric =>
            metric.TryGetProperty("filePath", out JsonElement filePath) &&
            string.Equals(filePath.GetString(), "ProjectA/Removed.cs", StringComparison.Ordinal));

        Assert.Single(nodes, node => HasPropertyValue(node, "id", sharedFileId));
        Assert.Contains(edges, edge =>
            HasPropertyValue(edge, "from", projectAId) &&
            HasPropertyValue(edge, "to", sharedFileId) &&
            HasPropertyValue(edge, "kind", "contains"));
        Assert.Contains(edges, edge =>
            HasPropertyValue(edge, "from", projectBId) &&
            HasPropertyValue(edge, "to", sharedFileId) &&
            HasPropertyValue(edge, "kind", "contains"));

        using var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(3, summary.RootElement.GetProperty("fileCount").GetInt32());
        Assert.Equal("trusted", summary.RootElement
            .GetProperty("analysisHealth")
            .GetProperty("analysisQuality")
            .GetString());
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
            metric => HasPropertyValue(metric, "metricId", "type_coupling"));
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
        Assert.Equal(0, GetMetric(metrics, orderServiceId, "dependency_cycle_membership").GetProperty("numericValue").GetInt32());
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
        Assert.DoesNotContain(metrics, metric => HasPropertyValue(metric, "metricId", "type_coupling"));
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
        Assert.Equal(4, MetricInt(metrics, fileTargetId, "token_line_count"));
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
        Assert.True(MetricInt(metrics, metricsSampleId, "type_coupling") >= 3);
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

        Assert.Equal(1, MetricInt(metrics, methodAId, "distinct_outgoing_callee_count"));
        Assert.Equal(1, MetricInt(metrics, methodAId, "distinct_incoming_caller_count"));
        Assert.Equal(2, MetricInt(metrics, methodAId, "recursive_component_size"));
        Assert.Equal(2, MetricInt(metrics, methodBId, "recursive_component_size"));
        Assert.Equal(1, MetricInt(metrics, selfMethodId, "distinct_outgoing_callee_count"));
        Assert.Equal(1, MetricInt(metrics, selfMethodId, "distinct_incoming_caller_count"));
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

        JsonElement methodLength = Assert.Single(components, component => HasPropertyValue(component, "metricId", "member_length"));
        JsonElement cyclomatic = Assert.Single(components, component => HasPropertyValue(component, "metricId", "cyclomatic_complexity"));
        Assert.Equal(0.15, methodLength.GetProperty("weight").GetDouble());
        Assert.True(cyclomatic.GetProperty("weight").GetDouble() > methodLength.GetProperty("weight").GetDouble());
        Assert.Contains(components, component => HasPropertyValue(component, "metricId", "diagnostic_count"));
    }
}
