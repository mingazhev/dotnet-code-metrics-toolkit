using System.Diagnostics;
using CodeMetricsToolkit.Cli;
using CodeMetricsToolkit.Core.Syntax;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class DotnetRestoreRunnerTests
{
    [Fact]
    public async Task RunProcessAsyncDrainsStandardOutputAndErrorBeforeReturning()
    {
        DotnetRestoreRunner.ProcessResult result = await DotnetRestoreRunner.RunProcessAsync(
            "dotnet",
            [typeof(CliApplication).Assembly.Location, "list-metrics"],
            SchemaAssertions.RepositoryRoot(),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("cyclomatic_complexity", result.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task RunProcessAsyncCancelsAndStopsProcessWithContinuousOutput()
    {
        var projectPath = Path.Combine(
            SchemaAssertions.RepositoryRoot(),
            "tests",
            "CodeMetricsToolkit.TestAssets",
            "ProcessProbe",
            "ProcessProbe.csproj");
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromSeconds(3));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DotnetRestoreRunner.RunProcessAsync(
                "dotnet",
                ["run", "--project", projectPath],
                SchemaAssertions.RepositoryRoot(),
                cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }
}
