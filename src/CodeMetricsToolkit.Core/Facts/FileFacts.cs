namespace CodeMetricsToolkit.Core.Facts;

public sealed record FileFacts
{
    public required string TargetId { get; init; }
    public required string TargetIdStability { get; init; }
    public required string ProjectKey { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int LinesOfCode { get; init; }
    public required int NonCommentLinesOfCode { get; init; }
    public required int BlankLineCount { get; init; }
    public required int CommentOnlyLineCount { get; init; }
    public required int CommentedLineCount { get; init; }
    public required int MixedCodeCommentLineCount { get; init; }
    public required int DocumentationCommentLineCount { get; init; }
}
