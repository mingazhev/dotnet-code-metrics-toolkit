using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Facts;

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
        Dictionary<string, FileFacts> uniqueFilesByPath = uniqueFiles
            .ToDictionary(file => file.FilePath, StringComparer.Ordinal);
        var filePathsByProject = facts.GetProjectFileMemberships()
            .GroupBy(membership => membership.ProjectKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(membership => membership.FilePath).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
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
            FileFacts[] projectFiles = filePathsByProject.GetValueOrDefault(projectKey, [])
                .Where(uniqueFilesByPath.ContainsKey)
                .Select(filePath => uniqueFilesByPath[filePath])
                .OrderBy(file => file.FilePath, StringComparer.Ordinal)
                .ToArray();
            AddScopeMetrics(
                metrics,
                "project",
                ProjectIdentity.TargetId(projectPath),
                SyntaxFallbackStability,
                projectPath,
                1,
                1,
                projectFiles,
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
            metrics.Add(MetricResultFactory.Create(
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
        metrics.Add(MetricResultFactory.Create(
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

        metrics.Add(MetricResultFactory.Create(
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
