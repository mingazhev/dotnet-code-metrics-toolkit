using CodeMetricsToolkit.Core.Contracts;

namespace CodeMetricsToolkit.Core.Metrics;

internal static class MetricResultFactory
{
    public static MetricResultLine Create(
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
        MetricDescriptor descriptor = MetricCatalog.Find(metricId)
            ?? throw new ArgumentException($"Unknown metric '{metricId}'.", nameof(metricId));
        if (!descriptor.TargetKinds.Contains(targetKind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Metric '{metricId}' does not support target kind '{targetKind}'.",
                nameof(targetKind));
        }

        if (!double.IsFinite(value) || (descriptor.ValueKind == "integer" && value % 1 != 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, $"Metric '{metricId}' requires a finite {descriptor.ValueKind} value.");
        }

        return new MetricResultLine
        {
            SchemaVersion = ContractVersion.Current,
            MetricId = descriptor.Id,
            MetricVersion = descriptor.Version,
            TargetId = targetId,
            TargetKind = targetKind,
            TargetIdStability = targetIdStability,
            ValueKind = descriptor.ValueKind,
            NumericValue = value,
            Unit = descriptor.Unit,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Tags = tags
        };
    }
}
