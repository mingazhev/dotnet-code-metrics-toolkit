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
        var facts = SyntaxFactsCollector.Collect(sources, useSemantic: !request.SyntaxOnly, cancellationToken);
        HotspotRanking hotspotRanking = HotspotRanker.Rank(facts, request.Top);
        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(facts)
            .Concat(hotspotRanking.Metrics)
            .ToArray();
        string outputPath = Path.GetFullPath(request.OutputPath);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        await ArtifactWriter.WriteAsync(
            outputPath,
            facts,
            metrics,
            hotspotRanking.Hotspots,
            request.IncludeChunkText,
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
