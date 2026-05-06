namespace CodeMetricsToolkit.Abstractions;

public sealed record AnalyzeRequest
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public bool IncludeGeneratedCode { get; init; }
    public bool IncludeChunkText { get; init; }
    public bool SyntaxOnly { get; init; }
    public int Top { get; init; } = 20;
}
