using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class GraphMetricProjectorTests
{
    private static readonly string[] DegradedMetricIds =
    [
        "dependency_cycle_membership",
        "incoming_type_dependency_count",
        "outgoing_type_dependency_count"
    ];

    [Fact]
    public void ProjectComputesCallDependencyTransitiveAndSccMetricsExactly()
    {
        TypeFacts typeA = TestFacts.Type("type:a", "file:a", "A.cs");
        TypeFacts typeB = TestFacts.Type("type:b", "file:b", "B.cs");
        TypeFacts typeC = TestFacts.Type("type:c", "file:c", "C.cs");
        MemberFacts memberA = TestFacts.Member("member:a", typeA.TargetId, typeA.FilePath);
        MemberFacts memberB = TestFacts.Member("member:b", typeB.TargetId, typeB.FilePath);
        MemberFacts memberC = TestFacts.Member("member:c", typeC.TargetId, typeC.FilePath);
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            types: [typeA, typeB, typeC],
            members: [memberA, memberB, memberC],
            edges:
            [
                TestFacts.Edge(typeA.TargetId, typeB.TargetId, "uses_type"),
                TestFacts.Edge(typeB.TargetId, typeA.TargetId, "uses_type"),
                TestFacts.Edge(typeB.TargetId, typeC.TargetId, "inherits"),
                TestFacts.Edge(memberA.TargetId, memberB.TargetId, "calls"),
                TestFacts.Edge(memberB.TargetId, memberA.TargetId, "calls"),
                TestFacts.Edge(memberC.TargetId, memberC.TargetId, "calls")
            ]);

        IReadOnlyList<MetricResultLine> metrics = GraphMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Equal(
            MetricFamilies.Graph.Order(StringComparer.Ordinal),
            metrics.Select(metric => metric.MetricId).Distinct().Order(StringComparer.Ordinal));
        AssertMetric(metrics, typeA.TargetId, "outgoing_type_dependency_count", 1);
        AssertMetric(metrics, typeA.TargetId, "incoming_type_dependency_count", 1);
        AssertMetric(metrics, typeA.TargetId, "dependency_cycle_membership", 1);
        AssertMetric(metrics, typeA.TargetId, "dependency_component_size", 2);
        AssertMetric(metrics, typeA.TargetId, "transitive_type_dependency_count", 2);
        AssertMetric(metrics, typeC.TargetId, "transitive_type_dependent_count", 2);
        AssertMetric(metrics, memberA.TargetId, "distinct_outgoing_callee_count", 1);
        AssertMetric(metrics, memberA.TargetId, "distinct_incoming_caller_count", 1);
        AssertMetric(metrics, memberA.TargetId, "recursive_component_size", 2);
        AssertMetric(metrics, memberC.TargetId, "recursive_component_size", 1);
        AssertMetric(metrics, typeA.TargetId, "distinct_outgoing_callee_count", 1);
        AssertMetric(metrics, typeC.TargetId, "distinct_incoming_caller_count", 1);
    }

    [Fact]
    public void ProjectCountsDistinctReachabilityAcrossCyclicBranchingGraph()
    {
        TypeFacts typeA = TestFacts.Type("type:a", "file:a", "A.cs");
        TypeFacts typeB = TestFacts.Type("type:b", "file:b", "B.cs");
        TypeFacts typeC = TestFacts.Type("type:c", "file:c", "C.cs");
        TypeFacts typeD = TestFacts.Type("type:d", "file:d", "D.cs");
        TypeFacts typeE = TestFacts.Type("type:e", "file:e", "E.cs");
        TypeFacts typeF = TestFacts.Type("type:f", "file:f", "F.cs");
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            types: [typeA, typeB, typeC, typeD, typeE, typeF],
            edges:
            [
                TestFacts.Edge(typeA.TargetId, typeB.TargetId, "uses_type"),
                TestFacts.Edge(typeB.TargetId, typeA.TargetId, "uses_type"),
                TestFacts.Edge(typeA.TargetId, typeC.TargetId, "uses_type"),
                TestFacts.Edge(typeB.TargetId, typeD.TargetId, "uses_type"),
                TestFacts.Edge(typeC.TargetId, typeE.TargetId, "uses_type"),
                TestFacts.Edge(typeD.TargetId, typeE.TargetId, "uses_type")
            ]);

        IReadOnlyList<MetricResultLine> metrics = GraphMetricProjector.Project(
            facts,
            CancellationToken.None);

        AssertMetric(metrics, typeA.TargetId, "dependency_component_size", 2);
        AssertMetric(metrics, typeB.TargetId, "dependency_component_size", 2);
        AssertMetric(metrics, typeA.TargetId, "transitive_type_dependency_count", 4);
        AssertMetric(metrics, typeB.TargetId, "transitive_type_dependency_count", 4);
        AssertMetric(metrics, typeE.TargetId, "transitive_type_dependent_count", 4);
        AssertMetric(metrics, typeC.TargetId, "transitive_type_dependent_count", 2);
        AssertMetric(metrics, typeF.TargetId, "transitive_type_dependency_count", 0);
        AssertMetric(metrics, typeF.TargetId, "transitive_type_dependent_count", 0);
    }

    [Fact]
    public void ProjectHandlesDeepDependencyChainWithoutRecursiveTraversal()
    {
        const int typeCount = 12_000;
        TypeFacts[] types = Enumerable
            .Range(0, typeCount)
            .Select(index => TestFacts.Type($"type:{index}", $"file:{index}", $"{index}.cs"))
            .ToArray();
        GraphEdgeFacts[] edges = Enumerable
            .Range(0, typeCount - 1)
            .Select(index => TestFacts.Edge(
                types[index].TargetId,
                types[index + 1].TargetId,
                "uses_type"))
            .ToArray();

        IReadOnlyList<MetricResultLine> metrics = GraphMetricProjector.Project(
            TestFacts.Analysis(types: types, edges: edges),
            CancellationToken.None);

        AssertMetric(metrics, types[0].TargetId, "transitive_type_dependency_count", typeCount - 1);
        AssertMetric(metrics, types[^1].TargetId, "transitive_type_dependent_count", typeCount - 1);
        AssertMetric(metrics, types[typeCount / 2].TargetId, "dependency_component_size", 0);
    }

    [Fact]
    public void ProjectSuppressesTrustedOnlyGraphMetricsForDegradedAnalysis()
    {
        TypeFacts type = TestFacts.Type("type:a", "file:a", "A.cs");
        SyntaxAnalysisFacts facts = TestFacts.Analysis(types: [type], trusted: false);

        IReadOnlyList<MetricResultLine> metrics = GraphMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Equal(
            DegradedMetricIds,
            metrics.Select(metric => metric.MetricId).Order(StringComparer.Ordinal));
    }

    private static void AssertMetric(
        IEnumerable<MetricResultLine> metrics,
        string targetId,
        string metricId,
        double expected)
    {
        MetricResultLine metric = Assert.Single(
            metrics,
            candidate => candidate.TargetId == targetId && candidate.MetricId == metricId);
        Assert.Equal(expected, metric.NumericValue);
    }
}
