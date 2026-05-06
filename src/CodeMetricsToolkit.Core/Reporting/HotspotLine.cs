namespace CodeMetricsToolkit.Core.Reporting;

public sealed record HotspotLine
{
    public required string TargetId { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetName { get; init; }
    public required int Rank { get; init; }
    public required double RankScore { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<HotspotComponentLine> Components { get; init; }
}

public sealed record HotspotComponentLine(
    string MetricId,
    double Value,
    double Percentile,
    double Weight);
