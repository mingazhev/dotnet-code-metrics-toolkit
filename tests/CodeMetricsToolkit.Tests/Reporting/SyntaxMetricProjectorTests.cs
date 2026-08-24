using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class SyntaxMetricProjectorTests
{
    [Fact]
    public void ProjectPublishesEntireSyntaxFamilyAndAggregatesUniqueFiles()
    {
        const string projectPath = "Sample.csproj";
        var projectKey = ProjectIdentity.Key(projectPath);
        FileFacts first = TestFacts.File(
            "file:a",
            "A.cs",
            projectKey,
            lines: 10,
            tokenLines: 5,
            blanks: 2,
            commentOnly: 2,
            commented: 3,
            mixed: 1,
            documentation: 1);
        FileFacts second = TestFacts.File(
            "file:b",
            "B.cs",
            projectKey,
            lines: 6,
            tokenLines: 4,
            blanks: 1,
            commentOnly: 1,
            commented: 2,
            mixed: 1,
            documentation: 0);
        TypeFacts type = TestFacts.Type("type:a", first.TargetId, first.FilePath, projectKey, memberCount: 2);
        MemberFacts simple = TestFacts.Member("member:simple", type.TargetId, first.FilePath, projectKey);
        MemberFacts complex = TestFacts.Member(
            "member:complex",
            type.TargetId,
            first.FilePath,
            projectKey,
            startLine: 6,
            endLine: 10,
            cyclomatic: 7,
            decisions: 6,
            cognitive: 9,
            nesting: 3);
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            files: [first, first with { TargetId = "file:duplicate" }, second],
            types: [type],
            members: [simple, complex],
            projects: [projectPath]);

        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Equal(
            MetricFamilies.Syntax.Order(StringComparer.Ordinal),
            metrics.Select(metric => metric.MetricId).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(16, Metric(metrics, "solution", "lines_of_code").NumericValue);
        Assert.Equal(16, Metric(metrics, "project", "lines_of_code").NumericValue);
        Assert.Equal(9, Metric(metrics, "type", "max_member_cognitive_complexity").NumericValue);
        Assert.Equal(7, Metric(metrics, "type", "p95_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(3, Metric(metrics, "type", "max_member_nesting_depth").NumericValue);
        Assert.Equal(5d / 10d, Metric(metrics, first.TargetId, "token_line_ratio").NumericValue, 12);
        Assert.Equal("number", Metric(metrics, first.TargetId, "token_line_ratio").ValueKind);
    }

    [Fact]
    public void ProjectFallsBackToFileProjectKeysForExternallyConstructedFacts()
    {
        const string projectPath = "External.csproj";
        var projectKey = ProjectIdentity.Key(projectPath);
        FileFacts file = TestFacts.File("file:external", "External.cs", projectKey, lines: 7);
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            projects: [projectPath]) with
        {
            ProjectFileMemberships = null
        };

        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(
            facts,
            CancellationToken.None);
        GraphArtifact graph = GraphProjector.Project(facts);

        Assert.Equal(7, Metric(metrics, "project", "lines_of_code").NumericValue);
        Assert.Contains(graph.Edges, edge =>
            edge.From == ProjectIdentity.TargetId(projectPath) &&
            edge.To == file.TargetId &&
            edge.Kind == "contains");
    }

    private static MetricResultLine Metric(
        IEnumerable<MetricResultLine> metrics,
        string targetKindOrId,
        string metricId)
    {
        return Assert.Single(metrics, metric =>
            metric.MetricId == metricId &&
            (metric.TargetKind == targetKindOrId || metric.TargetId == targetKindOrId));
    }
}
