using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Metrics;

namespace CodeMetricsToolkit.Core.Reporting;

internal static class MetricResultFactory
{
    public static MetricResultLine Numeric(
        string metricId,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        double value,
        IReadOnlyList<string>? tags = null)
    {
        return Create(metricId, targetKind, targetId, targetIdStability, filePath, startLine, endLine, value,
            value % 1 == 0 ? "integer" : "number", tags);
    }

    public static MetricResultLine Number(
        string metricId,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        double value,
        IReadOnlyList<string>? tags = null)
    {
        return Create(metricId, targetKind, targetId, targetIdStability, filePath, startLine, endLine, value, "number", tags);
    }

    private static MetricResultLine Create(
        string metricId,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        double value,
        string valueKind,
        IReadOnlyList<string>? tags)
    {
        MetricDescriptor descriptor = MetricCatalog.Find(metricId)
            ?? throw new ArgumentException($"Unknown metric '{metricId}'.", nameof(metricId));
        if (!descriptor.TargetKinds.Contains(targetKind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Metric '{metricId}' does not support target kind '{targetKind}'.",
                nameof(targetKind));
        }

        return new MetricResultLine
        {
            SchemaVersion = ContractVersion.Current,
            MetricId = descriptor.Id,
            MetricVersion = descriptor.Version,
            TargetId = targetId,
            TargetKind = targetKind,
            TargetIdStability = targetIdStability,
            ValueKind = valueKind,
            NumericValue = value,
            Unit = descriptor.Unit,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Tags = tags
        };
    }
}
