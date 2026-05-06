namespace CodeMetricsToolkit.Core.Facts;

public sealed record SyntaxAnalysisFacts
{
    public required string Mode { get; init; }
    public required AnalysisHealth Health { get; init; }
    public required string RootPath { get; init; }
    public required IReadOnlyList<string> ProjectPaths { get; init; }
    public required IReadOnlyList<FileFacts> Files { get; init; }
    public required IReadOnlyList<TypeFacts> Types { get; init; }
    public required IReadOnlyList<MemberFacts> Members { get; init; }
    public required IReadOnlyList<GraphEdgeFacts> GraphEdges { get; init; }
    public required IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; init; }
}
