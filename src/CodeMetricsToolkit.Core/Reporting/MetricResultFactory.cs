using CodeMetricsToolkit.Abstractions;

namespace CodeMetricsToolkit.Core.Reporting;

internal static class MetricResultFactory
{
    public static MetricResultLine Numeric(
        string metricId,
        string metricVersion,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        double value,
        string unit,
        IReadOnlyList<string>? tags = null)
    {
        return new MetricResultLine
        {
            SchemaVersion = ContractVersion.Current,
            MetricId = metricId,
            MetricVersion = metricVersion,
            TargetId = targetId,
            TargetKind = targetKind,
            TargetIdStability = targetIdStability,
            ValueKind = value % 1 == 0 ? "integer" : "number",
            NumericValue = value,
            Unit = unit,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Tags = tags
        };
    }
}
