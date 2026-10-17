using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class AnalysisDiagnosticCollectorTests
{
    [Fact]
    public void AddMergesDuplicateTagsWithoutChangingDiagnosticOrderOrIdentity()
    {
        var collector = new AnalysisDiagnosticCollector();
        AnalysisDiagnostic first = CreateDiagnostic(
            "CS1002",
            "first",
            line: 1,
            severity: "error",
            tags: ["syntax"]);
        AnalysisDiagnostic second = CreateDiagnostic(
            "CS0103",
            "second",
            line: 2,
            severity: "error",
            tags: ["compiler"]);

        collector.Add(first);
        collector.Add(second);
        collector.Add(CreateDiagnostic(
            "CS1002",
            "first",
            line: 1,
            severity: "warning",
            tags: ["compiler"]));

        Assert.Equal(["first", "second"], collector.Diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.Equal("error", collector.Diagnostics[0].Severity);
        Assert.Equal(["compiler", "syntax"], collector.Diagnostics[0].Tags);
    }

    private static AnalysisDiagnostic CreateDiagnostic(
        string id,
        string message,
        int line,
        string severity,
        string[] tags)
    {
        return new AnalysisDiagnostic
        {
            Id = id,
            Severity = severity,
            Message = message,
            ProjectPath = "Sample.csproj",
            FilePath = "Sample.cs",
            StartLine = line,
            EndLine = line,
            Tags = tags
        };
    }
}
