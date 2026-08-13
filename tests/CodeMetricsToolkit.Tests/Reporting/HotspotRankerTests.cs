using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Reporting;

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
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
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
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            types: [type],
            members: [member],
            diagnostics: [diagnostic]);

        HotspotRanking ranking = HotspotRanker.Rank(facts, top: 3, CancellationToken.None);

        Assert.Contains(
            ranking.Hotspots.SelectMany(hotspot => hotspot.Components),
            component => component.MetricId == "diagnostic_count" && component.Value == 1);
    }
}
