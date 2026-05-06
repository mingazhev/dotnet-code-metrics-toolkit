namespace CodeMetricsToolkit.Core.Facts;

public sealed record MemberFacts
{
    public required string TargetId { get; init; }
    public required string TargetIdStability { get; init; }
    public required string ParentTypeTargetId { get; init; }
    public required string ProjectKey { get; init; }
    public required string Name { get; init; }
    public required string ChunkKind { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required IReadOnlyList<SourceSpanFacts> Declarations { get; init; }
    public required int MethodLength { get; init; }
    public required int ParameterCount { get; init; }
}
