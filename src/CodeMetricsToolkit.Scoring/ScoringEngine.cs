using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace CodeMetricsToolkit.Scoring;

public static class ScoringEngine
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<ScoringResult> EvaluateAsync(
        string artifactDirectory,
        string profilePath,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateAsync(
            artifactDirectory,
            profilePath,
            ScoringInputLimits.Default,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<ScoringResult> EvaluateAsync(
        string artifactDirectory,
        string profilePath,
        ScoringInputLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        byte[] profileBytes;
        try
        {
            profileBytes = await ScoringInputReader.ReadFileBytesAsync(
                profilePath,
                limits.MaxProfileBytes,
                "The scoring profile",
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidProfile,
                exception.Message,
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidProfile,
                $"Could not read scoring profile '{profilePath}'.",
                exception);
        }

        return await EvaluateAsync(artifactDirectory, profileBytes, limits, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ScoringResult> EvaluateAsync(
        string artifactDirectory,
        Stream profileJson,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateAsync(
            artifactDirectory,
            profileJson,
            ScoringInputLimits.Default,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<ScoringResult> EvaluateAsync(
        string artifactDirectory,
        Stream profileJson,
        ScoringInputLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        ArgumentNullException.ThrowIfNull(profileJson);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        if (!profileJson.CanRead)
        {
            throw new ArgumentException("The profile stream must be readable.", nameof(profileJson));
        }

        byte[] profileBytes;
        try
        {
            profileBytes = await ScoringInputReader.ReadStreamBytesAsync(
                profileJson,
                limits.MaxProfileBytes,
                "The scoring profile",
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidProfile,
                exception.Message,
                exception);
        }

        return await EvaluateAsync(artifactDirectory, profileBytes, limits, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ScoringResult> EvaluateAsync(
        string artifactDirectory,
        byte[] profileBytes,
        ScoringInputLimits limits,
        CancellationToken cancellationToken)
    {
        var profileJson = DecodeProfile(profileBytes);
        ParsedScoringProfile profile = ScoringProfileParser.Parse(profileJson, limits.MaxJsonDepth);
        ScoringArtifacts artifacts = await ScoringArtifactReader.ReadAsync(
            artifactDirectory,
            limits,
            cancellationToken).ConfigureAwait(false);

        ValidateArtifactContract(profile, artifacts.Summary);
        ValidateAnalysisHealth(profile, artifacts.Summary);
        ValidateModeAndTargetIdStability(profile, artifacts);

        IReadOnlyList<ArtifactMetric> selectedMetrics = ApplyFileSelectors(
            artifacts.Metrics,
            artifacts.Graph,
            profile.Selectors);
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        var operationProvenance = new List<ScoringOperationProvenance>(profile.Operations.Count);

        for (var index = 0; index < profile.Operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedScoringOperation operation = profile.Operations[index];

            switch (operation)
            {
                case ThresholdDebtOperation thresholdDebt:
                    EvaluateThresholdDebt(
                        index,
                        thresholdDebt,
                        profile.Selectors,
                        artifacts.Metrics,
                        selectedMetrics,
                        values,
                        operationProvenance);
                    break;

                case WeightedSumOperation weightedSum:
                    EvaluateWeightedSum(index, weightedSum, values, operationProvenance);
                    break;
            }
        }

        var orderedValues = new SortedDictionary<string, double>(values, StringComparer.Ordinal);

        return new ScoringResult
        {
            Values = new ReadOnlyDictionary<string, double>(orderedValues),
            Provenance = new ScoringProvenance
            {
                ProfileSchemaVersion = profile.SchemaVersion,
                ProfileId = profile.Id,
                ProfileVersion = profile.Version,
                ProfileSha256 = Hash(profileBytes),
                ExpectedArtifactContractVersion = profile.ArtifactContractVersion,
                ArtifactSchemaVersion = artifacts.Summary.SchemaVersion,
                ManifestSha256 = artifacts.ManifestSha256,
                SummarySha256 = artifacts.SummarySha256,
                MetricsSha256 = artifacts.MetricsSha256,
                GraphSha256 = artifacts.GraphSha256,
                RootPath = artifacts.Summary.RootPath,
                AnalysisMode = artifacts.AnalysisMode,
                TargetIdStabilities = artifacts.Metrics
                    .Select(metric => metric.TargetIdStability)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                AllowedAnalysisModes = profile.AllowedAnalysisModes.ToArray(),
                AllowedTargetIdStabilities = profile.AllowedTargetIdStabilities.ToArray(),
                AnalysisQuality = artifacts.Summary.AnalysisQuality,
                TrustedDiagnostics = artifacts.Summary.TrustedDiagnostics,
                Selection = new ScoringSelectionProvenance
                {
                    IncludeFilePaths = profile.Selectors.IncludeFilePaths.ToArray(),
                    ExcludeFilePaths = profile.Selectors.ExcludeFilePaths.ToArray(),
                    RequireTrusted = profile.Selectors.RequireTrusted,
                    MinTargetCount = profile.Selectors.MinTargetCount
                },
                Primary = new ScoringPrimaryMetric
                {
                    Key = profile.Primary.Key,
                    Direction = profile.Primary.Direction
                },
                Operations = operationProvenance.ToArray()
            }
        };
    }

    private static void ValidateAnalysisHealth(
        ParsedScoringProfile profile,
        ArtifactSummary summary)
    {
        if (profile.Selectors.RequireTrusted &&
            (!string.Equals(summary.AnalysisQuality, "trusted", StringComparison.Ordinal) ||
             !summary.TrustedDiagnostics))
        {
            throw PreconditionsNotMet(
                "The profile requires trusted analysis and diagnostics, but summary.json reports " +
                $"analysisQuality='{summary.AnalysisQuality}' and " +
                $"trustedDiagnostics={summary.TrustedDiagnostics.ToString().ToLowerInvariant()}.");
        }
    }

    private static void ValidateModeAndTargetIdStability(
        ParsedScoringProfile profile,
        ScoringArtifacts artifacts)
    {
        if (!profile.AllowedAnalysisModes.Contains(artifacts.AnalysisMode, StringComparer.Ordinal))
        {
            throw PreconditionsNotMet(
                $"manifest.json mode '{artifacts.AnalysisMode}' is not allowed by the scoring profile.");
        }

        var unexpectedStabilities = artifacts.Metrics
            .Select(metric => metric.TargetIdStability)
            .Distinct(StringComparer.Ordinal)
            .Where(stability => !profile.AllowedTargetIdStabilities.Contains(
                stability,
                StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (unexpectedStabilities.Length > 0)
        {
            throw PreconditionsNotMet(
                "metrics.ndjson contains targetIdStability values not allowed by the scoring profile: " +
                string.Join(", ", unexpectedStabilities));
        }
    }

    private static void ValidateArtifactContract(
        ParsedScoringProfile profile,
        ArtifactSummary summary)
    {
        if (!string.Equals(
                summary.SchemaVersion,
                profile.ArtifactContractVersion,
                StringComparison.Ordinal))
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidArtifacts,
                $"summary.json uses schemaVersion '{summary.SchemaVersion}', but the profile requires " +
                $"artifactContractVersion '{profile.ArtifactContractVersion}'.");
        }
    }

    private static ArtifactMetric[] ApplyFileSelectors(
        IReadOnlyList<ArtifactMetric> metrics,
        ArtifactGraph graph,
        SelectionOptions selectors)
    {
        var hasFileSelectors = selectors.IncludeFilePaths.Count > 0 ||
            selectors.ExcludeFilePaths.Count > 0;
        if (!hasFileSelectors)
        {
            return metrics
                .Where(metric => metric.TargetKind is "file" or "type" or "member")
                .ToArray();
        }

        var metricTargetIds = metrics
            .Select(metric => metric.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        ArtifactTargetPaths? incompleteTarget = graph.Targets.Values.FirstOrDefault(target =>
            metricTargetIds.Contains(target.TargetId) &&
            target.TargetKind is "file" or "type" or "member" &&
            !target.HasCompleteDeclarationPaths);
        if (incompleteTarget is not null)
        {
            throw PreconditionsNotMet(
                $"File selectors require complete graph declaration paths, but target " +
                $"'{incompleteTarget.TargetId}' does not provide them.");
        }

        var selectedTargetIds = graph.Targets.Values
            .Where(target => target.FilePaths.Count > 0 && IsIncluded(target.FilePaths, selectors))
            .Select(target => target.TargetId)
            .ToHashSet(StringComparer.Ordinal);

        return metrics
            .Where(metric => selectedTargetIds.Contains(metric.TargetId))
            .ToArray();
    }

    private static bool IsIncluded(
        IReadOnlyList<string> filePaths,
        SelectionOptions selectors)
    {
        var included = selectors.IncludeFilePaths.Count == 0 ||
            filePaths.All(filePath =>
                selectors.IncludeFilePaths.Any(pattern => GlobMatcher.IsMatch(pattern, filePath)));
        var excluded = filePaths.Any(filePath =>
            selectors.ExcludeFilePaths.Any(pattern => GlobMatcher.IsMatch(pattern, filePath)));

        return included && !excluded;
    }

    private static void EvaluateThresholdDebt(
        int operationIndex,
        ThresholdDebtOperation operation,
        SelectionOptions selectors,
        IReadOnlyList<ArtifactMetric> allMetrics,
        IReadOnlyList<ArtifactMetric> selectedMetrics,
        Dictionary<string, double> values,
        List<ScoringOperationProvenance> provenance)
    {
        IReadOnlyList<ResolvedMetricThreshold> thresholds = operation.Thresholds
            .Select(threshold => ResolveMetricThreshold(threshold, operation.TargetKind, allMetrics))
            .ToArray();
        IGrouping<string, ArtifactMetric>[] targetGroups = selectedMetrics
            .Where(metric => string.Equals(metric.TargetKind, operation.TargetKind, StringComparison.Ordinal))
            .GroupBy(metric => metric.TargetId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (targetGroups.Length == 0)
        {
            throw PreconditionsNotMet(
                $"operations[{operationIndex}] selected no '{operation.TargetKind}' targets. " +
                "Review includeFilePaths and excludeFilePaths.");
        }

        if (targetGroups.Length < selectors.MinTargetCount)
        {
            throw PreconditionsNotMet(
                $"operations[{operationIndex}] selected {targetGroups.Length} '{operation.TargetKind}' " +
                $"targets, fewer than selectors.minTargetCount={selectors.MinTargetCount}.");
        }

        double gapSum = 0;
        double squaredGapSum = 0;
        double violatingTargetCount = 0;
        double maxTargetGap = 0;

        foreach (IGrouping<string, ArtifactMetric> targetGroup in targetGroups)
        {
            EnsureSingleFilePath(targetGroup, operationIndex);
            double targetGap = 0;

            foreach (ResolvedMetricThreshold threshold in thresholds)
            {
                ArtifactMetric? metric = targetGroup.SingleOrDefault(candidate =>
                    string.Equals(candidate.MetricId, threshold.MetricId, StringComparison.Ordinal) &&
                    string.Equals(candidate.MetricVersion, threshold.MetricVersion, StringComparison.Ordinal) &&
                    candidate.Tags.SequenceEqual(threshold.Tags));

                if (metric is null)
                {
                    throw PreconditionsNotMet(
                        $"Target '{targetGroup.Key}' selected by operations[{operationIndex}] is missing " +
                        $"required metric '{threshold.Reference}'.");
                }

                if (metric.NumericValue is null)
                {
                    throw PreconditionsNotMet(
                        $"Metric '{threshold.Reference}' for target '{targetGroup.Key}' is not numeric.");
                }

                var gap = Math.Max(0d, metric.NumericValue.Value - threshold.Maximum);
                var weightedGap = gap * threshold.Weight;
                EnsureFinite(weightedGap, operationIndex, "weighted threshold gap");
                targetGap += weightedGap;
                EnsureFinite(targetGap, operationIndex, "target gap");
            }

            gapSum += targetGap;
            squaredGapSum += targetGap * targetGap;
            EnsureFinite(gapSum, operationIndex, "gapSum");
            EnsureFinite(squaredGapSum, operationIndex, "squaredGapSum");

            if (targetGap > 0)
            {
                violatingTargetCount++;
            }

            maxTargetGap = Math.Max(maxTargetGap, targetGap);
        }

        values.Add(operation.Outputs.GapSum, gapSum);
        values.Add(operation.Outputs.SquaredGapSum, squaredGapSum);
        values.Add(operation.Outputs.ViolatingTargetCount, violatingTargetCount);
        values.Add(operation.Outputs.MaxTargetGap, maxTargetGap);

        provenance.Add(new ScoringOperationProvenance
        {
            Index = operationIndex,
            Operation = operation.Operation,
            Inputs = thresholds.Select(threshold => threshold.Reference).ToArray(),
            Outputs = operation.Outputs.All.ToArray(),
            TargetKind = operation.TargetKind,
            SelectedTargetCount = targetGroups.Length
        });
    }

    private static ResolvedMetricThreshold ResolveMetricThreshold(
        MetricThreshold threshold,
        string targetKind,
        IReadOnlyList<ArtifactMetric> allMetrics)
    {
        ArtifactMetric[] byId = allMetrics
            .Where(metric => string.Equals(metric.MetricId, threshold.MetricId, StringComparison.Ordinal))
            .ToArray();

        if (byId.Length == 0)
        {
            throw InvalidProfile(
                $"Unknown metric '{threshold.MetricId}': it is not present in metrics.ndjson.");
        }

        var metricVersion = threshold.MetricVersion;
        if (!byId.Any(metric => string.Equals(metric.MetricVersion, metricVersion, StringComparison.Ordinal)))
        {
            throw InvalidProfile(
                $"Unknown metric '{threshold.Reference}': that exact current version is not present " +
                "in metrics.ndjson.");
        }

        if (!byId.Any(metric =>
                string.Equals(metric.MetricVersion, metricVersion, StringComparison.Ordinal) &&
                string.Equals(metric.TargetKind, targetKind, StringComparison.Ordinal) &&
                metric.Tags.SequenceEqual(threshold.Tags)))
        {
            throw InvalidProfile(
                $"Metric '{MetricThreshold.FormatReference(threshold.MetricId, metricVersion, threshold.Tags)}' " +
                $"is not available for targetKind " +
                $"'{targetKind}' in metrics.ndjson.");
        }

        return new ResolvedMetricThreshold(
            threshold.MetricId,
            metricVersion,
            threshold.Maximum,
            threshold.Weight,
            threshold.Tags);
    }

    private static void EvaluateWeightedSum(
        int operationIndex,
        WeightedSumOperation operation,
        Dictionary<string, double> values,
        List<ScoringOperationProvenance> provenance)
    {
        double sum = 0;

        foreach (WeightedTerm term in operation.Terms)
        {
            var contribution = values[term.Key] * term.Weight;
            EnsureFinite(contribution, operationIndex, $"weighted contribution for '{term.Key}'");
            sum += contribution;
            EnsureFinite(sum, operationIndex, "weightedSum output");
        }

        values.Add(operation.Output, sum);
        provenance.Add(new ScoringOperationProvenance
        {
            Index = operationIndex,
            Operation = operation.Operation,
            Inputs = operation.Terms.Select(term => term.Key).ToArray(),
            Outputs = [operation.Output]
        });
    }

    private static void EnsureSingleFilePath(
        IEnumerable<ArtifactMetric> targetMetrics,
        int operationIndex)
    {
        var paths = targetMetrics
            .Select(metric => metric.FilePath)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        if (paths.Length > 1)
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidArtifacts,
                $"A target selected by operations[{operationIndex}] has metrics from multiple file paths.");
        }
    }

    private static void EnsureFinite(double value, int operationIndex, string valueDescription)
    {
        if (!double.IsFinite(value))
        {
            throw PreconditionsNotMet(
                $"operations[{operationIndex}] produced a non-finite {valueDescription}. " +
                "Reduce thresholds, metric weights, or weightedSum weights.");
        }
    }

    private static string DecodeProfile(ReadOnlySpan<byte> bytes)
    {
        var hasUtf8Bom = bytes.Length >= 3 &&
            bytes[0] == 0xef &&
            bytes[1] == 0xbb &&
            bytes[2] == 0xbf;
        ReadOnlySpan<byte> content = hasUtf8Bom ? bytes[3..] : bytes;

        try
        {
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidProfile,
                "The scoring profile must be valid UTF-8 JSON.",
                exception);
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static ScoringException InvalidProfile(string message)
    {
        return new ScoringException(ScoringFailureKind.InvalidProfile, message);
    }

    private static ScoringException PreconditionsNotMet(string message)
    {
        return new ScoringException(ScoringFailureKind.PreconditionsNotMet, message);
    }

    private sealed record ResolvedMetricThreshold(
        string MetricId,
        string MetricVersion,
        double Maximum,
        double Weight,
        IReadOnlyList<string> Tags)
    {
        public string Reference => MetricThreshold.FormatReference(MetricId, MetricVersion, Tags);
    }
}
