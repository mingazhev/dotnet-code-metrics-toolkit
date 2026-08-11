namespace CodeMetricsToolkit.Core.Analysis;

public sealed record AnalyzeRequest
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public IReadOnlyList<string> IncludePatterns { get; init; } = [];
    public IReadOnlyList<string> ExcludePatterns { get; init; } = [];
    public bool IncludeGeneratedCode { get; init; }
    public bool IncludeChunkText { get; init; }
    public bool SyntaxOnly { get; init; }
    public bool NoRestore { get; init; }
    public bool IsolateInput { get; init; }
    public int Top { get; init; } = 20;
}
