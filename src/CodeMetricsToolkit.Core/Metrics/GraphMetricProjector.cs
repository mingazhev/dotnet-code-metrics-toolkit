using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Discovery;

namespace CodeMetricsToolkit.Core.Metrics;

public static class GraphMetricProjector
{
    private static readonly HashSet<string> TypeDependencyEdgeKinds = new(StringComparer.Ordinal)
    {
        "inherits",
        "implements",
        "uses_type"
    };

    public static IReadOnlyList<MetricResultLine> Project(
        AnalysisFacts facts,
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
        var projectTargetIds = facts.AnalyzedProjectPaths
            .Select(ProjectIdentity.TargetId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, HashSet<string>> projectOutgoing = CreateAdjacency(projectTargetIds);
        Dictionary<string, HashSet<string>> projectIncoming = CreateAdjacency(projectTargetIds);
        Dictionary<string, HashSet<string>> namespaceOutgoing = CreateAdjacency(
            facts.Namespaces.Select(namespaceFacts => namespaceFacts.TargetId));
        Dictionary<string, HashSet<string>> namespaceIncoming = CreateAdjacency(
            facts.Namespaces.Select(namespaceFacts => namespaceFacts.TargetId));
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

            if (string.Equals(edge.Kind, "depends_on", StringComparison.Ordinal) &&
                namespaceOutgoing.TryGetValue(edge.From, out HashSet<string>? sourceNamespaceDependencies) &&
                namespaceIncoming.TryGetValue(edge.To, out HashSet<string>? targetNamespaceDependents))
            {
                sourceNamespaceDependencies.Add(edge.To);
                targetNamespaceDependents.Add(edge.From);
            }
        }

        foreach (ProjectDependencyFacts dependency in facts.ProjectDependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var from = ProjectIdentity.TargetId(dependency.FromProjectPath);
            var to = ProjectIdentity.TargetId(dependency.ToProjectPath);
            if (projectOutgoing.TryGetValue(from, out HashSet<string>? sourceProjectDependencies) &&
                projectIncoming.TryGetValue(to, out HashSet<string>? targetProjectDependents))
            {
                sourceProjectDependencies.Add(to);
                targetProjectDependents.Add(from);
            }
        }

        if (!string.Equals(facts.Health.AnalysisQuality, "trusted", StringComparison.Ordinal))
        {
            return [];
        }

        DirectedGraphAnalysis dependencyGraph = DirectedGraphAnalyzer.Analyze(
            typeOutgoing,
            typeIncoming,
            includeReachability: true,
            cancellationToken);
        var metrics = new List<MetricResultLine>();

        DirectedGraphAnalysis projectDependencyGraph = DirectedGraphAnalyzer.Analyze(
            projectOutgoing,
            projectIncoming,
            includeReachability: false,
            cancellationToken);
        foreach (var projectPath in facts.AnalyzedProjectPaths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetId = ProjectIdentity.TargetId(projectPath);
            metrics.Add(CreateProjectMetric(projectPath, "outgoing_project_dependency_count", projectOutgoing[targetId].Count));
            metrics.Add(CreateProjectMetric(projectPath, "incoming_project_dependency_count", projectIncoming[targetId].Count));
            var componentSize = projectDependencyGraph.GetCyclicComponentSize(targetId);
            metrics.Add(CreateProjectMetric(projectPath, "project_dependency_cycle_membership", componentSize > 0 ? 1 : 0));
            metrics.Add(CreateProjectMetric(projectPath, "project_dependency_component_size", componentSize));
        }

        DirectedGraphAnalysis namespaceDependencyGraph = DirectedGraphAnalyzer.Analyze(
            namespaceOutgoing,
            namespaceIncoming,
            includeReachability: false,
            cancellationToken);
        foreach (NamespaceFacts namespaceFacts in facts.Namespaces.OrderBy(
                     namespaceFacts => namespaceFacts.TargetId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            metrics.Add(CreateNamespaceMetric(
                namespaceFacts,
                "outgoing_namespace_dependency_count",
                namespaceOutgoing[namespaceFacts.TargetId].Count));
            metrics.Add(CreateNamespaceMetric(
                namespaceFacts,
                "incoming_namespace_dependency_count",
                namespaceIncoming[namespaceFacts.TargetId].Count));
            var componentSize = namespaceDependencyGraph.GetCyclicComponentSize(namespaceFacts.TargetId);
            metrics.Add(CreateNamespaceMetric(
                namespaceFacts,
                "namespace_dependency_cycle_membership",
                componentSize > 0 ? 1 : 0));
            metrics.Add(CreateNamespaceMetric(
                namespaceFacts,
                "namespace_dependency_component_size",
                componentSize));
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            metrics.Add(CreateTypeMetric(type, "outgoing_type_dependency_count", typeOutgoing[type.TargetId].Count));
            metrics.Add(CreateTypeMetric(type, "incoming_type_dependency_count", typeIncoming[type.TargetId].Count));
            metrics.Add(CreateTypeMetric(
                type,
                "dependency_cycle_membership",
                dependencyGraph.GetCyclicComponentSize(type.TargetId) > 0 ? 1 : 0));
        }

        DirectedGraphAnalysis callGraph = DirectedGraphAnalyzer.Analyze(
            callOutgoing,
            callIncoming,
            includeReachability: false,
            cancellationToken);

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            metrics.Add(CreateMemberMetric(member, "distinct_outgoing_callee_count", callOutgoing[member.TargetId].Count));
            metrics.Add(CreateMemberMetric(member, "distinct_incoming_caller_count", callIncoming[member.TargetId].Count));
            metrics.Add(CreateMemberMetric(
                member,
                "recursive_component_size",
                callGraph.GetCyclicComponentSize(member.TargetId)));
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

            metrics.Add(CreateTypeMetric(type, "distinct_outgoing_callee_count", outgoingCallees.Count));
            metrics.Add(CreateTypeMetric(type, "distinct_incoming_caller_count", incomingCallers.Count));
            metrics.Add(CreateTypeMetric(
                type,
                "dependency_component_size",
                dependencyGraph.GetCyclicComponentSize(type.TargetId)));
            metrics.Add(CreateTypeMetric(
                type,
                "transitive_type_dependency_count",
                dependencyGraph.GetTransitiveOutgoingCount(type.TargetId)));
            metrics.Add(CreateTypeMetric(
                type,
                "transitive_type_dependent_count",
                dependencyGraph.GetTransitiveIncomingCount(type.TargetId)));
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

    private static MetricResultLine CreateTypeMetric(
        TypeFacts type,
        string metricId,
        int value)
    {
        return MetricResultFactory.Numeric(
            metricId,
            "type",
            type.TargetId,
            type.TargetIdStability,
            type.FilePath,
            type.StartLine,
            type.EndLine,
            value);
    }

    private static MetricResultLine CreateMemberMetric(MemberFacts member, string metricId, int value)
    {
        return MetricResultFactory.Numeric(
            metricId,
            "member",
            member.TargetId,
            member.TargetIdStability,
            member.FilePath,
            member.StartLine,
            member.EndLine,
            value);
    }

    private static MetricResultLine CreateProjectMetric(string projectPath, string metricId, int value)
    {
        return MetricResultFactory.Numeric(
            metricId,
            "project",
            ProjectIdentity.TargetId(projectPath),
            "syntax_fallback",
            projectPath,
            1,
            1,
            value);
    }

    private static MetricResultLine CreateNamespaceMetric(
        NamespaceFacts namespaceFacts,
        string metricId,
        int value)
    {
        return MetricResultFactory.Numeric(
            metricId,
            "namespace",
            namespaceFacts.TargetId,
            "syntax_fallback",
            namespaceFacts.ProjectPath,
            1,
            1,
            value);
    }

}
