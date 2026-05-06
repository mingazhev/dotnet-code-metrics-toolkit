namespace CodeMetricsToolkit.Core.Facts;

public sealed record ControlFlowFacts
{
    public required int CyclomaticComplexity { get; init; }
    public required int DecisionPointCount { get; init; }
    public required int CognitiveComplexity { get; init; }
    public required int NestingDepth { get; init; }
}
