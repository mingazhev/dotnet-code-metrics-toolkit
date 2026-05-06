namespace CodeMetricsToolkit.Core.Reporting;

public sealed record HotspotRanking(
    IReadOnlyList<HotspotLine> Hotspots,
    IReadOnlyList<MetricResultLine> Metrics);
