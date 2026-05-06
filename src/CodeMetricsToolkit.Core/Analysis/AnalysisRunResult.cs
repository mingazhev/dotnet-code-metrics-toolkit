namespace CodeMetricsToolkit.Core.Analysis;

public sealed record AnalysisRunResult
{
    public required AnalysisSummary Summary { get; init; }
    public required string OutputPath { get; init; }
}
