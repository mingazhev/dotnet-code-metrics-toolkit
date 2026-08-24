using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
{
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
    public async Task AnalyzeCommandHonorsCompileRemoveWithoutDegradingAnalysis()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        CreatePartialCoverageSample(input.Path);

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
        Assert.True(health.GetProperty("trustedDiagnostics").GetBoolean());
        Assert.DoesNotContain(
            ReadNdjson(Path.Combine(output.Path, "metrics.ndjson")),
            metric => metric.TryGetProperty("filePath", out JsonElement filePath) &&
                string.Equals(filePath.GetString(), "Excluded.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeCommandKeepsValidProjectWithNoCompileItemsEmpty()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(input.Path, "EmptyProject.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Remove="Only.cs" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(input.Path, "Only.cs"),
            "internal sealed class MustRemainExcluded { }");

        var exitCode = await RunCliAsync(
            "analyze",
            input.Path,
            "--output",
            output.Path,
            "--no-restore",
            "--allow-empty");

        Assert.Equal(0, exitCode);
        using var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(0, summary.RootElement.GetProperty("fileCount").GetInt32());
        Assert.Equal(0, summary.RootElement.GetProperty("typeCount").GetInt32());
        Assert.Equal(0, summary.RootElement.GetProperty("memberCount").GetInt32());
        Assert.Equal(
            "trusted",
            summary.RootElement
                .GetProperty("analysisHealth")
                .GetProperty("analysisQuality")
                .GetString());
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "manifest.json")));
        Assert.Equal("semantic", manifest.RootElement.GetProperty("mode").GetString());
        Assert.DoesNotContain(
            ReadNdjson(Path.Combine(output.Path, "metrics.ndjson")),
            metric => metric.TryGetProperty("filePath", out JsonElement filePath) &&
                string.Equals(filePath.GetString(), "Only.cs", StringComparison.Ordinal));
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
    public async Task EmptyIncludeGlobIsAnInputError()
    {
        using var output = TemporaryDirectory.Create();
        var projectPath = TestAssetPath("SimpleProject");

        var exitCode = await RunCliAllowFailureAsync(
            "analyze",
            projectPath,
            "--output",
            output.Path,
            "--include",
            "");

        Assert.Equal(CliExitCodes.InputError, exitCode);
    }

    [Fact]
    public async Task IsolateInputQuotaFailuresUseInputErrorExitCode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(input.Path, "Included.cs"), "internal sealed class Included { }");
        Assert.Equal(0, MkFifo(Path.Combine(input.Path, "named-pipe"), Convert.ToInt32("644", 8)));

        var exitCode = await RunCliAllowFailureAsync(
            "analyze",
            input.Path,
            "--output",
            output.Path,
            "--syntax-only",
            "--isolate-input",
            "--allow-empty");

        Assert.Equal(CliExitCodes.InputError, exitCode);
    }

    [Fact]
    public async Task AnalyzeCommandWarnsAboutAmbientBuildFilesWithoutIsolation()
    {
        using var parent = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(parent.Path, "Directory.Build.props"),
            "<Project />");
        var child = Path.Combine(parent.Path, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(
            Path.Combine(child, "Child.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(child, "Child.cs"), "internal sealed class Child { }");

        var exitCode = await RunCliAllowFailureAsync(
            "analyze",
            child,
            "--output",
            output.Path,
            "--no-restore",
            "--allow-degraded");

        Assert.True(exitCode is 0 or 3, $"unexpected exit {exitCode}");
        using var summary = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Contains(
            summary.RootElement.GetProperty("analysisHealth").GetProperty("messages").EnumerateArray(),
            message => message.GetString()!.Contains("Ambient MSBuild/NuGet file", StringComparison.Ordinal));
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true, EntryPoint = "mkfifo", CharSet = System.Runtime.InteropServices.CharSet.Ansi, BestFitMapping = false)]
    private static extern int MkFifo(string path, int mode);
}
