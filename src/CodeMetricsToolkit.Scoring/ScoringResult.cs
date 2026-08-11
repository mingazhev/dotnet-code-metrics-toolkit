namespace CodeMetricsToolkit.Scoring;

public sealed record ScoringResult
{
    public required IReadOnlyDictionary<string, double> Values { get; init; }
    public required ScoringProvenance Provenance { get; init; }
}

public sealed record ScoringProvenance
{
    public required string ProfileSchemaVersion { get; init; }
    public required string ProfileId { get; init; }
    public required string ProfileVersion { get; init; }
    public required string ProfileSha256 { get; init; }
    public required string ExpectedArtifactContractVersion { get; init; }
    public required string ArtifactSchemaVersion { get; init; }
    public required string ManifestSha256 { get; init; }
    public required string SummarySha256 { get; init; }
    public required string MetricsSha256 { get; init; }
    public required string GraphSha256 { get; init; }
    public required string RootPath { get; init; }
    public required string AnalysisMode { get; init; }
    public required IReadOnlyList<string> TargetIdStabilities { get; init; }
    public required IReadOnlyList<string> AllowedAnalysisModes { get; init; }
    public required IReadOnlyList<string> AllowedTargetIdStabilities { get; init; }
    public required string AnalysisQuality { get; init; }
    public required bool TrustedDiagnostics { get; init; }
    public required ScoringSelectionProvenance Selection { get; init; }
    public required ScoringPrimaryMetric Primary { get; init; }
    public required IReadOnlyList<ScoringOperationProvenance> Operations { get; init; }
}

/// <summary>
/// File selectors are target-wide: all declaration paths must match an include pattern, while any
/// declaration matching an exclude pattern rejects the complete aggregated target.
/// </summary>
public sealed record ScoringSelectionProvenance
{
    public required IReadOnlyList<string> IncludeFilePaths { get; init; }
    public required IReadOnlyList<string> ExcludeFilePaths { get; init; }
    public required bool RequireTrusted { get; init; }
    public required int MinTargetCount { get; init; }
}

public sealed record ScoringPrimaryMetric
{
    public required string Key { get; init; }
    public required string Direction { get; init; }
}

public sealed record ScoringOperationProvenance
{
    public required int Index { get; init; }
    public required string Operation { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public required IReadOnlyList<string> Outputs { get; init; }
    public string? TargetKind { get; init; }
    public int? SelectedTargetCount { get; init; }
}
