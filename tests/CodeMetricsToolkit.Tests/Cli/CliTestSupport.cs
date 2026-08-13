using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
{
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
