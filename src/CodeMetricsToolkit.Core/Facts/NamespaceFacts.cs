namespace CodeMetricsToolkit.Core.Facts;

internal sealed record NamespaceFacts(
    string TargetId,
    string ProjectKey,
    string ProjectPath,
    string Name,
    IReadOnlyList<string> TypeTargetIds,
    IReadOnlyList<SourceSpanFacts> Declarations);
