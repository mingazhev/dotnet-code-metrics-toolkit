namespace CodeMetricsToolkit.Core.Metrics;

public sealed record MetricDescriptor(
    string Id,
    string Version,
    IReadOnlyList<string> TargetKinds,
    string AnalysisMode,
    string Unit,
    string ValueKind,
    string Formula,
    string Description,
    IReadOnlyList<string> KnownLimitations);
