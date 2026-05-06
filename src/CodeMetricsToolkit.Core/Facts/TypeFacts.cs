namespace CodeMetricsToolkit.Core.Facts;

public sealed record TypeFacts
{
    public required string TargetId { get; init; }
    public required string ParentFileTargetId { get; init; }
    public required string ProjectKey { get; init; }
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int LinesOfCode { get; init; }
    public required int NonCommentLinesOfCode { get; init; }
    public required int MemberCount { get; init; }
}
