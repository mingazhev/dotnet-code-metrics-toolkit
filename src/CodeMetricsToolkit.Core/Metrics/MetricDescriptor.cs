namespace CodeMetricsToolkit.Core.Metrics;

public sealed record MetricDescriptor(
    string Id,
    string Version,
    IReadOnlyList<string> TargetKinds,
    string AnalysisMode,
    string Unit,
    string Formula,
    string Description,
    IReadOnlyList<string> KnownLimitations);
