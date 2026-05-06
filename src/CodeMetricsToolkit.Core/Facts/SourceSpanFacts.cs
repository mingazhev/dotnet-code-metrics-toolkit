namespace CodeMetricsToolkit.Core.Facts;

public sealed record SourceSpanFacts
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
}
