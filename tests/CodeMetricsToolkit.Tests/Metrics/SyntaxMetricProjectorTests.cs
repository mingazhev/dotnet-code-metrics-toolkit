using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Metrics;

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
        AnalysisFacts facts = TestFacts.Analysis(
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
        Assert.Equal(1, Metric(metrics, "member:simple", "parameter_count").NumericValue);
    }

    [Fact]
    public void ProjectOmitsRatiosForZeroLocAndReportsZeroMemberAggregates()
    {
        FileFacts empty = TestFacts.File(
            "file:empty",
            "Empty.cs",
            lines: 0,
            tokenLines: 0,
            blanks: 0,
            commentOnly: 0,
            commented: 0,
            mixed: 0,
            documentation: 0);
        TypeFacts type = TestFacts.Type("type:empty", empty.TargetId, empty.FilePath, memberCount: 0);
        AnalysisFacts facts = TestFacts.Analysis(files: [empty], types: [type], members: []);

        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.DoesNotContain(metrics, metric => metric.MetricId.EndsWith("_ratio", StringComparison.Ordinal));
        Assert.Equal(0, Metric(metrics, empty.TargetId, "max_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(0, Metric(metrics, empty.TargetId, "p95_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(0, Metric(metrics, type.TargetId, "max_member_cognitive_complexity").NumericValue);
        Assert.Equal(0, Metric(metrics, type.TargetId, "p95_member_cognitive_complexity").NumericValue);
        Assert.Equal(0, Metric(metrics, type.TargetId, "max_member_nesting_depth").NumericValue);
        Assert.Equal(0, Metric(metrics, type.TargetId, "max_member_length").NumericValue);
        Assert.Equal(0, Metric(metrics, type.TargetId, "p95_member_length").NumericValue);
        Assert.Equal(0, Metric(metrics, type.TargetId, "weighted_member_complexity").NumericValue);
    }

    [Fact]
    public void ProjectPublishesExtendedAggregatesForProjectAndSolutionPopulations()
    {
        const string firstProjectPath = "First.csproj";
        const string secondProjectPath = "Second.csproj";
        var firstProjectKey = ProjectIdentity.Key(firstProjectPath);
        var secondProjectKey = ProjectIdentity.Key(secondProjectPath);
        FileFacts sharedFirst = TestFacts.File("file:shared-first", "Shared.cs", firstProjectKey);
        FileFacts sharedSecond = TestFacts.File("file:shared-second", "Shared.cs", secondProjectKey);
        FileFacts other = TestFacts.File("file:other", "Other.cs", firstProjectKey);
        TypeFacts[] types = Enumerable.Range(1, 21)
            .Select(index =>
            {
                TypeFacts type = TestFacts.Type($"type:{index:D2}", sharedFirst.TargetId, sharedFirst.FilePath, firstProjectKey);
                return type with
                {
                    LinesOfCode = index,
                    Declarations = index == 21
                        ? [TestFacts.Span(sharedFirst.FilePath, 1, 10), TestFacts.Span(other.FilePath, 1, 11)]
                        : type.Declarations
                };
            })
            .ToArray();
        MemberFacts[] firstProjectMembers = Enumerable.Range(1, 20)
            .Select(index =>
            {
                MemberFacts member = TestFacts.Member(
                    $"member:{index:D2}",
                    "type:01",
                    sharedFirst.FilePath,
                    firstProjectKey,
                    startLine: 1,
                    endLine: index,
                    cyclomatic: index,
                    decisions: index - 1,
                    cognitive: index * 2,
                    nesting: index);
                return member with
                {
                    Declarations = index == 20
                        ? [TestFacts.Span(sharedFirst.FilePath, 1, 10), TestFacts.Span(other.FilePath, 1, 10)]
                        : member.Declarations
                };
            })
            .ToArray();
        MemberFacts sharedMemberInSecondProject = firstProjectMembers[^1] with { ProjectKey = secondProjectKey };
        TypeFacts sharedTypeInSecondProject = types[^1] with
        {
            ParentFileTargetId = sharedSecond.TargetId,
            ProjectKey = secondProjectKey
        };
        AnalysisFacts facts = TestFacts.Analysis(
            files: [sharedFirst, sharedSecond, other],
            types: [.. types, sharedTypeInSecondProject],
            members: [.. firstProjectMembers, sharedMemberInSecondProject],
            projects: [firstProjectPath, secondProjectPath]);

        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(facts, CancellationToken.None);
        var firstProjectTarget = ProjectIdentity.TargetId(firstProjectPath);
        var secondProjectTarget = ProjectIdentity.TargetId(secondProjectPath);

        Assert.Equal(210, Metric(metrics, "type:01", "weighted_member_complexity").NumericValue);
        Assert.Equal(20, Metric(metrics, "type:01", "max_member_length").NumericValue);
        Assert.Equal(19, Metric(metrics, "type:01", "p95_member_length").NumericValue);
        Assert.Equal(20, Metric(metrics, sharedFirst.TargetId, "max_member_length").NumericValue);
        Assert.Equal(19, Metric(metrics, sharedFirst.TargetId, "p95_member_length").NumericValue);
        Assert.Equal(20, Metric(metrics, firstProjectTarget, "max_member_length").NumericValue);
        Assert.Equal(19, Metric(metrics, firstProjectTarget, "p95_member_length").NumericValue);
        Assert.Equal(20, Metric(metrics, firstProjectTarget, "max_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(19, Metric(metrics, firstProjectTarget, "p95_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(40, Metric(metrics, firstProjectTarget, "max_member_cognitive_complexity").NumericValue);
        Assert.Equal(38, Metric(metrics, firstProjectTarget, "p95_member_cognitive_complexity").NumericValue);
        Assert.Equal(20, Metric(metrics, firstProjectTarget, "max_member_nesting_depth").NumericValue);
        Assert.Equal(20, Metric(metrics, secondProjectTarget, "p95_member_length").NumericValue);
        Assert.Equal(20, Metric(metrics, secondProjectTarget, "p95_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(40, Metric(metrics, secondProjectTarget, "max_member_cognitive_complexity").NumericValue);
        Assert.Equal(40, Metric(metrics, secondProjectTarget, "p95_member_cognitive_complexity").NumericValue);
        Assert.Equal(20, Metric(metrics, secondProjectTarget, "max_member_nesting_depth").NumericValue);
        Assert.Equal(21, Metric(metrics, firstProjectTarget, "max_type_lines_of_code").NumericValue);
        Assert.Equal(20, Metric(metrics, firstProjectTarget, "p95_type_lines_of_code").NumericValue);
        Assert.Equal(21, Metric(metrics, secondProjectTarget, "max_type_lines_of_code").NumericValue);
        Assert.Equal(21, Metric(metrics, secondProjectTarget, "p95_type_lines_of_code").NumericValue);

        Assert.Equal(20, Metric(metrics, "solution", "max_member_length").NumericValue);
        Assert.Equal(19, Metric(metrics, "solution", "p95_member_length").NumericValue);
        Assert.Equal(20, Metric(metrics, "solution", "max_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(19, Metric(metrics, "solution", "p95_member_cyclomatic_complexity").NumericValue);
        Assert.Equal(40, Metric(metrics, "solution", "max_member_cognitive_complexity").NumericValue);
        Assert.Equal(38, Metric(metrics, "solution", "p95_member_cognitive_complexity").NumericValue);
        Assert.Equal(20, Metric(metrics, "solution", "max_member_nesting_depth").NumericValue);
        Assert.Equal(21, Metric(metrics, "solution", "max_type_lines_of_code").NumericValue);
        Assert.Equal(20, Metric(metrics, "solution", "p95_type_lines_of_code").NumericValue);
        Assert.Equal(0, Metric(metrics, other.TargetId, "max_member_length").NumericValue);
        Assert.Equal(0, Metric(metrics, other.TargetId, "p95_member_length").NumericValue);
    }

    [Fact]
    public void ProjectReportsZeroForNewAggregatesWhenPopulationsAreEmpty()
    {
        const string projectPath = "Empty.csproj";
        AnalysisFacts facts = TestFacts.Analysis(projects: [projectPath]);

        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(facts, CancellationToken.None);

        foreach (var target in new[] { "solution", ProjectIdentity.TargetId(projectPath) })
        {
            Assert.Equal(0, Metric(metrics, target, "max_member_length").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "p95_member_length").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "max_member_cyclomatic_complexity").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "p95_member_cyclomatic_complexity").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "max_member_cognitive_complexity").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "p95_member_cognitive_complexity").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "max_member_nesting_depth").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "max_type_lines_of_code").NumericValue);
            Assert.Equal(0, Metric(metrics, target, "p95_type_lines_of_code").NumericValue);
        }
    }

    [Fact]
    public void ProjectCountsPartialTypeDeclarationsInEachFile()
    {
        FileFacts first = TestFacts.File("file:a", "A.cs");
        FileFacts second = TestFacts.File("file:b", "B.cs");
        TypeFacts partialType = TestFacts.Type("type:partial", first.TargetId, first.FilePath) with
        {
            Declarations =
            [
                TestFacts.Span(first.FilePath, 1, 3),
                TestFacts.Span(first.FilePath, 5, 7),
                TestFacts.Span(second.FilePath, 1, 3)
            ]
        };
        AnalysisFacts facts = TestFacts.Analysis(files: [first, second], types: [partialType]);

        IReadOnlyList<MetricResultLine> metrics = SyntaxMetricProjector.Project(
            facts,
            CancellationToken.None);
        GraphArtifact graph = GraphProjector.Project(facts);

        Assert.Equal(2, Metric(metrics, first.TargetId, "type_count").NumericValue);
        Assert.Equal(1, Metric(metrics, second.TargetId, "type_count").NumericValue);
        Assert.Single(graph.Nodes, node => node.Id == partialType.TargetId && node.Kind == "type");
    }

    [Fact]
    public void ProjectFallsBackToFileProjectKeysForExternallyConstructedFacts()
    {
        const string projectPath = "External.csproj";
        var projectKey = ProjectIdentity.Key(projectPath);
        FileFacts file = TestFacts.File("file:external", "External.cs", projectKey, lines: 7);
        AnalysisFacts facts = TestFacts.Analysis(
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
