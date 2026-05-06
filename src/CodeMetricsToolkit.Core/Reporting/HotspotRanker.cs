using System.Globalization;
using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class HotspotRanker
{
    private static readonly IReadOnlyDictionary<string, int> KindOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["member"] = 0,
        ["type"] = 1,
        ["file"] = 2
    };

    public static HotspotRanking Rank(SyntaxAnalysisFacts facts, int top, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        cancellationToken.ThrowIfCancellationRequested();

        List<HotspotCandidate> candidates = CreateMemberCandidates(facts)
            .Concat(CreateTypeCandidates(facts))
            .Concat(CreateFileCandidates(facts))
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
        IReadOnlyList<MetricResultLine> metrics = rankedHotspots
            .Select(ToHotspotMetric)
            .ToArray();

        return new HotspotRanking(hotspots, metrics);
    }

    private static IEnumerable<HotspotCandidate> CreateMemberCandidates(SyntaxAnalysisFacts facts)
    {
        foreach (MemberFacts member in facts.Members)
        {
            yield return new HotspotCandidate(
                member.TargetId,
                "member",
                member.Name,
                member.TargetIdStability,
                member.FilePath,
                member.StartLine,
                member.EndLine,
                [
                    new HotspotComponentValue("cyclomatic_complexity", member.ControlFlow.CyclomaticComplexity, 0.35),
                    new HotspotComponentValue("cognitive_complexity", member.ControlFlow.CognitiveComplexity, 0.35),
                    new HotspotComponentValue("nesting_depth", member.ControlFlow.NestingDepth, 0.20),
                    new HotspotComponentValue("method_length", member.MethodLength, 0.10)
                ]);
        }
    }

    private static IEnumerable<HotspotCandidate> CreateTypeCandidates(SyntaxAnalysisFacts facts)
    {
        foreach (TypeFacts type in facts.Types)
        {
            IReadOnlyList<MemberFacts> members = facts.Members
                .Where(member => member.ParentTypeTargetId == type.TargetId)
                .ToArray();

            yield return new HotspotCandidate(
                type.TargetId,
                "type",
                type.Name,
                type.TargetIdStability,
                type.FilePath,
                type.StartLine,
                type.EndLine,
                [
                    new HotspotComponentValue("max_member_cyclomatic_complexity", Max(members, member => member.ControlFlow.CyclomaticComplexity), 0.35),
                    new HotspotComponentValue("max_member_cognitive_complexity", Max(members, member => member.ControlFlow.CognitiveComplexity), 0.35),
                    new HotspotComponentValue("max_member_nesting_depth", Max(members, member => member.ControlFlow.NestingDepth), 0.20),
                    new HotspotComponentValue("lines_of_code", type.LinesOfCode, 0.10)
                ]);
        }
    }

    private static IEnumerable<HotspotCandidate> CreateFileCandidates(SyntaxAnalysisFacts facts)
    {
        foreach (FileFacts file in facts.Files)
        {
            IReadOnlyList<MemberFacts> members = facts.Members
                .Where(member => member.FilePath == file.FilePath)
                .ToArray();

            yield return new HotspotCandidate(
                file.TargetId,
                "file",
                Path.GetFileName(file.FilePath),
                file.TargetIdStability,
                file.FilePath,
                file.StartLine,
                file.EndLine,
                [
                    new HotspotComponentValue("max_member_cyclomatic_complexity", Max(members, member => member.ControlFlow.CyclomaticComplexity), 0.35),
                    new HotspotComponentValue("max_member_cognitive_complexity", Max(members, member => member.ControlFlow.CognitiveComplexity), 0.35),
                    new HotspotComponentValue("max_member_nesting_depth", Max(members, member => member.ControlFlow.NestingDepth), 0.20),
                    new HotspotComponentValue("lines_of_code", file.LinesOfCode, 0.10)
                ]);
        }
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
        double[] orderedValues = values
            .Distinct()
            .Order()
            .ToArray();

        if (orderedValues.Length == 0)
        {
            return [];
        }

        if (orderedValues.Length == 1)
        {
            double percentile = orderedValues[0] > 0 ? 1.0 : 0.0;

            return new Dictionary<double, double>
            {
                [orderedValues[0]] = percentile
            };
        }

        return orderedValues
            .Select((value, index) => new
            {
                Value = value,
                Percentile = value <= 0 ? 0.0 : index / (double)(orderedValues.Length - 1)
            })
            .ToDictionary(entry => entry.Value, entry => entry.Percentile);
    }

    private static RankedHotspot ApplyPercentiles(
        HotspotCandidate candidate,
        IReadOnlyDictionary<ComponentKey, Dictionary<double, double>> percentiles)
    {
        IReadOnlyList<HotspotComponentLine> components = candidate.Components
            .Select(component =>
            {
                var key = new ComponentKey(candidate.TargetKind, component.MetricId);
                double percentile = percentiles[key][component.Value];

                return new HotspotComponentLine(
                    component.MetricId,
                    component.Value,
                    Round(percentile),
                    component.Weight);
            })
            .ToArray();
        double totalWeight = components.Sum(component => component.Weight);
        double score = totalWeight <= 0
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
        return new MetricResultLine
        {
            SchemaVersion = ContractVersion.Current,
            MetricId = "hotspot_rank",
            MetricVersion = "1.0.0",
            TargetId = hotspot.TargetId,
            TargetKind = hotspot.TargetKind,
            TargetIdStability = hotspot.TargetIdStability,
            ValueKind = "integer",
            NumericValue = hotspot.Rank,
            Unit = "rank",
            FilePath = hotspot.FilePath,
            StartLine = hotspot.StartLine,
            EndLine = hotspot.EndLine
        };
    }

    private static int Max(IReadOnlyList<MemberFacts> members, Func<MemberFacts, int> selector)
    {
        return members.Count == 0 ? 0 : members.Max(selector);
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    private sealed record HotspotCandidate(
        string TargetId,
        string TargetKind,
        string TargetName,
        string TargetIdStability,
        string FilePath,
        int StartLine,
        int EndLine,
        IReadOnlyList<HotspotComponentValue> Components);

    private sealed record HotspotComponentValue(
        string MetricId,
        double Value,
        double Weight);

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

    private sealed record ComponentKey(string TargetKind, string MetricId);
}
