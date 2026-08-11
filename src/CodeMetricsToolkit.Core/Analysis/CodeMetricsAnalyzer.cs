using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
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
        IsolatedInput? isolatedInput = request.IsolateInput
            ? InputIsolator.CopyToTemporaryDirectory(request.InputPath, cancellationToken)
            : null;
        var inputPath = isolatedInput?.IsolatedInputPath ?? request.InputPath;
        var warnings = new List<string>();

        try
        {
            DiscoveredSources sources = SourceFileDiscovery.Discover(
                inputPath,
                request.IncludeGeneratedCode,
                request.IncludePatterns,
                request.ExcludePatterns,
                cancellationToken);
            var reportedRootPath = isolatedInput?.OriginalRootPath ?? sources.RootPath;
            MSBuildRegistration.RegistrationResult registration = request.SyntaxOnly
                ? MSBuildRegistration.RegistrationResult.Available
                : MSBuildRegistration.EnsureRegistered(sources.RootPath);
            SyntaxAnalysisFacts facts = await SyntaxFactsCollector.CollectAsync(
                    sources,
                    useSemantic: !request.SyntaxOnly && registration.IsAvailable,
                    noRestore: request.NoRestore,
                    cancellationToken,
                    registration.FailureMessage)
                .ConfigureAwait(false);

            if (isolatedInput is not null)
            {
                facts = facts with
                {
                    Health = facts.Health with
                    {
                        Messages = facts.Health.Messages
                            .Append("Input was analyzed from an isolated temporary copy.")
                            .ToArray()
                    }
                };
            }

            HotspotRanking hotspotRanking = HotspotRanker.Rank(facts, request.Top, cancellationToken);
            MetricResultLine[] metrics = SyntaxMetricProjector.Project(facts, cancellationToken)
                .Concat(GraphMetricProjector.Project(facts, cancellationToken))
                .Concat(DiagnosticMetricProjector.Project(facts, cancellationToken))
                .Concat(hotspotRanking.Metrics)
                .ToArray();
            var outputPath = Path.GetFullPath(request.OutputPath);
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;

            await ArtifactWriter.WriteAsync(
                outputPath,
                facts,
                reportedRootPath,
                request,
                sources,
                metrics,
                hotspotRanking.Hotspots,
                request.IncludeChunkText,
                startedAt,
                completedAt,
                cancellationToken).ConfigureAwait(false);

            if (isolatedInput is not null)
            {
                if (!isolatedInput.TryDispose(out var cleanupFailure))
                {
                    warnings.Add(cleanupFailure!);
                }

                isolatedInput = null;
            }

            return new AnalysisRunResult
            {
                OutputPath = outputPath,
                Warnings = warnings,
                Summary = new AnalysisSummary
                {
                    RootPath = reportedRootPath,
                    ProjectCount = facts.ProjectPaths.Count,
                    FileCount = facts.Files.Count,
                    TypeCount = facts.Types.Count,
                    MemberCount = facts.Members.Count,
                    MetricResultCount = metrics.Length,
                    DiagnosticCount = facts.Diagnostics.Count,
                    Health = facts.Health
                }
            };
        }
        finally
        {
            if (isolatedInput is not null && !isolatedInput.TryDispose(out var cleanupFailure))
            {
                System.Diagnostics.Trace.TraceWarning(cleanupFailure);
            }
        }
    }
}
