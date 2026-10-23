using System.Text.Json;
using CodeMetricsToolkit.Core.Analysis;
using CodeMetricsToolkit.Cli;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
{
    [Fact]
    public async Task AnalyzeRecursiveGenericConstraintsCompletesWithTrustedCoupling()
    {
        using var output = TemporaryDirectory.Create();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        // A stack-overflow regression must not terminate the test runner.
        DotnetRestoreRunner.ProcessResult result = await DotnetRestoreRunner.RunProcessAsync(
            DotnetMuxer.Resolve(),
            [typeof(CliApplication).Assembly.Location, "analyze",
                TestAssetPath("RecursiveConstraintsProject"), "--output", output.Path],
            Path.GetTempPath(),
            cancellation.Token);

        Assert.True(result.ExitCode == 0, result.StandardError);
        AssertMandatoryArtifactsExist(output.Path);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        JsonElement health = summary.RootElement.GetProperty("analysisHealth");
        Assert.Equal("trusted", health.GetProperty("analysisQuality").GetString());
        Assert.True(health.GetProperty("trustedDiagnostics").GetBoolean());
        JsonElement coupling = Assert.Single(ReadNdjson(Path.Combine(output.Path, "metrics.ndjson")),
            metric => HasPropertyValue(metric, "metricId", "type_coupling"));
        Assert.Equal(1, coupling.GetProperty("numericValue").GetInt32());
    }
}
