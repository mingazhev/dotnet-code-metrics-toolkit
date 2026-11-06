using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class DiagnosticMetricProjectorTests
{
    [Fact]
    public void ProjectCountsDiagnosticsAcrossPartialDeclarationsOnce()
    {
        TypeFacts type = TestFacts.Type("type:a", "file:a", "A.cs", startLine: 2, endLine: 10) with
        {
            Declarations = [TestFacts.Span("A.cs", 2, 10), TestFacts.Span("B.cs", 2, 10), TestFacts.Span("B.cs", 8, 12)]
        };
        MemberFacts member = TestFacts.Member("member:a", type.TargetId, "A.cs", startLine: 5, endLine: 5) with
        {
            Declarations = [TestFacts.Span("A.cs", 5, 5), TestFacts.Span("B.cs", 5, 10)]
        };
        AnalysisDiagnostic[] diagnostics = [
            Diagnostic("A.cs", 5, 5), Diagnostic("B.cs", 9, 9),
            Diagnostic("B.cs", 20, 20), Diagnostic("Unrelated.cs", 5, 5)
        ];

        IReadOnlyList<MetricResultLine> metrics = DiagnosticMetricProjector.Project(
            TestFacts.Analysis(types: [type], members: [member], diagnostics: diagnostics),
            CancellationToken.None);

        Assert.Equal(4, metrics.Count);
        Assert.All(metrics, metric => Assert.Equal(2, metric.NumericValue));
        Assert.All(metrics, metric => Assert.Equal("A.cs", metric.FilePath));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(5, null)]
    [InlineData(null, 5)]
    public void ProjectDoesNotAttributeDiagnosticsWithoutCompleteSourceSpans(int? startLine, int? endLine)
    {
        FileFacts file = TestFacts.File("file:a", "A.cs");
        IReadOnlyList<MetricResultLine> metrics = DiagnosticMetricProjector.Project(
            TestFacts.Analysis(files: [file], diagnostics: [Diagnostic("A.cs", startLine, endLine)]),
            CancellationToken.None);

        MetricResultLine metric = Assert.Single(metrics);
        Assert.Null(metric.Tags);
        Assert.Equal(0, metric.NumericValue);
    }

    private static AnalysisDiagnostic Diagnostic(string path, int? startLine, int? endLine) => new()
    {
        Id = "CS0001",
        Severity = "warning",
        Message = "sample",
        FilePath = path,
        StartLine = startLine,
        EndLine = endLine,
        Tags = ["compiler"]
    };

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
        AnalysisFacts facts = TestFacts.Analysis(
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
        AnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            trustedDiagnostics: false);

        IReadOnlyList<MetricResultLine> metrics = DiagnosticMetricProjector.Project(
            facts,
            CancellationToken.None);

        Assert.Empty(metrics);
    }
}
