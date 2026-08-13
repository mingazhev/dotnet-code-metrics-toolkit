using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class DiagnosticMetricProjectorTests
{
    [Fact]
    public void ProjectCountsOverlappingDiagnosticsAndTagsAtEverySourceTargetLevel()
    {
        FileFacts file = TestFacts.File("file:a", "A.cs", lines: 20);
        TypeFacts type = TestFacts.Type("type:a", file.TargetId, file.FilePath, startLine: 2, endLine: 18);
        MemberFacts member = TestFacts.Member("member:a", type.TargetId, type.FilePath, startLine: 5, endLine: 10);
        var diagnostic = new AnalysisDiagnostic
        {
            Id = "CS0001",
            Severity = "warning",
            Message = "sample",
            FilePath = file.FilePath,
            StartLine = 7,
            EndLine = 7,
            Tags = ["compiler", "nullable"]
        };
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            types: [type],
            members: [member],
            diagnostics: [diagnostic]);

        IReadOnlyList<MetricResultLine> metrics = DiagnosticMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Equal(MetricFamilies.Diagnostics, metrics.Select(metric => metric.MetricId).ToHashSet());
        Assert.Equal(9, metrics.Count);
        foreach (var targetId in new[] { file.TargetId, type.TargetId, member.TargetId })
        {
            Assert.Equal(1, Assert.Single(metrics, metric => metric.TargetId == targetId && metric.Tags is null).NumericValue);
            Assert.Equal(1, Assert.Single(metrics, metric => metric.TargetId == targetId && metric.Tags is ["compiler"]).NumericValue);
            Assert.Equal(1, Assert.Single(metrics, metric => metric.TargetId == targetId && metric.Tags is ["nullable"]).NumericValue);
        }
    }

    [Fact]
    public void ProjectReturnsNoMetricsWhenDiagnosticsAreUntrusted()
    {
        FileFacts file = TestFacts.File("file:a", "A.cs");
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            trustedDiagnostics: false);

        IReadOnlyList<MetricResultLine> metrics = DiagnosticMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Empty(metrics);
    }
}
