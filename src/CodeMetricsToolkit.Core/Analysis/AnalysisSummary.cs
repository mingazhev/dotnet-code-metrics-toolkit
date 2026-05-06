namespace CodeMetricsToolkit.Core.Analysis;

using CodeMetricsToolkit.Core.Facts;

public sealed record AnalysisSummary
{
    public required string RootPath { get; init; }
    public required int ProjectCount { get; init; }
    public required int FileCount { get; init; }
    public required int TypeCount { get; init; }
    public required int MemberCount { get; init; }
    public required int MetricResultCount { get; init; }
    public required int DiagnosticCount { get; init; }
    public required AnalysisHealth Health { get; init; }
}
