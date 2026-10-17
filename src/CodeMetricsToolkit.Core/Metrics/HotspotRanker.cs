using CodeMetricsToolkit.Core.Reporting;
using System.Globalization;
using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Metrics;

public static class HotspotRanker
{
    private static readonly Dictionary<string, int> KindOrder = new(StringComparer.Ordinal)
    {
        ["member"] = 0,
        ["type"] = 1,
        ["file"] = 2
    };

    private static readonly HotspotComponentWeight[] MemberWeights =
    [
        new("cognitive_complexity", 0.30),
        new("cyclomatic_complexity", 0.25),
        new("nesting_depth", 0.15),
        new("member_length", 0.15),
        new("diagnostic_count", 0.10),
        new("parameter_count", 0.05)
    ];

    private static readonly HotspotComponentWeight[] TypeWeights =
    [
        new("max_member_cognitive_complexity", 0.25),
        new("p95_member_cyclomatic_complexity", 0.20),
        new("outgoing_type_dependency_count", 0.20),
        new("lines_of_code", 0.15),
        new("member_count", 0.10),
        new("diagnostic_count", 0.10)
    ];

    private static readonly HotspotComponentWeight[] FileWeights =
    [
        new("p95_member_cognitive_complexity", 0.25),
        new("p95_member_cyclomatic_complexity", 0.20),
        new("lines_of_code", 0.20),
        new("diagnostic_count", 0.15),
        new("type_count", 0.10),
        new("member_count", 0.10)
    ];

    public static HotspotRanking Rank(AnalysisFacts facts, int top, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        cancellationToken.ThrowIfCancellationRequested();

        MetricResultLine[] metrics = SyntaxMetricProjector.Project(facts, cancellationToken)
            .Concat(GraphMetricProjector.Project(facts, cancellationToken))
            .Concat(SemanticMetricProjector.Project(facts, cancellationToken))
            .Concat(DiagnosticMetricProjector.Project(facts, cancellationToken))
            .ToArray();

        return Rank(facts, metrics, top, cancellationToken);
    }

    internal static HotspotRanking Rank(
        AnalysisFacts facts,
        IReadOnlyList<MetricResultLine> metrics,
        int top,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(metrics);
        cancellationToken.ThrowIfCancellationRequested();

        var metricsByTarget = metrics
            .Where(metric => metric.Tags is null or { Count: 0 })
            .ToDictionary(metric => new ComponentKey(metric.TargetId, metric.MetricId));
        var candidates = CreateMemberCandidates(facts, metricsByTarget)
            .Concat(CreateTypeCandidates(facts, metricsByTarget))
            .Concat(CreateFileCandidates(facts, metricsByTarget))
            .ToList();
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<ComponentKey, Dictionary<double, double>> percentiles = CalculatePercentiles(candidates);
        IReadOnlyList<RankedHotspot> rankedHotspots = candidates
            .Select(candidate => ApplyPercentiles(candidate, percentiles))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => KindOrder[candidate.TargetKind])
            .ThenBy(candidate => candidate.TargetId, StringComparer.Ordinal)
            .Select((candidate, index) => candidate with { Rank = index + 1 })
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<HotspotLine> hotspots = rankedHotspots
            .Where(hotspot => hotspot.Score > 0)
            .Take(top)
            .Select(ToHotspotLine)
            .ToArray();
        IReadOnlyList<MetricResultLine> rankingMetrics = rankedHotspots
            .Select(ToHotspotMetric)
            .ToArray();

        return new HotspotRanking(hotspots, rankingMetrics);
    }

    private static IEnumerable<HotspotCandidate> CreateMemberCandidates(
        AnalysisFacts facts,
        IReadOnlyDictionary<ComponentKey, MetricResultLine> metrics)
    {
        return facts.Members.Select(member => new HotspotCandidate(
            member.TargetId,
            "member",
            member.Name,
            member.TargetIdStability,
            member.FilePath,
            member.StartLine,
            member.EndLine,
            CreateComponents(member.TargetId, MemberWeights, metrics, facts.Health.DiagnosticsIncludedInHotspotRank)));
    }

    private static IEnumerable<HotspotCandidate> CreateTypeCandidates(
        AnalysisFacts facts,
        IReadOnlyDictionary<ComponentKey, MetricResultLine> metrics)
    {
        return facts.Types.Select(type => new HotspotCandidate(
            type.TargetId,
            "type",
            type.Name,
            type.TargetIdStability,
            type.FilePath,
            type.StartLine,
            type.EndLine,
            CreateComponents(type.TargetId, TypeWeights, metrics, facts.Health.DiagnosticsIncludedInHotspotRank)));
    }

    private static IEnumerable<HotspotCandidate> CreateFileCandidates(
        AnalysisFacts facts,
        IReadOnlyDictionary<ComponentKey, MetricResultLine> metrics)
    {
        return facts.Files.Select(file => new HotspotCandidate(
            file.TargetId,
            "file",
            Path.GetFileName(file.FilePath),
            file.TargetIdStability,
            file.FilePath,
            file.StartLine,
            file.EndLine,
            CreateComponents(file.TargetId, FileWeights, metrics, facts.Health.DiagnosticsIncludedInHotspotRank)));
    }

    private static HotspotComponentValue[] CreateComponents(
        string targetId,
        IReadOnlyList<HotspotComponentWeight> weights,
        IReadOnlyDictionary<ComponentKey, MetricResultLine> metrics,
        bool includeDiagnostics)
    {
        HotspotComponentValue[] available = weights
            .Where(component => includeDiagnostics || component.MetricId != "diagnostic_count")
            .Where(component => metrics.ContainsKey(new ComponentKey(targetId, component.MetricId)))
            .Select(component => new HotspotComponentValue(
                component.MetricId,
                metrics[new ComponentKey(targetId, component.MetricId)].NumericValue,
                component.Weight))
            .ToArray();
        var totalWeight = available.Sum(component => component.Weight);

        return totalWeight == 0
            ? available
            : available
                .Select(component => component with { Weight = component.Weight / totalWeight })
                .ToArray();
    }

    private static Dictionary<ComponentKey, Dictionary<double, double>> CalculatePercentiles(
        IReadOnlyList<HotspotCandidate> candidates)
    {
        return candidates
            .SelectMany(candidate => candidate.Components.Select(component => new
            {
                Key = new ComponentKey(candidate.TargetKind, component.MetricId),
                component.Value
            }))
            .GroupBy(entry => entry.Key)
            .ToDictionary(
                group => group.Key,
                group => CalculatePercentiles(group.Select(entry => entry.Value).ToArray()));
    }

    private static Dictionary<double, double> CalculatePercentiles(IReadOnlyList<double> values)
    {
        var orderedValues = values
            .Order()
            .ToArray();

        if (orderedValues.Length == 0)
        {
            return [];
        }

        var percentiles = new Dictionary<double, double>();
        var cumulativeCount = 0;

        foreach (IGrouping<double, double> group in orderedValues.GroupBy(value => value))
        {
            cumulativeCount += group.Count();
            var value = group.Key;
            percentiles[value] = value <= 0
                ? 0.0
                : cumulativeCount / (double)orderedValues.Length;
        }

        return percentiles;
    }

    private static RankedHotspot ApplyPercentiles(
        HotspotCandidate candidate,
        IReadOnlyDictionary<ComponentKey, Dictionary<double, double>> percentiles)
    {
        IReadOnlyList<HotspotComponentLine> components = candidate.Components
            .Select(component =>
            {
                var key = new ComponentKey(candidate.TargetKind, component.MetricId);
                var percentile = percentiles[key][component.Value];

                return new HotspotComponentLine(
                    component.MetricId,
                    component.Value,
                    Round(percentile),
                    component.Weight);
            })
            .ToArray();
        var totalWeight = components.Sum(component => component.Weight);
        var score = totalWeight <= 0
            ? 0
            : components.Sum(component => component.Percentile * component.Weight) / totalWeight;

        return new RankedHotspot(
            candidate.TargetId,
            candidate.TargetKind,
            candidate.TargetName,
            candidate.TargetIdStability,
            candidate.FilePath,
            candidate.StartLine,
            candidate.EndLine,
            Rank: 0,
            Score: Round(score),
            Components: components);
    }

    private static HotspotLine ToHotspotLine(RankedHotspot hotspot)
    {
        return new HotspotLine
        {
            TargetId = hotspot.TargetId,
            TargetKind = hotspot.TargetKind,
            TargetName = hotspot.TargetName,
            Rank = hotspot.Rank,
            RankScore = hotspot.Score,
            FilePath = hotspot.FilePath,
            StartLine = hotspot.StartLine,
            EndLine = hotspot.EndLine,
            Reasons = hotspot.Components
                .Where(component => component.Percentile > 0)
                .OrderByDescending(component => component.Percentile * component.Weight)
                .ThenBy(component => component.MetricId, StringComparer.Ordinal)
                .Select(FormatReason)
                .ToArray(),
            Components = hotspot.Components
        };
    }

    private static string FormatReason(HotspotComponentLine component)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{component.MetricId}={component.Value:g} p{component.Percentile:0.##} w{component.Weight:0.##}");
    }

    private static MetricResultLine ToHotspotMetric(RankedHotspot hotspot)
    {
        return MetricResultFactory.Numeric(
            "hotspot_rank",
            hotspot.TargetKind,
            hotspot.TargetId,
            hotspot.TargetIdStability,
            hotspot.FilePath,
            hotspot.StartLine,
            hotspot.EndLine,
            hotspot.Rank);
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private sealed record HotspotComponentWeight(string MetricId, double Weight);

    private sealed record HotspotCandidate(
        string TargetId,
        string TargetKind,
        string TargetName,
        string TargetIdStability,
        string FilePath,
        int StartLine,
        int EndLine,
        IReadOnlyList<HotspotComponentValue> Components);

    private sealed record HotspotComponentValue(string MetricId, double Value, double Weight);

    private sealed record RankedHotspot(
        string TargetId,
        string TargetKind,
        string TargetName,
        string TargetIdStability,
        string FilePath,
        int StartLine,
        int EndLine,
        int Rank,
        double Score,
        IReadOnlyList<HotspotComponentLine> Components);

    private readonly record struct ComponentKey(string TargetId, string MetricId);
}
