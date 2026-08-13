using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class SemanticMetricProjectorTests
{
    [Fact]
    public void ProjectPublishesEntireSemanticFamilyWithExactTypeProjectAndCfgValues()
    {
        const string projectPath = "Sample.csproj";
        var projectKey = ProjectIdentity.Key(projectPath);
        var semantic = new TypeSemanticFacts
        {
            InheritanceDepth = 2,
            ClassCoupling = 4,
            PublicApiCount = 3,
            DocumentedPublicApiCount = 2
        };
        TypeFacts type = TestFacts.Type(
            "type:a",
            "file:a",
            "A.cs",
            projectKey,
            semantic: semantic);
        var operations = new OperationFacts
        {
            OperationCount = 15,
            AllocationCount = 2,
            AwaitCount = 1,
            ControlFlowGraphCount = 1,
            BasicBlockCount = 6,
            ReachableBasicBlockCount = 5,
            UnreachableBasicBlockCount = 1,
            ControlFlowEdgeCount = 6,
            CfgCyclomaticComplexity = 2
        };
        MemberFacts member = TestFacts.Member(
            "member:a",
            type.TargetId,
            type.FilePath,
            projectKey,
            operations: operations);
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            types: [type],
            members: [member],
            projects: [projectPath]);

        IReadOnlyList<MetricResultLine> metrics = SemanticMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Equal(
            MetricFamilies.Semantic.Order(StringComparer.Ordinal),
            metrics.Select(metric => metric.MetricId).Distinct().Order(StringComparer.Ordinal));
        AssertMetric(metrics, type.TargetId, "inheritance_depth", 2, "integer");
        AssertMetric(metrics, type.TargetId, "class_coupling", 4, "integer");
        AssertMetric(metrics, type.TargetId, "public_api_count", 3, "integer");
        AssertMetric(metrics, type.TargetId, "documented_public_api_count", 2, "integer");
        AssertMetric(metrics, type.TargetId, "public_api_documentation_ratio", 2d / 3d, "number");
        AssertMetric(metrics, ProjectIdentity.TargetId(projectPath), "public_api_count", 3, "integer");
        AssertMetric(metrics, member.TargetId, "operation_count", 15, "integer");
        AssertMetric(metrics, member.TargetId, "allocation_count", 2, "integer");
        AssertMetric(metrics, member.TargetId, "await_count", 1, "integer");
        AssertMetric(metrics, member.TargetId, "basic_block_count", 6, "integer");
        AssertMetric(metrics, member.TargetId, "reachable_basic_block_count", 5, "integer");
        AssertMetric(metrics, member.TargetId, "unreachable_basic_block_count", 1, "integer");
        AssertMetric(metrics, member.TargetId, "control_flow_edge_count", 6, "integer");
        AssertMetric(metrics, member.TargetId, "cfg_cyclomatic_complexity", 2, "integer");
    }

    [Fact]
    public void ProjectReturnsNoSemanticMetricsForDegradedAnalysis()
    {
        TypeFacts type = TestFacts.Type(
            "type:a",
            "file:a",
            "A.cs",
            semantic: new TypeSemanticFacts
            {
                InheritanceDepth = 1,
                ClassCoupling = 1,
                PublicApiCount = 1,
                DocumentedPublicApiCount = 1
            });
        SyntaxAnalysisFacts facts = TestFacts.Analysis(types: [type], trusted: false);

        IReadOnlyList<MetricResultLine> metrics = SemanticMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Empty(metrics);
    }

    private static void AssertMetric(
        IEnumerable<MetricResultLine> metrics,
        string targetId,
        string metricId,
        double expected,
        string valueKind)
    {
        MetricResultLine metric = Assert.Single(
            metrics,
            candidate => candidate.TargetId == targetId && candidate.MetricId == metricId);
        Assert.Equal(expected, metric.NumericValue, 12);
        Assert.Equal(valueKind, metric.ValueKind);
    }
}
