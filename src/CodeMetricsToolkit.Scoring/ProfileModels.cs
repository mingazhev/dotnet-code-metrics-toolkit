namespace CodeMetricsToolkit.Scoring;

internal sealed record ParsedScoringProfile(
    string SchemaVersion,
    string Id,
    string Version,
    string ArtifactContractVersion,
    IReadOnlyList<string> AllowedAnalysisModes,
    IReadOnlyList<string> AllowedTargetIdStabilities,
    SelectionOptions Selectors,
    ScoringPrimaryMetric Primary,
    IReadOnlyList<ParsedScoringOperation> Operations);

internal sealed record SelectionOptions(
    IReadOnlyList<string> IncludeFilePaths,
    IReadOnlyList<string> ExcludeFilePaths,
    bool RequireTrusted,
    int MinTargetCount);

internal abstract record ParsedScoringOperation(string Operation);

internal sealed record ThresholdDebtOperation(
    string TargetKind,
    IReadOnlyList<MetricThreshold> Thresholds,
    ThresholdDebtOutputs Outputs)
    : ParsedScoringOperation("thresholdDebt");

internal sealed record MetricThreshold(
    string MetricId,
    string MetricVersion,
    double Maximum,
    double Weight,
    IReadOnlyList<string> Tags)
{
    public string Reference => FormatReference(MetricId, MetricVersion, Tags);

    public string Signature => $"{MetricId}\u001e{MetricVersion}\u001e{string.Join('\u001f', Tags)}";

    internal static string FormatReference(
        string metricId,
        string metricVersion,
        IReadOnlyList<string> tags)
    {
        var versionedId = $"{metricId}@{metricVersion}";

        return tags.Count == 0
            ? versionedId
            : $"{versionedId}[tags={string.Join(',', tags)}]";
    }
}

internal sealed record ThresholdDebtOutputs(
    string GapSum,
    string SquaredGapSum,
    string ViolatingTargetCount,
    string MaxTargetGap)
{
    public IReadOnlyList<string> All { get; } =
    [
        GapSum,
        SquaredGapSum,
        ViolatingTargetCount,
        MaxTargetGap
    ];
}

internal sealed record WeightedSumOperation(
    IReadOnlyList<WeightedTerm> Terms,
    string Output)
    : ParsedScoringOperation("weightedSum");

internal sealed record WeightedTerm(string Key, double Weight);
