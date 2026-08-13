using System.Diagnostics;
using System.Text.Json;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Core;
using CodeMetricsToolkit.Tests.SchemaValidation;
using CodeMetricsToolkit.Tests.Snapshots;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
{
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
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Analysis took {stopwatch.Elapsed}.");
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
}
