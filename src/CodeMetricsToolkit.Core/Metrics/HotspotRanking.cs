using CodeMetricsToolkit.Core.Contracts;
namespace CodeMetricsToolkit.Core.Metrics;

public sealed record HotspotRanking(
    IReadOnlyList<HotspotLine> Hotspots,
    IReadOnlyList<MetricResultLine> Metrics);
