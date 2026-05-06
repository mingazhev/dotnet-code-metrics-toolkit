namespace CodeMetricsToolkit.Core.Facts;

public sealed record AnalysisHealth
{
    public required string AnalysisQuality { get; init; }
    public required string SemanticModel { get; init; }
    public required string RestoreStatus { get; init; }
    public required string BuildStatus { get; init; }
    public required bool TrustedDiagnostics { get; init; }
    public required bool DiagnosticsIncludedInHotspotRank { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
}
