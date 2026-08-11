using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class ControlFlowFactsCollectorTests
{
    [Theory]
    [InlineData("if (value > 0) { }", 2)]
    [InlineData("if (value > 0) { } else if (value < 0) { }", 3)]
    [InlineData("for (int i = 0; i < 1; i++) { }", 2)]
    [InlineData("foreach (int item in new[] { value }) { }", 2)]
    [InlineData("while (value > 0) { value--; }", 2)]
    [InlineData("do { value--; } while (value > 0);", 2)]
    [InlineData("switch (value) { case 1: break; case 2: break; default: break; }", 3)]
    [InlineData("_ = value switch { 1 => 1, 2 => 2, _ => 0 };", 4)]
    [InlineData("try { } catch (InvalidOperationException) { }", 2)]
    [InlineData("try { } catch (InvalidOperationException) when (value > 0) { }", 3)]
    [InlineData("switch (value) { case > 0 when value < 10: break; }", 3)]
    [InlineData("_ = value > 0 && value < 10;", 2)]
    [InlineData("_ = value < 0 || value > 10;", 2)]
    [InlineData("_ = text ?? \"fallback\";", 2)]
    [InlineData("_ = value > 0 ? 1 : 0;", 2)]
    [InlineData("_ = value is > 0 and < 10;", 3)]
    [InlineData("_ = value is < 0 or > 10;", 3)]
    [InlineData("_ = value is > 0;", 2)]
    [InlineData("_ = obj is string textValue;", 2)]
    public void CyclomaticComplexityCountsDocumentedDecisionPoints(string body, int expectedComplexity)
    {
        MethodDeclarationSyntax method = ParseTargetMethod(body);

        ControlFlowFacts facts = ControlFlowFactsCollector.Collect(method, CancellationToken.None);

        Assert.Equal(expectedComplexity, facts.CyclomaticComplexity);
    }

    [Fact]
    public void CyclomaticComplexityIgnoresDocumentedNonDecisionPoints()
    {
        MethodDeclarationSyntax method = ParseTargetMethod(
            """
            int[] values = [1, 2];
            var query = from item in values select item;
            _ = text?.Length;
            int result = 0;
            result = value;
            text ??= "fallback";
            Func<int> factory = () => throw new InvalidOperationException();
            await Task.CompletedTask;
            using var stream = new System.IO.MemoryStream();
            lock (obj)
            {
                result++;
            }
            """);

        ControlFlowFacts facts = ControlFlowFactsCollector.Collect(method, CancellationToken.None);

        Assert.Equal(1, facts.CyclomaticComplexity);
    }

    [Fact]
    public void ControlFlowFactsUseSinglePassForCognitiveComplexityAndNesting()
    {
        MethodDeclarationSyntax method = ParseTargetMethod(
            """
            if (value > 0)
            {
                while (value > 10)
                {
                    value--;
                }
            }
            """);

        ControlFlowFacts facts = ControlFlowFactsCollector.Collect(method, CancellationToken.None);

        Assert.Equal(3, facts.CyclomaticComplexity);
        Assert.Equal(3, facts.CognitiveComplexity);
        Assert.Equal(2, facts.NestingDepth);
    }

    private static MethodDeclarationSyntax ParseTargetMethod(string body)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            $$"""
            using System;
            using System.Linq;
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task Target(int value, string? text, object obj)
                {
                    {{body}}
                }
            }
            """);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
    }
}
