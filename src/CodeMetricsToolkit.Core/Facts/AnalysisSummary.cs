namespace CodeMetricsToolkit.Core.Facts;

public sealed record AnalysisSummary
{
    internal static AnalysisSummary Create(AnalysisFacts facts, string rootPath, int metricResultCount)
    {
        return new AnalysisSummary
        {
            RootPath = rootPath,
            ProjectCount = facts.ProjectPaths.Count,
            FileCount = facts.Files.Select(file => file.FilePath).Distinct(StringComparer.Ordinal).Count(),
            TypeCount = facts.Types.Count,
            MemberCount = facts.Members.Count,
            MetricResultCount = metricResultCount,
            DiagnosticCount = facts.Diagnostics.Count,
            Health = facts.Health
        };
    }

    public required string RootPath { get; init; }
    public required int ProjectCount { get; init; }
    public required int FileCount { get; init; }
    public required int TypeCount { get; init; }
    public required int MemberCount { get; init; }
    public required int MetricResultCount { get; init; }
    public required int DiagnosticCount { get; init; }
    public required AnalysisHealth Health { get; init; }
}
