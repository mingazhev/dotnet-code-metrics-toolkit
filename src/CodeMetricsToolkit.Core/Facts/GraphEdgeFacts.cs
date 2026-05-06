namespace CodeMetricsToolkit.Core.Facts;

public sealed record GraphEdgeFacts
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Kind { get; init; }
    public required string Confidence { get; init; }
}
