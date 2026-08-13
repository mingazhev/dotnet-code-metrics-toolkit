using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class SemanticMetricProjector
{
    public static IReadOnlyList<MetricResultLine> Project(
        SyntaxAnalysisFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!string.Equals(facts.Health.AnalysisQuality, "trusted", StringComparison.Ordinal))
        {
            return [];
        }

        var metrics = new List<MetricResultLine>();

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.Semantic is null)
            {
                continue;
            }

            AddTypeMetric(metrics, type, "inheritance_depth", type.Semantic.InheritanceDepth, "levels");
            AddTypeMetric(metrics, type, "type_coupling", type.Semantic.ClassCoupling, "count");
            AddTypeMetric(metrics, type, "public_api_count", type.Semantic.PublicApiCount, "count");
            AddTypeMetric(metrics, type, "documented_public_api_count", type.Semantic.DocumentedPublicApiCount, "count");
            if (type.Semantic.PublicApiCount > 0)
            {
                AddTypeRatioMetric(
                    metrics,
                    type,
                    "public_api_documentation_ratio",
                    (double)type.Semantic.DocumentedPublicApiCount / type.Semantic.PublicApiCount);
            }
        }

        foreach (var projectPath in facts.ProjectPaths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectKey = ProjectIdentity.Key(projectPath);
            TypeSemanticFacts[] typeFacts = facts.Types
                .Where(type => type.ProjectKey == projectKey && type.Semantic is not null)
                .Select(type => type.Semantic!)
                .ToArray();
            var publicApiCount = typeFacts.Sum(type => type.PublicApiCount);
            var documentedPublicApiCount = typeFacts.Sum(type => type.DocumentedPublicApiCount);

            AddProjectMetric(metrics, projectPath, "public_api_count", publicApiCount, "count");
            AddProjectMetric(
                metrics,
                projectPath,
                "documented_public_api_count",
                documentedPublicApiCount,
                "count");
            if (publicApiCount > 0)
            {
                AddProjectRatioMetric(
                    metrics,
                    projectPath,
                    "public_api_documentation_ratio",
                    (double)documentedPublicApiCount / publicApiCount);
            }
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.Operations is null)
            {
                continue;
            }

            AddMemberMetric(metrics, member, "operation_count", member.Operations.OperationCount);
            AddMemberMetric(metrics, member, "allocation_count", member.Operations.AllocationCount);
            AddMemberMetric(metrics, member, "await_count", member.Operations.AwaitCount);
            AddMemberMetric(metrics, member, "control_flow_graph_count", member.Operations.ControlFlowGraphCount);
            AddMemberMetric(metrics, member, "basic_block_count", member.Operations.BasicBlockCount);
            AddMemberMetric(metrics, member, "reachable_basic_block_count", member.Operations.ReachableBasicBlockCount);
            AddMemberMetric(metrics, member, "unreachable_basic_block_count", member.Operations.UnreachableBasicBlockCount);
            AddMemberMetric(metrics, member, "control_flow_edge_count", member.Operations.ControlFlowEdgeCount);
            if (member.Operations.ControlFlowGraphCount > 0)
            {
                AddMemberMetric(
                    metrics,
                    member,
                    "cfg_cyclomatic_complexity",
                    member.Operations.CfgCyclomaticComplexity);
            }
        }

        return metrics;
    }

    private static void AddTypeMetric(
        List<MetricResultLine> metrics,
        TypeFacts type,
        string metricId,
        double value,
        string unit)
    {
        metrics.Add(MetricResultFactory.Numeric(
            metricId,
            "1.0.0",
            "type",
            type.TargetId,
            type.TargetIdStability,
            type.FilePath,
            type.StartLine,
            type.EndLine,
            value,
            unit));
    }

    private static void AddProjectMetric(
        List<MetricResultLine> metrics,
        string projectPath,
        string metricId,
        double value,
        string unit)
    {
        metrics.Add(MetricResultFactory.Numeric(
            metricId,
            "1.0.0",
            "project",
            ProjectIdentity.TargetId(projectPath),
            "syntax_fallback",
            projectPath,
            1,
            1,
            value,
            unit));
    }

    private static void AddTypeRatioMetric(
        List<MetricResultLine> metrics,
        TypeFacts type,
        string metricId,
        double value)
    {
        metrics.Add(MetricResultFactory.Number(
            metricId,
            "1.0.0",
            "type",
            type.TargetId,
            type.TargetIdStability,
            type.FilePath,
            type.StartLine,
            type.EndLine,
            value,
            "ratio"));
    }

    private static void AddProjectRatioMetric(
        List<MetricResultLine> metrics,
        string projectPath,
        string metricId,
        double value)
    {
        metrics.Add(MetricResultFactory.Number(
            metricId,
            "1.0.0",
            "project",
            ProjectIdentity.TargetId(projectPath),
            "syntax_fallback",
            projectPath,
            1,
            1,
            value,
            "ratio"));
    }

    private static void AddMemberMetric(
        List<MetricResultLine> metrics,
        MemberFacts member,
        string metricId,
        int value)
    {
        metrics.Add(MetricResultFactory.Numeric(
            metricId,
            "1.0.0",
            "member",
            member.TargetId,
            member.TargetIdStability,
            member.FilePath,
            member.StartLine,
            member.EndLine,
            value,
            "count"));
    }
}
