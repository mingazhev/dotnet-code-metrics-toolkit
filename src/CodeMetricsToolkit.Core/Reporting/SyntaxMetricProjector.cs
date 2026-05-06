using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class SyntaxMetricProjector
{
    public static IReadOnlyList<MetricResultLine> Project(SyntaxAnalysisFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var results = new List<MetricResultLine>();

        foreach (FileFacts file in facts.Files.OrderBy(file => file.FilePath, StringComparer.Ordinal))
        {
            AddNumericMetric(results, "lines_of_code", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, file.LinesOfCode, "lines");
            AddNumericMetric(results, "non_comment_lines_of_code", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, file.NonCommentLinesOfCode, "lines");
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            AddNumericMetric(results, "lines_of_code", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.LinesOfCode, "lines");
            AddNumericMetric(results, "non_comment_lines_of_code", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.NonCommentLinesOfCode, "lines");
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            AddNumericMetric(results, "method_length", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.MethodLength, "lines");
            AddNumericMetric(results, "parameter_count", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ParameterCount, "count");
        }

        return results;
    }

    private static void AddNumericMetric(
        List<MetricResultLine> results,
        string metricId,
        string metricVersion,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        int value,
        string unit)
    {
        results.Add(new MetricResultLine
        {
            SchemaVersion = ContractVersion.Current,
            MetricId = metricId,
            MetricVersion = metricVersion,
            TargetId = targetId,
            TargetKind = targetKind,
            TargetIdStability = targetIdStability,
            ValueKind = "integer",
            NumericValue = value,
            Unit = unit,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine
        });
    }
}
