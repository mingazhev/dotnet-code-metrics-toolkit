using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class GraphMetricProjector
{
    private static readonly HashSet<string> TypeDependencyEdgeKinds = new(StringComparer.Ordinal)
    {
        "inherits",
        "implements",
        "uses_type"
    };

    public static IReadOnlyList<MetricResultLine> Project(
        SyntaxAnalysisFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var typesById = facts.Types.ToDictionary(type => type.TargetId, StringComparer.Ordinal);
        var membersById = facts.Members.ToDictionary(member => member.TargetId, StringComparer.Ordinal);
        var parentTypeByMemberId = facts.Members.ToDictionary(
            member => member.TargetId,
            member => member.ParentTypeTargetId,
            StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> typeOutgoing = CreateAdjacency(typesById.Keys);
        Dictionary<string, HashSet<string>> typeIncoming = CreateAdjacency(typesById.Keys);
        Dictionary<string, HashSet<string>> callOutgoing = CreateAdjacency(membersById.Keys);
        Dictionary<string, HashSet<string>> callIncoming = CreateAdjacency(membersById.Keys);
        IReadOnlyDictionary<string, IReadOnlyList<string>> memberIdsByType = facts.Members
            .GroupBy(member => member.ParentTypeTargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(member => member.TargetId)
                    .ToArray(),
                StringComparer.Ordinal);

        foreach (GraphEdgeFacts edge in facts.GraphEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TypeDependencyEdgeKinds.Contains(edge.Kind) &&
                TryResolveSourceType(edge, parentTypeByMemberId, typesById, out var sourceTypeId) &&
                typesById.ContainsKey(edge.To) &&
                !string.Equals(sourceTypeId, edge.To, StringComparison.Ordinal))
            {
                typeOutgoing[sourceTypeId].Add(edge.To);
                typeIncoming[edge.To].Add(sourceTypeId);
            }

            if (string.Equals(edge.Kind, "calls", StringComparison.Ordinal) &&
                membersById.ContainsKey(edge.From) &&
                membersById.ContainsKey(edge.To))
            {
                callOutgoing[edge.From].Add(edge.To);
                callIncoming[edge.To].Add(edge.From);
            }
        }

        Dictionary<string, int> dependencyComponentSizes = FindCyclicComponentSizes(
            typeOutgoing,
            cancellationToken);
        var metrics = new List<MetricResultLine>();

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            metrics.Add(CreateTypeMetric(type, "outgoing_type_dependency_count", typeOutgoing[type.TargetId].Count));
            metrics.Add(CreateTypeMetric(type, "incoming_type_dependency_count", typeIncoming[type.TargetId].Count));
            metrics.Add(CreateTypeMetric(
                type,
                "dependency_cycle_count",
                dependencyComponentSizes.GetValueOrDefault(type.TargetId) > 0 ? 1 : 0));
        }

        if (!string.Equals(facts.Health.AnalysisQuality, "trusted", StringComparison.Ordinal))
        {
            return metrics;
        }

        Dictionary<string, int> recursiveComponentSizes = FindCyclicComponentSizes(
            callOutgoing,
            cancellationToken);

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            metrics.Add(CreateMemberMetric(member, "outgoing_call_count", callOutgoing[member.TargetId].Count));
            metrics.Add(CreateMemberMetric(member, "incoming_call_count", callIncoming[member.TargetId].Count));
            metrics.Add(CreateMemberMetric(
                member,
                "recursive_component_size",
                recursiveComponentSizes.GetValueOrDefault(member.TargetId)));
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<string> memberIds = memberIdsByType.GetValueOrDefault(
                type.TargetId,
                []);
            var outgoingCallees = memberIds
                .SelectMany(memberId => callOutgoing[memberId])
                .ToHashSet(StringComparer.Ordinal);
            var incomingCallers = memberIds
                .SelectMany(memberId => callIncoming[memberId])
                .ToHashSet(StringComparer.Ordinal);

            metrics.Add(CreateTypeMetric(type, "outgoing_call_count", outgoingCallees.Count));
            metrics.Add(CreateTypeMetric(type, "incoming_call_count", incomingCallers.Count));
            metrics.Add(CreateTypeMetric(
                type,
                "dependency_component_size",
                dependencyComponentSizes.GetValueOrDefault(type.TargetId)));
            metrics.Add(CreateTypeMetric(
                type,
                "transitive_type_dependency_count",
                CountReachable(type.TargetId, typeOutgoing, cancellationToken)));
            metrics.Add(CreateTypeMetric(
                type,
                "transitive_type_dependent_count",
                CountReachable(type.TargetId, typeIncoming, cancellationToken)));
        }

        return metrics;
    }

    private static Dictionary<string, HashSet<string>> CreateAdjacency(IEnumerable<string> targetIds)
    {
        return targetIds.ToDictionary(
            targetId => targetId,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
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

    private static MetricResultLine CreateTypeMetric(TypeFacts type, string metricId, int value)
    {
        return MetricResultFactory.Numeric(
            metricId,
            "1.0.0",
            "type",
            type.TargetId,
            type.TargetIdStability,
            type.FilePath,
            type.StartLine,
            type.EndLine,
            value,
            "count");
    }

    private static MetricResultLine CreateMemberMetric(MemberFacts member, string metricId, int value)
    {
        return MetricResultFactory.Numeric(
            metricId,
            "1.0.0",
            "member",
            member.TargetId,
            member.TargetIdStability,
            member.FilePath,
            member.StartLine,
            member.EndLine,
            value,
            "count");
    }

    private static int CountReachable(
        string start,
        Dictionary<string, HashSet<string>> adjacency,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { start };
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            foreach (var target in adjacency[current])
            {
                if (visited.Add(target))
                {
                    pending.Push(target);
                }
            }
        }

        return visited.Count - 1;
    }

    private static Dictionary<string, int> FindCyclicComponentSizes(
        IReadOnlyDictionary<string, HashSet<string>> outgoing,
        CancellationToken cancellationToken)
    {
        var index = 0;
        var stack = new Stack<string>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var componentSizes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in outgoing.Keys.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!indexes.ContainsKey(node))
            {
                StrongConnect(node);
            }
        }

        return componentSizes;

        void StrongConnect(string node)
        {
            cancellationToken.ThrowIfCancellationRequested();

            indexes[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var target in outgoing[node].Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!indexes.TryGetValue(target, out var targetIndex))
                {
                    StrongConnect(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], targetIndex);
                }
            }

            if (lowLinks[node] != indexes[node])
            {
                return;
            }

            var component = new List<string>();
            string current;

            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            }
            while (!string.Equals(current, node, StringComparison.Ordinal));

            var isCyclic = component.Count > 1 ||
                component.Any(componentNode => outgoing[componentNode].Contains(componentNode));
            if (!isCyclic)
            {
                return;
            }

            foreach (var cyclicNode in component)
            {
                componentSizes[cyclicNode] = component.Count;
            }
        }
    }
}
