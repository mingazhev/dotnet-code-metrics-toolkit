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
        using IsolatedInput? isolatedInput = request.IsolateInput
            ? InputIsolator.CopyToTemporaryDirectory(request.InputPath)
            : null;
        string inputPath = isolatedInput?.IsolatedRootPath ?? request.InputPath;
        DiscoveredSources sources = SourceFileDiscovery.Discover(
            inputPath,
            request.IncludeGeneratedCode,
            request.IncludePatterns,
            request.ExcludePatterns);
        var facts = await SyntaxFactsCollector.CollectAsync(
                sources,
                useSemantic: !request.SyntaxOnly,
                noRestore: request.NoRestore,
                cancellationToken)
            .ConfigureAwait(false);
        HotspotRanking hotspotRanking = HotspotRanker.Rank(facts, request.Top, cancellationToken);
        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(facts, cancellationToken)
            .Concat(GraphMetricProjector.Project(facts, cancellationToken))
            .Concat(DiagnosticMetricProjector.Project(facts, cancellationToken))
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
                DiagnosticCount = facts.Diagnostics.Count,
                Health = facts.Health
            }
        };
    }
}
