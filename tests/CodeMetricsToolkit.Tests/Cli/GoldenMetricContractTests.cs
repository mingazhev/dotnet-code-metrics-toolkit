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
            ["analyze", projectPath, "--output", output.Path],
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
        Assert.Equal(
            expected
                .Concat(scopeExpectations)
                .Select(expectation => expectation.GetProperty("targetId").GetString())
                .Distinct()
                .Order(),
            expectedTargets.Select(target => target.GetProperty("targetId").GetString()).Order());
        Assert.Equal(
            ["file", "member", "project", "solution", "type"],
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
