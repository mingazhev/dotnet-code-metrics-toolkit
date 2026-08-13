namespace CodeMetricsToolkit.Scoring;

internal sealed record ScoringArtifacts(
    string AnalysisMode,
    ArtifactSummary Summary,
    IReadOnlyList<ArtifactMetric> Metrics,
    ArtifactGraph Graph,
    string ManifestSha256,
    string SummarySha256,
    string MetricsSha256,
    string GraphSha256);

internal sealed record MetricsSnapshot(
    IReadOnlyList<ArtifactMetric> Metrics,
    string Sha256);

internal sealed record ArtifactSummary(
    string SchemaVersion,
    string RootPath,
    int MetricResultCount,
    string AnalysisQuality,
    bool TrustedDiagnostics);

internal sealed record ArtifactGraph(
    string SchemaVersion,
    IReadOnlyDictionary<string, ArtifactTargetPaths> Targets);

internal sealed record ArtifactTargetPaths(
    string TargetId,
    string TargetKind,
    string TargetIdStability,
    IReadOnlyList<string> FilePaths,
    bool HasCompleteDeclarationPaths);

internal sealed record ArtifactMetric(
    string SchemaVersion,
    string MetricId,
    string MetricVersion,
    string TargetId,
    string TargetKind,
    string TargetIdStability,
    double? NumericValue,
    string? FilePath,
    IReadOnlyList<string> Tags)
{
    public string TagSignature => string.Join('\u001f', Tags);
}
