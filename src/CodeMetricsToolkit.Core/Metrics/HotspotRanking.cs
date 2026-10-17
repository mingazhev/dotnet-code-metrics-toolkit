using CodeMetricsToolkit.Core.Reporting;
namespace CodeMetricsToolkit.Core.Metrics;

public sealed record HotspotRanking(
    IReadOnlyList<HotspotLine> Hotspots,
    IReadOnlyList<MetricResultLine> Metrics);
