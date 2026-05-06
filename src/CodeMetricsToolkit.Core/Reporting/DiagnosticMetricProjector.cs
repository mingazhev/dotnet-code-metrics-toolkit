using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class DiagnosticMetricProjector
{
    public static IReadOnlyList<MetricResultLine> Project(SyntaxAnalysisFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var metrics = new List<MetricResultLine>();

        foreach (FileFacts file in facts.Files.OrderBy(file => file.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddDiagnosticMetrics(metrics, facts.Diagnostics, "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine);
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddDiagnosticMetrics(metrics, facts.Diagnostics, "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine);
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddDiagnosticMetrics(metrics, facts.Diagnostics, "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine);
        }

        return metrics;
    }

    private static void AddDiagnosticMetrics(
        List<MetricResultLine> metrics,
        IReadOnlyList<AnalysisDiagnostic> diagnostics,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine)
    {
        AnalysisDiagnostic[] matchingDiagnostics = diagnostics
            .Where(diagnostic => OverlapsTarget(diagnostic, filePath, startLine, endLine))
            .ToArray();

        metrics.Add(CreateMetric(targetKind, targetId, targetIdStability, filePath, startLine, endLine, matchingDiagnostics.Length, null));

        foreach (IGrouping<string, AnalysisDiagnostic> tagGroup in matchingDiagnostics
            .SelectMany(diagnostic => (diagnostic.Tags ?? []).Select(tag => new { Tag = tag, Diagnostic = diagnostic }))
            .GroupBy(entry => entry.Tag, entry => entry.Diagnostic, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            metrics.Add(CreateMetric(targetKind, targetId, targetIdStability, filePath, startLine, endLine, tagGroup.Count(), [tagGroup.Key]));
        }
    }

    private static MetricResultLine CreateMetric(
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        int count,
        IReadOnlyList<string>? tags)
    {
        return MetricResultFactory.Numeric(
            "diagnostic_count",
            "1.0.0",
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            count,
            "count",
            tags);
    }

    private static bool OverlapsTarget(AnalysisDiagnostic diagnostic, string filePath, int startLine, int endLine)
    {
        if (!string.Equals(diagnostic.FilePath, filePath, StringComparison.Ordinal))
        {
            return false;
        }

        if (diagnostic.StartLine is null || diagnostic.EndLine is null)
        {
            return true;
        }

        return diagnostic.StartLine <= endLine && diagnostic.EndLine >= startLine;
    }
}
