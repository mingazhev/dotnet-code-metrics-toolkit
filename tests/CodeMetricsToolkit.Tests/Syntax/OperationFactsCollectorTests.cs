using CodeMetricsToolkit.Core.Syntax;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Tests.Support;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class OperationFactsCollectorTests
{
    [Theory]
    [InlineData("public int Target() => 1;", 1, 1, 3, 2)]
    [InlineData("public int Target() { return 1; }", 2, 1, 3, 2)]
    [InlineData("public Sample() { }", 0, 1, 3, 2)]
    [InlineData("public int Target => 1;", 1, 1, 3, 2)]
    [InlineData("public int Target { get { return 1; } set { } }", 2, 2, 5, 3)]
    public void CollectSupportsExecutableMemberForms(string source, int operations, int graphs, int blocks, int edges)
    {
        var context = RoslynTestCompilation.Create($"class Sample {{ {source} }}");
        MemberDeclarationSyntax declaration = context.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single().Members.Single();

        OperationFacts? facts = OperationFactsCollector.Collect(declaration, context.SemanticModel, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(operations, facts.OperationCount);
        Assert.Equal(graphs, facts.ControlFlowGraphCount);
        Assert.Equal(blocks, facts.BasicBlockCount);
        Assert.Equal(blocks, facts.ReachableBasicBlockCount);
        Assert.Equal(0, facts.UnreachableBasicBlockCount);
        Assert.Equal(edges, facts.ControlFlowEdgeCount);
        Assert.Equal(graphs, facts.CfgCyclomaticComplexity);
    }

    [Theory]
    [InlineData("public int Target { get; set; }")]
    [InlineData("public abstract int Target();")]
    public void CollectOmitsMembersWithoutAuthoredExecution(string source)
    {
        var context = RoslynTestCompilation.Create($"abstract class Sample {{ {source} }}");
        MemberDeclarationSyntax declaration = context.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single().Members.Single();

        Assert.Null(OperationFactsCollector.Collect(declaration, context.SemanticModel, CancellationToken.None));
    }

    [Fact]
    public void CollectCountsExplicitAllocationKindsIncludingNestedLambdaBody()
    {
        var context = RoslynTestCompilation.Create("""
            class Sample
            {
                public object Target()
                {
                    var item = new object();
                    var array = new int[2];
                    var anonymous = new { Value = 1 };
                    var callback = new System.Func<object>(() => new object());
                    return item;
                }
            }
            """);
        MethodDeclarationSyntax declaration = context.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        OperationFacts? facts = OperationFactsCollector.Collect(declaration, context.SemanticModel, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(5, facts.AllocationCount);
        Assert.Equal(0, facts.AwaitCount);
        Assert.Equal(1, facts.ControlFlowGraphCount);
        Assert.Equal(1, facts.CfgCyclomaticComplexity);
    }

    [Theory]
    [InlineData("throw new System.Exception();")]
    [InlineData("try { throw new System.Exception(); } catch { return 1; }")]
    [InlineData("try { return 1; } finally { System.Console.WriteLine(); }")]
    public void CfgFormulaHasDocumentedFloorForExceptionRegions(string body)
    {
        var context = RoslynTestCompilation.Create($"class Sample {{ int Target() {{ {body} }} }}");
        MethodDeclarationSyntax declaration = context.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        OperationFacts? facts = OperationFactsCollector.Collect(declaration, context.SemanticModel, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(1, facts.ControlFlowGraphCount);
        Assert.True(facts.ControlFlowEdgeCount - facts.BasicBlockCount + 2 < 1);
        Assert.Equal(1, facts.CfgCyclomaticComplexity);
        Assert.Equal(facts.BasicBlockCount, facts.ReachableBasicBlockCount + facts.UnreachableBasicBlockCount);
    }

    [Fact]
    public void CollectCountsOperationsAllocationsAwaitAndCfgComponentsExactly()
    {
        var context = RoslynTestCompilation.Create(
            """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task<int> Target(bool condition)
                {
                    object item = new();
                    await Task.Yield();
                    if (condition)
                    {
                        return item.GetHashCode();
                    }

                    return 0;
                }
            }
            """);
        MethodDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

        OperationFacts? facts = OperationFactsCollector.Collect(
            declaration,
            context.SemanticModel,
            CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(15, facts.OperationCount);
        Assert.Equal(1, facts.AllocationCount);
        Assert.Equal(1, facts.AwaitCount);
        Assert.Equal(1, facts.ControlFlowGraphCount);
        Assert.Equal(5, facts.BasicBlockCount);
        Assert.Equal(5, facts.ReachableBasicBlockCount);
        Assert.Equal(0, facts.UnreachableBasicBlockCount);
        Assert.Equal(5, facts.ControlFlowEdgeCount);
        Assert.Equal(2, facts.CfgCyclomaticComplexity);
    }

    [Fact]
    public void CollectReturnsNullForMemberWithoutExecutableBody()
    {
        var context = RoslynTestCompilation.Create(
            "public interface ISample { void Target(); }");
        MethodDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

        OperationFacts? facts = OperationFactsCollector.Collect(
            declaration,
            context.SemanticModel,
            CancellationToken.None);

        Assert.Null(facts);
    }

    [Fact]
    public void CollectReportsRoslynUnreachableBasicBlocksExactly()
    {
        var context = RoslynTestCompilation.Create(
            """
            public sealed class Sample
            {
                public int Target()
                {
                    if (false)
                    {
                        return 1;
                    }

                    return 2;
                }
            }
            """);
        MethodDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

        OperationFacts? facts = OperationFactsCollector.Collect(
            declaration,
            context.SemanticModel,
            CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(5, facts.BasicBlockCount);
        Assert.Equal(4, facts.ReachableBasicBlockCount);
        Assert.Equal(1, facts.UnreachableBasicBlockCount);
        Assert.Equal(5, facts.ControlFlowEdgeCount);
        Assert.Equal(2, facts.CfgCyclomaticComplexity);
    }
}
