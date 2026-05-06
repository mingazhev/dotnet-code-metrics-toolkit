using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Core.Analysis;

public static class CodeMetricsAnalyzer
{
    public static async Task<AnalysisRunResult> AnalyzeAsync(
        AnalyzeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        DiscoveredSources sources = SourceFileDiscovery.Discover(request.InputPath, request.IncludeGeneratedCode);
        var facts = SyntaxFactsCollector.Collect(sources, cancellationToken);
        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(facts);
        string outputPath = Path.GetFullPath(request.OutputPath);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        await ArtifactWriter.WriteAsync(
            outputPath,
            facts,
            metrics,
            startedAt,
            completedAt,
            cancellationToken).ConfigureAwait(false);

        return new AnalysisRunResult
        {
            OutputPath = outputPath,
            Summary = new AnalysisSummary
            {
                RootPath = facts.RootPath,
                ProjectCount = facts.ProjectPaths.Count,
                FileCount = facts.Files.Count,
                TypeCount = facts.Types.Count,
                MemberCount = facts.Members.Count,
                MetricResultCount = metrics.Count,
                DiagnosticCount = facts.Diagnostics.Count
            }
        };
    }
}
