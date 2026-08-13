namespace CodeMetricsToolkit.Core.Facts;

public sealed record OperationFacts
{
    public required int OperationCount { get; init; }
    public required int AllocationCount { get; init; }
    public required int AwaitCount { get; init; }
    public required int ControlFlowGraphCount { get; init; }
    public required int BasicBlockCount { get; init; }
    public required int ReachableBasicBlockCount { get; init; }
    public required int UnreachableBasicBlockCount { get; init; }
    public required int ControlFlowEdgeCount { get; init; }
    public required int CfgCyclomaticComplexity { get; init; }
}
