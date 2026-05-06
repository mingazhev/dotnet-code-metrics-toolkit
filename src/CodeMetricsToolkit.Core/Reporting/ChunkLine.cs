namespace CodeMetricsToolkit.Core.Reporting;

public sealed record ChunkLine
{
    public required string SchemaVersion { get; init; }
    public required string ChunkId { get; init; }
    public required string TargetId { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetIdStability { get; init; }
    public required string ChunkKind { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int TokenEstimate { get; init; }
    public required string TextHash { get; init; }
    public required IReadOnlyList<string> RelatedTargetIds { get; init; }
    public string? Text { get; init; }
}
