using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Metrics;

internal sealed record LineFacts(
    int LinesOfCode,
    int NonCommentLinesOfCode,
    int BlankLineCount,
    int CommentOnlyLineCount,
    int CommentedLineCount,
    int MixedCodeCommentLineCount,
    int DocumentationCommentLineCount);

internal static class LineFactsCollector
{
    public static LineFacts Collect(
        SyntaxTree syntaxTree,
        SyntaxNode root,
        SourceText sourceText)
    {
        ArgumentNullException.ThrowIfNull(syntaxTree);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sourceText);

        var fullSpan = new TextSpan(0, sourceText.Length);
        HashSet<int> tokenLines = CollectTokenLines(root, fullSpan, syntaxTree);
        var commentLines = new HashSet<int>();
        var documentationCommentLines = new HashSet<int>();

        foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            var isDocumentation = trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
            var isComment = isDocumentation ||
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

            if (!isComment || trivia.Span.IsEmpty)
            {
                continue;
            }

            FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(trivia.Span);
            var endLine = lineSpan.EndLinePosition.Line;
            if (lineSpan.EndLinePosition.Character == 0 &&
                endLine > lineSpan.StartLinePosition.Line)
            {
                endLine--;
            }

            for (var line = lineSpan.StartLinePosition.Line;
                 line <= endLine;
                 line++)
            {
                commentLines.Add(line);
                if (isDocumentation)
                {
                    documentationCommentLines.Add(line);
                }
            }
        }

        var blankLineCount = sourceText.Lines.Count(line =>
            IsBlankLine(sourceText, line) &&
            !tokenLines.Contains(line.LineNumber) &&
            !commentLines.Contains(line.LineNumber));

        return new LineFacts(
            CountLines(fullSpan, syntaxTree),
            tokenLines.Count,
            blankLineCount,
            commentLines.Count(line => !tokenLines.Contains(line)),
            commentLines.Count,
            commentLines.Count(tokenLines.Contains),
            documentationCommentLines.Count);
    }

    internal static HashSet<int> CollectTokenLines(
        SyntaxNode root,
        TextSpan span,
        SyntaxTree syntaxTree)
    {
        var lines = new HashSet<int>();

        foreach (SyntaxToken token in root.DescendantTokens(span).Where(token => token.Span.Length > 0))
        {
            FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(token.Span);
            var endLine = lineSpan.EndLinePosition.Line;
            if (endLine > lineSpan.StartLinePosition.Line && lineSpan.EndLinePosition.Character == 0)
            {
                endLine--;
            }

            for (var line = lineSpan.StartLinePosition.Line; line <= endLine; line++)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static bool IsBlankLine(SourceText sourceText, TextLine line)
    {
        TextSpan span = line.Span;
        for (var index = span.Start; index < span.End; index++)
        {
            if (!char.IsWhiteSpace(sourceText[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountLines(TextSpan span, SyntaxTree syntaxTree)
    {
        FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(span);

        return Math.Max(1, lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1);
    }
}
