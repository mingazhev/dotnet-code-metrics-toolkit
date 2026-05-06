namespace CodeMetricsToolkit.Core.Reporting;

public sealed record MetricResultLine
{
    public required string SchemaVersion { get; init; }
    public required string MetricId { get; init; }
    public required string MetricVersion { get; init; }
    public required string TargetId { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetIdStability { get; init; }
    public required string ValueKind { get; init; }
    public required double NumericValue { get; init; }
    public required string Unit { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}
