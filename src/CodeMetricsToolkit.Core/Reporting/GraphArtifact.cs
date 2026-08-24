namespace CodeMetricsToolkit.Core.Reporting;

public sealed record GraphArtifact(
    string SchemaVersion,
    IReadOnlyList<GraphNodeLine> Nodes,
    IReadOnlyList<GraphEdgeLine> Edges);

public sealed record GraphNodeLine
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string TargetIdStability { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public IReadOnlyList<GraphSourceSpanLine>? Declarations { get; init; }
}

public sealed record GraphSourceSpanLine(
    string FilePath,
    int StartLine,
    int EndLine);

public sealed record GraphEdgeLine(
    string From,
    string To,
    string Kind,
    string Confidence);
