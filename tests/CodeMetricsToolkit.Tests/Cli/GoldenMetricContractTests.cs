using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Validation;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed class GoldenMetricContractTests
{
    [Fact]
    public async Task AnalyzeReadyProjectMatchesExactExpectationForEveryCatalogMetric()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = Path.Combine(
            SchemaAssertions.RepositoryRoot(),
            "tests",
            "CodeMetricsToolkit.TestAssets",
            "ExpandedMetricsProject");
        var expectationPath = Path.Combine(projectPath, "expected-metrics.json");
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["analyze", projectPath, "--output", output.Path, "--isolate-input"],
            standardOutput,
            standardError,
            CancellationToken.None);

        Assert.True(exitCode == 0, standardError.ToString());
        SchemaAssertions.OutputDirectoryValidates(output.Path);
        OutputValidationResult validation = OutputValidator.Validate(
            output.Path,
            CancellationToken.None);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));

        JsonElement[] actual = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        using var expectationsDocument = JsonDocument.Parse(File.ReadAllText(expectationPath));
        JsonElement[] expected = expectationsDocument.RootElement
            .GetProperty("expectations")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
        JsonElement[] expectedTargets = expectationsDocument.RootElement
            .GetProperty("targets")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
        JsonElement[] scopeExpectations = expectationsDocument.RootElement
            .GetProperty("scopeExpectations")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
        JsonElement[] graphNodes = ReadJsonArray(
            Path.Combine(output.Path, "graph.json"),
            "nodes");
        var catalogIds = MetricCatalog.All
            .Select(metric => metric.Id)
            .ToHashSet(StringComparer.Ordinal);
        var expectedIds = expected
            .Select(expectation => expectation.GetProperty("metricId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var actualIds = actual
            .Select(metric => metric.GetProperty("metricId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(MetricCatalog.All.Count, expected.Length);
        Assert.Equal(catalogIds.Order(StringComparer.Ordinal), expectedIds.Order(StringComparer.Ordinal));
        Assert.Equal(catalogIds.Order(StringComparer.Ordinal), actualIds.Order(StringComparer.Ordinal));
        foreach (MetricDescriptor metric in MetricCatalog.All)
        {
            JsonElement catalogExpectation = Assert.Single(
                expected,
                expectation => string.Equals(
                    expectation.GetProperty("metricId").GetString(),
                    metric.Id,
                    StringComparison.Ordinal));
            Assert.Equal(metric.Version, catalogExpectation.GetProperty("metricVersion").GetString());
            Assert.Equal(metric.Unit, catalogExpectation.GetProperty("unit").GetString());
            Assert.Equal(metric.ValueKind, catalogExpectation.GetProperty("valueKind").GetString());
            Assert.Contains(
                catalogExpectation.GetProperty("targetKind").GetString(),
                metric.TargetKinds);
            Assert.Equal(
                metric.TargetKinds.Order(StringComparer.Ordinal),
                actual.Where(row => row.GetProperty("metricId").GetString() == metric.Id)
                    .Select(row => row.GetProperty("targetKind").GetString())
                    .Distinct().Order(StringComparer.Ordinal));
        }
        Assert.Equal(
            expected
                .Concat(scopeExpectations)
                .Select(expectation => expectation.GetProperty("targetId").GetString())
                .Distinct()
                .Order(),
            expectedTargets.Select(target => target.GetProperty("targetId").GetString()).Order());
        Assert.Equal(
            ["file", "member", "namespace", "project", "solution", "type"],
            expectedTargets
                .Select(target => target.GetProperty("targetKind").GetString())
                .Distinct()
                .Order());

        foreach (JsonElement expectation in expected.Concat(scopeExpectations))
        {
            JsonElement metric = Assert.Single(actual, candidate =>
                SameString(candidate, expectation, "metricId") &&
                SameString(candidate, expectation, "targetId") &&
                !candidate.TryGetProperty("tags", out _));

            Assert.Equal(
                expectation.GetProperty("metricVersion").GetString(),
                metric.GetProperty("metricVersion").GetString());
            Assert.Equal(
                expectation.GetProperty("targetKind").GetString(),
                metric.GetProperty("targetKind").GetString());
            Assert.Equal(
                expectation.GetProperty("valueKind").GetString(),
                metric.GetProperty("valueKind").GetString());
            Assert.Equal(
                expectation.GetProperty("unit").GetString(),
                metric.GetProperty("unit").GetString());
            Assert.Equal(
                expectation.GetProperty("numericValue").GetDouble(),
                metric.GetProperty("numericValue").GetDouble(),
                12);
        }

        foreach (JsonElement expectedTarget in expectedTargets)
        {
            var targetId = expectedTarget.GetProperty("targetId").GetString()!;
            JsonElement graphNode = Assert.Single(
                graphNodes,
                candidate => candidate.GetProperty("id").GetString() == targetId);
            AssertTargetMetadata(expectedTarget, graphNode);

            JsonElement[] targetMetrics = actual
                .Where(metric => metric.GetProperty("targetId").GetString() == targetId)
                .ToArray();
            Assert.NotEmpty(targetMetrics);
            Assert.All(targetMetrics, metric => AssertTargetMetadata(expectedTarget, metric));
        }

        AssertRelationships(actual);
    }

    private static void AssertRelationships(IEnumerable<JsonElement> rows)
    {
        (string Ratio, string Numerator, string Denominator)[] ratios =
        [
            ("token_line_ratio", "token_line_count", "lines_of_code"),
            ("blank_line_ratio", "blank_line_count", "lines_of_code"),
            ("comment_only_line_ratio", "comment_only_line_count", "lines_of_code"),
            ("commented_line_ratio", "commented_line_count", "lines_of_code"),
            ("documentation_comment_ratio", "documentation_comment_line_count", "lines_of_code"),
            ("duplicated_line_ratio", "duplicated_line_count", "lines_of_code"),
            ("public_api_documentation_ratio", "documented_public_api_count", "public_api_count")
        ];
        foreach (IGrouping<string, JsonElement> target in rows
                     .Where(row => !row.TryGetProperty("tags", out _))
                     .GroupBy(row => row.GetProperty("targetId").GetString()!))
        {
            var values = target.ToDictionary(
                row => row.GetProperty("metricId").GetString()!,
                row => row.GetProperty("numericValue").GetDouble());
            foreach ((var ratio, var numerator, var denominator) in ratios)
            {
                if (values.TryGetValue(ratio, out var value))
                {
                    Assert.True(values[denominator] > 0);
                    Assert.InRange(value, 0, 1);
                    Assert.Equal(values[numerator] / values[denominator], value, 12);
                }
            }

            if (values.TryGetValue("cyclomatic_complexity", out var cyclomatic))
            {
                Assert.Equal(1 + values["decision_point_count"], cyclomatic);
            }
            if (values.TryGetValue("basic_block_count", out var blocks))
            {
                Assert.Equal(blocks, values["reachable_basic_block_count"] + values["unreachable_basic_block_count"]);
            }
            if (values.TryGetValue("cfg_cyclomatic_complexity", out var cfgComplexity))
            {
                Assert.Equal(Math.Max(1, values["control_flow_edge_count"] - blocks + (2 * values["control_flow_graph_count"])), cfgComplexity);
            }
            if (values.TryGetValue("commented_line_count", out var comments))
            {
                Assert.Equal(comments, values["comment_only_line_count"] + values["mixed_code_comment_line_count"]);
                Assert.InRange(values["documentation_comment_line_count"], 0, comments);
            }
            foreach (var prefix in new[] { "", "project_", "namespace_" })
            {
                if (values.TryGetValue(prefix + "dependency_component_size", out var size))
                {
                    Assert.Equal(size > 0 ? 1 : 0, values[prefix + "dependency_cycle_membership"]);
                }
            }
        }
    }

    private static void AssertTargetMetadata(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(
            expected.GetProperty("targetKind").GetString(),
            actual.GetProperty(actual.TryGetProperty("kind", out _) ? "kind" : "targetKind").GetString());
        Assert.Equal(
            expected.GetProperty("targetIdStability").GetString(),
            actual.GetProperty("targetIdStability").GetString());
        Assert.Equal(
            expected.GetProperty("filePath").GetString(),
            actual.GetProperty("filePath").GetString());
        Assert.Equal(
            expected.GetProperty("startLine").GetInt32(),
            actual.GetProperty("startLine").GetInt32());
        Assert.Equal(
            expected.GetProperty("endLine").GetInt32(),
            actual.GetProperty("endLine").GetInt32());
    }

    private static bool SameString(
        JsonElement actual,
        JsonElement expected,
        string propertyName)
    {
        return string.Equals(
            actual.GetProperty(propertyName).GetString(),
            expected.GetProperty(propertyName).GetString(),
            StringComparison.Ordinal);
    }

    private static JsonElement[] ReadNdjson(string path)
    {
        return File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();
    }

    private static JsonElement[] ReadJsonArray(string path, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement
            .GetProperty(propertyName)
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
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
                "codemetrics-golden-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);

            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
