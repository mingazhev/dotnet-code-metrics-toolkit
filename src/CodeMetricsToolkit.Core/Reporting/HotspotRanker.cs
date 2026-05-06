using System.Globalization;
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

        HotspotContext context = HotspotContext.Create(facts);
        List<HotspotCandidate> candidates = CreateMemberCandidates(facts, context)
            .Concat(CreateTypeCandidates(facts, context))
            .Concat(CreateFileCandidates(facts, context))
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

    private static IEnumerable<HotspotCandidate> CreateMemberCandidates(SyntaxAnalysisFacts facts, HotspotContext context)
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
                    new HotspotComponentValue("cognitive_complexity", member.ControlFlow.CognitiveComplexity, 0.30),
                    new HotspotComponentValue("cyclomatic_complexity", member.ControlFlow.CyclomaticComplexity, 0.25),
                    new HotspotComponentValue("nesting_depth", member.ControlFlow.NestingDepth, 0.15),
                    new HotspotComponentValue("method_length", member.MethodLength, 0.15),
                    new HotspotComponentValue("diagnostic_count", context.DiagnosticCount(member.FilePath, member.StartLine, member.EndLine), 0.10),
                    new HotspotComponentValue("parameter_count", member.ParameterCount, 0.05)
                ]);
        }
    }

    private static IEnumerable<HotspotCandidate> CreateTypeCandidates(SyntaxAnalysisFacts facts, HotspotContext context)
    {
        foreach (TypeFacts type in facts.Types)
        {
            IReadOnlyList<MemberFacts> members = context.MembersForType(type.TargetId);

            yield return new HotspotCandidate(
                type.TargetId,
                "type",
                type.Name,
                type.TargetIdStability,
                type.FilePath,
                type.StartLine,
                type.EndLine,
                [
                    new HotspotComponentValue("max_member_cognitive_complexity", Max(members, member => member.ControlFlow.CognitiveComplexity), 0.25),
                    new HotspotComponentValue("p95_member_cyclomatic_complexity", Percentile(members, member => member.ControlFlow.CyclomaticComplexity, 0.95), 0.20),
                    new HotspotComponentValue("outgoing_type_dependency_count", context.OutgoingTypeDependencyCount(type.TargetId), 0.20),
                    new HotspotComponentValue("lines_of_code", type.LinesOfCode, 0.15),
                    new HotspotComponentValue("member_count", type.MemberCount, 0.10),
                    new HotspotComponentValue("diagnostic_count", context.DiagnosticCount(type.FilePath, type.StartLine, type.EndLine), 0.10)
                ]);
        }
    }

    private static IEnumerable<HotspotCandidate> CreateFileCandidates(SyntaxAnalysisFacts facts, HotspotContext context)
    {
        foreach (FileFacts file in facts.Files)
        {
            IReadOnlyList<MemberFacts> members = context.MembersForFile(file.FilePath);

            yield return new HotspotCandidate(
                file.TargetId,
                "file",
                Path.GetFileName(file.FilePath),
                file.TargetIdStability,
                file.FilePath,
                file.StartLine,
                file.EndLine,
                [
                    new HotspotComponentValue("p95_member_cognitive_complexity", Percentile(members, member => member.ControlFlow.CognitiveComplexity, 0.95), 0.25),
                    new HotspotComponentValue("p95_member_cyclomatic_complexity", Percentile(members, member => member.ControlFlow.CyclomaticComplexity, 0.95), 0.20),
                    new HotspotComponentValue("lines_of_code", file.LinesOfCode, 0.20),
                    new HotspotComponentValue("diagnostic_count", context.DiagnosticCount(file.FilePath, file.StartLine, file.EndLine), 0.15),
                    new HotspotComponentValue("type_count", context.TypeCountForFile(file.FilePath), 0.10),
                    new HotspotComponentValue("member_count", members.Count, 0.10)
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
        return MetricResultFactory.Numeric(
            "hotspot_rank",
            "1.0.0",
            hotspot.TargetKind,
            hotspot.TargetId,
            hotspot.TargetIdStability,
            hotspot.FilePath,
            hotspot.StartLine,
            hotspot.EndLine,
            hotspot.Rank,
            "rank");
    }

    private static int Max(IReadOnlyList<MemberFacts> members, Func<MemberFacts, int> selector)
    {
        return members.Count == 0 ? 0 : members.Max(selector);
    }

    private static int Percentile(IReadOnlyList<MemberFacts> members, Func<MemberFacts, int> selector, double percentile)
    {
        if (members.Count == 0)
        {
            return 0;
        }

        int[] orderedValues = members
            .Select(selector)
            .Order()
            .ToArray();
        int index = (int)Math.Ceiling(percentile * orderedValues.Length) - 1;

        return orderedValues[Math.Clamp(index, 0, orderedValues.Length - 1)];
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

    private sealed class HotspotContext
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> _membersByType;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> _membersByFile;
        private readonly IReadOnlyDictionary<string, int> _typeCountByFile;
        private readonly IReadOnlyDictionary<string, int> _outgoingDependencyCountByType;
        private readonly IReadOnlyList<AnalysisDiagnostic> _diagnostics;

        private HotspotContext(
            IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> membersByType,
            IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> membersByFile,
            IReadOnlyDictionary<string, int> typeCountByFile,
            IReadOnlyDictionary<string, int> outgoingDependencyCountByType,
            IReadOnlyList<AnalysisDiagnostic> diagnostics)
        {
            _membersByType = membersByType;
            _membersByFile = membersByFile;
            _typeCountByFile = typeCountByFile;
            _outgoingDependencyCountByType = outgoingDependencyCountByType;
            _diagnostics = diagnostics;
        }

        public static HotspotContext Create(SyntaxAnalysisFacts facts)
        {
            Dictionary<string, TypeFacts> typesById = facts.Types.ToDictionary(type => type.TargetId, StringComparer.Ordinal);
            Dictionary<string, string> parentTypeByMemberId = facts.Members.ToDictionary(member => member.TargetId, member => member.ParentTypeTargetId, StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> outgoingDependencies = typesById.Keys.ToDictionary(
                targetId => targetId,
                _ => new HashSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);

            foreach (GraphEdgeFacts edge in facts.GraphEdges)
            {
                if (!IsTypeDependencyEdge(edge) ||
                    !TryResolveSourceType(edge, parentTypeByMemberId, typesById, out string? sourceTypeId) ||
                    !typesById.ContainsKey(edge.To) ||
                    string.Equals(sourceTypeId, edge.To, StringComparison.Ordinal))
                {
                    continue;
                }

                outgoingDependencies[sourceTypeId].Add(edge.To);
            }

            return new HotspotContext(
                facts.Members
                    .GroupBy(member => member.ParentTypeTargetId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<MemberFacts>)group.ToArray(), StringComparer.Ordinal),
                facts.Members
                    .GroupBy(member => member.FilePath, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<MemberFacts>)group.ToArray(), StringComparer.Ordinal),
                facts.Types
                    .GroupBy(type => type.FilePath, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                outgoingDependencies.ToDictionary(entry => entry.Key, entry => entry.Value.Count, StringComparer.Ordinal),
                facts.Health.DiagnosticsIncludedInHotspotRank ? facts.Diagnostics : []);
        }

        public IReadOnlyList<MemberFacts> MembersForType(string targetId)
        {
            return _membersByType.GetValueOrDefault(targetId, []);
        }

        public IReadOnlyList<MemberFacts> MembersForFile(string filePath)
        {
            return _membersByFile.GetValueOrDefault(filePath, []);
        }

        public int TypeCountForFile(string filePath)
        {
            return _typeCountByFile.GetValueOrDefault(filePath);
        }

        public int OutgoingTypeDependencyCount(string targetId)
        {
            return _outgoingDependencyCountByType.GetValueOrDefault(targetId);
        }

        public int DiagnosticCount(string filePath, int startLine, int endLine)
        {
            return _diagnostics.Count(diagnostic =>
                string.Equals(diagnostic.FilePath, filePath, StringComparison.Ordinal) &&
                (diagnostic.StartLine is null ||
                    diagnostic.EndLine is null ||
                    (diagnostic.StartLine <= endLine && diagnostic.EndLine >= startLine)));
        }

        private static bool IsTypeDependencyEdge(GraphEdgeFacts edge)
        {
            return string.Equals(edge.Kind, "inherits", StringComparison.Ordinal) ||
                string.Equals(edge.Kind, "implements", StringComparison.Ordinal) ||
                string.Equals(edge.Kind, "uses_type", StringComparison.Ordinal);
        }

        private static bool TryResolveSourceType(
            GraphEdgeFacts edge,
            Dictionary<string, string> parentTypeByMemberId,
            Dictionary<string, TypeFacts> typesById,
            out string sourceTypeId)
        {
            if (typesById.ContainsKey(edge.From))
            {
                sourceTypeId = edge.From;
                return true;
            }

            return parentTypeByMemberId.TryGetValue(edge.From, out sourceTypeId!);
        }
    }
}
