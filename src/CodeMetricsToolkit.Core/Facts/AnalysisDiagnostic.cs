namespace CodeMetricsToolkit.Core.Facts;

public sealed record AnalysisDiagnostic
{
    public required string Id { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? ProjectPath { get; init; }
    public string? FilePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}
