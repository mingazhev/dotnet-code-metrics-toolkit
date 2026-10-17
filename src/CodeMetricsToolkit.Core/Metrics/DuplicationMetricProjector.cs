using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;

namespace CodeMetricsToolkit.Core.Metrics;

internal static class DuplicationMetricProjector
{
    private const string SyntaxFallbackStability = "syntax_fallback";

    public static IReadOnlyList<MetricResultLine> Project(
        AnalysisFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        cancellationToken.ThrowIfCancellationRequested();
        if (facts.Duplication is null)
        {
            return [];
        }

        var metrics = new List<MetricResultLine>();
        FileFacts[] uniqueFiles = facts.Files
            .GroupBy(file => file.FilePath, StringComparer.Ordinal)
            .Select(group => group.OrderBy(file => file.TargetId, StringComparer.Ordinal).First())
            .OrderBy(file => file.FilePath, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyDictionary<string, DuplicationFileFacts> duplicationByPath =
            facts.Duplication.Files
            .GroupBy(file => file.FilePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        AddScopeMetrics(
            metrics,
            "solution",
            TargetIds.SolutionRoot,
            SyntaxFallbackStability,
            ".",
            1,
            1,
            uniqueFiles,
            duplicationByPath,
            cancellationToken);

        foreach (var projectPath in facts.ProjectPaths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectKey = ProjectIdentity.Key(projectPath);
            var projectFiles = facts.GetProjectFileMemberships()
                .Where(membership => membership.ProjectKey == projectKey)
                .Select(membership => membership.FilePath)
                .ToHashSet(StringComparer.Ordinal);
            AddScopeMetrics(
                metrics,
                "project",
                ProjectIdentity.TargetId(projectPath),
                SyntaxFallbackStability,
                projectPath,
                1,
                1,
                uniqueFiles.Where(file => projectFiles.Contains(file.FilePath)).ToArray(),
                duplicationByPath,
                cancellationToken);
        }

        foreach (FileFacts file in uniqueFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DuplicationFileFacts duplication = duplicationByPath.GetValueOrDefault(file.FilePath)
                ?? new DuplicationFileFacts(file.FilePath, [], 0);
            AddFileMetrics(metrics, file, duplication);
        }

        return metrics;
    }

    private static void AddScopeMetrics(
        List<MetricResultLine> metrics,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        IReadOnlyList<FileFacts> files,
        IReadOnlyDictionary<string, DuplicationFileFacts> duplicationByPath,
        CancellationToken cancellationToken)
    {
        long blockCount = 0;
        long duplicatedLineCount = 0;
        long linesOfCode = 0;

        foreach (FileFacts file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DuplicationFileFacts duplication = duplicationByPath.GetValueOrDefault(file.FilePath)
                ?? new DuplicationFileFacts(file.FilePath, [], 0);
            blockCount += duplication.Regions.Count;
            duplicatedLineCount += duplication.DuplicatedLineCount;
            linesOfCode += file.LinesOfCode;
        }

        AddNumericMetric(
            metrics,
            "duplicated_block_count",
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            blockCount);
        AddNumericMetric(
            metrics,
            "duplicated_line_count",
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            duplicatedLineCount);
        AddRatioMetric(
            metrics,
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            duplicatedLineCount,
            linesOfCode);
    }

    private static void AddFileMetrics(
        List<MetricResultLine> metrics,
        FileFacts file,
        DuplicationFileFacts duplication)
    {
        AddNumericMetric(
            metrics,
            "duplicated_block_count",
            "file",
            file.TargetId,
            file.TargetIdStability,
            file.FilePath,
            file.StartLine,
            file.EndLine,
            duplication.Regions.Count);
        AddNumericMetric(
            metrics,
            "duplicated_line_count",
            "file",
            file.TargetId,
            file.TargetIdStability,
            file.FilePath,
            file.StartLine,
            file.EndLine,
            duplication.DuplicatedLineCount);
        AddRatioMetric(
            metrics,
            "file",
            file.TargetId,
            file.TargetIdStability,
            file.FilePath,
            file.StartLine,
            file.EndLine,
            duplication.DuplicatedLineCount,
            file.LinesOfCode);

        foreach (DuplicationRegionFacts region in duplication.Regions)
        {
            metrics.Add(MetricResultFactory.Numeric(
                "duplicated_block_count",
                "file",
                file.TargetId,
                file.TargetIdStability,
                file.FilePath,
                region.StartLine,
                region.EndLine,
                1,
                [region.Tag]));
        }
    }

    private static void AddNumericMetric(
        List<MetricResultLine> metrics,
        string metricId,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        double value)
    {
        metrics.Add(MetricResultFactory.Numeric(
            metricId,
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            value));
    }

    private static void AddRatioMetric(
        List<MetricResultLine> metrics,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        long duplicatedLineCount,
        long linesOfCode)
    {
        if (linesOfCode == 0)
        {
            return;
        }

        metrics.Add(MetricResultFactory.Number(
            "duplicated_line_ratio",
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            (double)duplicatedLineCount / linesOfCode));
    }
}
