using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Tests.Support;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class LineFactsCollectorTests
{
    [Fact]
    public void CollectClassifiesEveryLineCategoryWithoutDoubleCountingComponents()
    {
        var source = string.Join(
            '\n',
            "// ordinary",
            "",
            "/// <summary>",
            "/// Docs.",
            "/// </summary>",
            "public class Sample // mixed",
            "{",
            "    public void M() { } /* mixed */",
            "}");
        var context = RoslynTestCompilation.Create(source);

        LineFacts facts = LineFactsCollector.Collect(
            context.SyntaxTree,
            context.Root,
            SourceText.From(source));

        Assert.Equal(
            new LineFacts(
                LinesOfCode: 9,
                NonCommentLinesOfCode: 4,
                BlankLineCount: 1,
                CommentOnlyLineCount: 4,
                CommentedLineCount: 6,
                MixedCodeCommentLineCount: 2,
                DocumentationCommentLineCount: 3),
            facts);
    }

    [Fact]
    public void CollectTreatsBlankTextInsideMultilineCommentAsCommentOnly()
    {
        var source = string.Join(
            '\n',
            "class Sample",
            "{",
            "    /*",
            "",
            "    */",
            "    string Text = \"/* not a comment */\"; // mixed",
            "}");
        var context = RoslynTestCompilation.Create(source);

        LineFacts facts = LineFactsCollector.Collect(
            context.SyntaxTree,
            context.Root,
            SourceText.From(source));

        Assert.Equal(7, facts.LinesOfCode);
        Assert.Equal(4, facts.NonCommentLinesOfCode);
        Assert.Equal(0, facts.BlankLineCount);
        Assert.Equal(3, facts.CommentOnlyLineCount);
        Assert.Equal(4, facts.CommentedLineCount);
        Assert.Equal(1, facts.MixedCodeCommentLineCount);
        Assert.Equal(0, facts.DocumentationCommentLineCount);
    }

    [Fact]
    public void CollectReportsOneBlankPhysicalLineForEmptyFile()
    {
        const string source = "";
        var context = RoslynTestCompilation.Create(source);

        LineFacts facts = LineFactsCollector.Collect(
            context.SyntaxTree,
            context.Root,
            SourceText.From(source));

        Assert.Equal(new LineFacts(1, 0, 1, 0, 0, 0, 0), facts);
    }
}
