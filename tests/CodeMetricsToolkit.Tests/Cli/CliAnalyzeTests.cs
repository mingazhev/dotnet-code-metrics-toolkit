using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Tests.SchemaValidation;

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
        Assert.Contains(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("\"metricId\":\"method_length\"", StringComparison.Ordinal));
        Assert.Contains(
            File.ReadLines(Path.Combine(output.Path, "metrics.ndjson")),
            line => line.Contains("\"targetIdStability\":\"syntax_fallback\"", StringComparison.Ordinal));
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

    private static async Task<int> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await CliApplication.RunAsync(args, output, error, CancellationToken.None);

        Assert.True(exitCode == 0, error.ToString());
        return exitCode;
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
