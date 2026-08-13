using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class GraphMetricProjectorTests
{
    private static readonly string[] DegradedMetricIds =
    [
        "dependency_cycle_count",
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
        AssertMetric(metrics, typeA.TargetId, "dependency_cycle_count", 1);
        AssertMetric(metrics, typeA.TargetId, "dependency_component_size", 2);
        AssertMetric(metrics, typeA.TargetId, "transitive_type_dependency_count", 2);
        AssertMetric(metrics, typeC.TargetId, "transitive_type_dependent_count", 2);
        AssertMetric(metrics, memberA.TargetId, "outgoing_call_count", 1);
        AssertMetric(metrics, memberA.TargetId, "incoming_call_count", 1);
        AssertMetric(metrics, memberA.TargetId, "recursive_component_size", 2);
        AssertMetric(metrics, memberC.TargetId, "recursive_component_size", 1);
        AssertMetric(metrics, typeA.TargetId, "outgoing_call_count", 1);
        AssertMetric(metrics, typeC.TargetId, "incoming_call_count", 1);
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
