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

    public static IReadOnlyList<MetricResultLine> Project(SyntaxAnalysisFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var typesById = facts.Types.ToDictionary(type => type.TargetId, StringComparer.Ordinal);
        var parentTypeByMemberId = facts.Members.ToDictionary(
            member => member.TargetId,
            member => member.ParentTypeTargetId,
            StringComparer.Ordinal);
        var outgoing = typesById.Keys.ToDictionary(
            targetId => targetId,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var incoming = typesById.Keys.ToDictionary(
            targetId => targetId,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (GraphEdgeFacts edge in facts.GraphEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TypeDependencyEdgeKinds.Contains(edge.Kind) ||
                !TryResolveSourceType(edge, parentTypeByMemberId, typesById, out var sourceTypeId) ||
                !typesById.ContainsKey(edge.To) ||
                string.Equals(sourceTypeId, edge.To, StringComparison.Ordinal))
            {
                continue;
            }

            outgoing[sourceTypeId].Add(edge.To);
            incoming[edge.To].Add(sourceTypeId);
        }

        HashSet<string> cyclicTypes = FindCyclicTypes(outgoing, cancellationToken);
        var metrics = new List<MetricResultLine>();

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            metrics.Add(CreateTypeMetric(type, "outgoing_type_dependency_count", outgoing[type.TargetId].Count, "count"));
            metrics.Add(CreateTypeMetric(type, "incoming_type_dependency_count", incoming[type.TargetId].Count, "count"));
            metrics.Add(CreateTypeMetric(type, "dependency_cycle_count", cyclicTypes.Contains(type.TargetId) ? 1 : 0, "count"));
        }

        return metrics;
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

    private static MetricResultLine CreateTypeMetric(TypeFacts type, string metricId, int value, string unit)
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
            unit);
    }

    private static HashSet<string> FindCyclicTypes(
        IReadOnlyDictionary<string, HashSet<string>> outgoing,
        CancellationToken cancellationToken)
    {
        var index = 0;
        var stack = new Stack<string>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var cyclicTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in outgoing.Keys.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!indexes.ContainsKey(node))
            {
                StrongConnect(node);
            }
        }

        return cyclicTypes;

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

            if (component.Count > 1 ||
                component.Any(componentNode => outgoing[componentNode].Contains(componentNode)))
            {
                foreach (var cyclicNode in component)
                {
                    cyclicTypes.Add(cyclicNode);
                }
            }
        }
    }
}
