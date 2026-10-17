using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class HotspotRankerTests
{
    [Fact]
    public void RankPublishesOneStableRankMetricPerCandidateAndOrdersHighestMemberFirst()
    {
        FileFacts file = TestFacts.File("file:a", "A.cs", lines: 40, tokenLines: 30);
        TypeFacts type = TestFacts.Type("type:a", file.TargetId, file.FilePath, endLine: 40, memberCount: 2);
        MemberFacts simple = TestFacts.Member("member:a", type.TargetId, type.FilePath);
        MemberFacts complex = TestFacts.Member(
            "member:b",
            type.TargetId,
            type.FilePath,
            startLine: 10,
            endLine: 35,
            parameterCount: 4,
            cyclomatic: 8,
            decisions: 7,
            cognitive: 12,
            nesting: 4);
        AnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            types: [type],
            members: [simple, complex],
            diagnosticsInHotspots: false);

        HotspotRanking ranking = HotspotRanker.Rank(facts, top: 4, CancellationToken.None);

        Assert.Equal(MetricFamilies.Hotspots, ranking.Metrics.Select(metric => metric.MetricId).ToHashSet());
        Assert.Equal(4, ranking.Metrics.Count);
        Assert.Equal(complex.TargetId, ranking.Hotspots[0].TargetId);
        Assert.Equal(1, ranking.Hotspots[0].Rank);
        Assert.DoesNotContain(
            ranking.Hotspots.SelectMany(hotspot => hotspot.Components),
            component => component.MetricId == "diagnostic_count");
    }

    [Fact]
    public void RankIncludesDiagnosticComponentOnlyWhenHealthAllowsIt()
    {
        FileFacts file = TestFacts.File("file:a", "A.cs");
        TypeFacts type = TestFacts.Type("type:a", file.TargetId, file.FilePath);
        MemberFacts member = TestFacts.Member("member:a", type.TargetId, type.FilePath);
        var diagnostic = new AnalysisDiagnostic
        {
            Id = "CS0001",
            Severity = "warning",
            Message = "sample",
            FilePath = file.FilePath,
            StartLine = member.StartLine,
            EndLine = member.EndLine,
            Tags = ["compiler"]
        };
        AnalysisDiagnostic taggedDiagnostic = diagnostic with
        {
            Id = "CS0002",
            Tags = ["nullable"]
        };
        AnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            types: [type],
            members: [member],
            diagnostics: [diagnostic, taggedDiagnostic]);

        HotspotRanking ranking = HotspotRanker.Rank(facts, top: 3, CancellationToken.None);
        MetricResultLine aggregate = Assert.Single(
            DiagnosticMetricProjector.Project(facts, CancellationToken.None),
            metric => metric.TargetId == member.TargetId && metric.Tags is null);
        HotspotLine memberHotspot = Assert.Single(ranking.Hotspots, hotspot => hotspot.TargetId == member.TargetId);
        HotspotComponentLine diagnosticComponent = Assert.Single(
            memberHotspot.Components,
            component => component.MetricId == "diagnostic_count");

        Assert.Equal(aggregate.NumericValue, diagnosticComponent.Value);
    }

    [Theory]
    [InlineData("syntax_only")]
    [InlineData("degraded")]
    public void RankUsesAvailableProjectedMetricRowsAndNormalizesRemainingWeights(string analysisQuality)
    {
        FileFacts firstFile = TestFacts.File("file:a", "A.cs");
        FileFacts secondFile = TestFacts.File("file:b", "B.cs");
        TypeFacts firstType = TestFacts.Type("type:a", firstFile.TargetId, firstFile.FilePath, memberCount: 11) with
        {
            Declarations =
            [
                TestFacts.Span(firstFile.FilePath, 1, 4),
                TestFacts.Span(firstFile.FilePath, 6, 9)
            ]
        };
        TypeFacts secondType = TestFacts.Type("type:b", secondFile.TargetId, secondFile.FilePath);
        AnalysisFacts facts = TestFacts.Analysis(
            files: [firstFile, secondFile],
            types: [firstType, secondType],
            edges: [TestFacts.Edge(firstType.TargetId, secondType.TargetId, "uses_type")],
            trusted: false,
            diagnosticsInHotspots: false);
        facts = facts with { Health = facts.Health with { AnalysisQuality = analysisQuality } };
        MetricResultLine[] projectedMetrics = SyntaxMetricProjector.Project(facts, CancellationToken.None)
            .Concat(GraphMetricProjector.Project(facts, CancellationToken.None))
            .Concat(SemanticMetricProjector.Project(facts, CancellationToken.None))
            .Concat(DiagnosticMetricProjector.Project(facts, CancellationToken.None))
            .ToArray();

        HotspotRanking ranking = HotspotRanker.Rank(facts, top: 10, CancellationToken.None);

        HotspotLine hotspot = Assert.Single(ranking.Hotspots, item => item.TargetId == firstType.TargetId);
        Assert.DoesNotContain(hotspot.Components, component => component.MetricId == "outgoing_type_dependency_count");
        Assert.Equal(1, hotspot.Components.Sum(component => component.Weight), 12);
        foreach (HotspotComponentLine component in hotspot.Components)
        {
            MetricResultLine metric = Assert.Single(projectedMetrics, row =>
                row.TargetId == firstType.TargetId &&
                row.MetricId == component.MetricId &&
                row.Tags is null);
            Assert.Equal(metric.NumericValue, component.Value);
        }

        Assert.All(ranking.Metrics, metric => Assert.Equal("2.0.0", metric.MetricVersion));
    }
}
