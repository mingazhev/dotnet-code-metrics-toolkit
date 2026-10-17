using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class DuplicationMetricProjectorTests
{
    [Fact]
    public void ProjectAggregatesSharedPhysicalFilesByProjectAndSolutionAndTagsFileRegions()
    {
        const string firstProjectPath = "First.csproj";
        const string secondProjectPath = "Second.csproj";
        var firstProjectKey = ProjectIdentity.Key(firstProjectPath);
        var secondProjectKey = ProjectIdentity.Key(secondProjectPath);
        FileFacts first = TestFacts.File("file:first", "A.cs", firstProjectKey, lines: 20);
        FileFacts second = TestFacts.File("file:second", "B.cs", secondProjectKey, lines: 10);
        AnalysisFacts facts = TestFacts.Analysis(
                files: [first, second],
                projects: [firstProjectPath, secondProjectPath]) with
        {
            ProjectFileMemberships = [
                new ProjectFileMembershipFacts(firstProjectKey, first.FilePath),
                new ProjectFileMembershipFacts(secondProjectKey, first.FilePath),
                new ProjectFileMembershipFacts(secondProjectKey, second.FilePath)
            ],
            Duplication = new DuplicationFacts([
                new DuplicationFileFacts(first.FilePath, [new DuplicationRegionFacts(2, 6, "duplication_block:000001")], 5),
                new DuplicationFileFacts(second.FilePath, [new DuplicationRegionFacts(4, 6, "duplication_block:000002")], 3)
            ])
        };

        IReadOnlyList<MetricResultLine> metrics = DuplicationMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Equal(2, Metric(metrics, "solution:root", "duplicated_block_count").NumericValue);
        Assert.Equal(8, Metric(metrics, "solution:root", "duplicated_line_count").NumericValue);
        Assert.Equal(8d / 30d, Metric(metrics, "solution:root", "duplicated_line_ratio").NumericValue, 12);
        Assert.Equal(1, Metric(metrics, "project:" + firstProjectKey, "duplicated_block_count").NumericValue);
        Assert.Equal(5, Metric(metrics, "project:" + firstProjectKey, "duplicated_line_count").NumericValue);
        Assert.Equal(2, Metric(metrics, "project:" + secondProjectKey, "duplicated_block_count").NumericValue);
        Assert.Equal(8d / 30d, Metric(metrics, "project:" + secondProjectKey, "duplicated_line_ratio").NumericValue, 12);

        MetricResultLine fileAggregate = Assert.Single(metrics, metric =>
            metric.TargetId == first.TargetId && metric.MetricId == "duplicated_block_count" && metric.Tags is null);
        Assert.Equal(1, fileAggregate.NumericValue);
        MetricResultLine region = Assert.Single(metrics, metric =>
            metric.TargetId == first.TargetId && metric.MetricId == "duplicated_block_count" && metric.Tags is not null);
        Assert.Equal(1, region.NumericValue);
        Assert.Equal((2, 6), (region.StartLine, region.EndLine));
        Assert.Equal("duplication_block:000001", Assert.Single(region.Tags!));
        Assert.Equal(first.TargetIdStability, region.TargetIdStability);
        Assert.Equal(first.FilePath, region.FilePath);
    }

    [Fact]
    public void ProjectOmitsRatioWhenPhysicalLineDenominatorIsZero()
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
        AnalysisFacts facts = TestFacts.Analysis(files: [empty]) with
        {
            Duplication = new DuplicationFacts([
                new DuplicationFileFacts(empty.FilePath, [], 0)
            ])
        };

        IReadOnlyList<MetricResultLine> metrics = DuplicationMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.DoesNotContain(metrics, metric => metric.MetricId == "duplicated_line_ratio");
        Assert.Equal(0, Metric(metrics, empty.TargetId, "duplicated_line_count").NumericValue);
    }

    [Fact]
    public void ProjectOmitsMetricsWhenDuplicationFactsWereNotCollected()
    {
        AnalysisFacts facts = TestFacts.Analysis(files: [TestFacts.File("file:a", "A.cs")]);

        IReadOnlyList<MetricResultLine> metrics = DuplicationMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Empty(metrics);
    }

    private static MetricResultLine Metric(
        IReadOnlyList<MetricResultLine> metrics,
        string targetId,
        string metricId)
    {
        return Assert.Single(metrics, metric =>
            metric.TargetId == targetId && metric.MetricId == metricId && metric.Tags is null);
    }
}
