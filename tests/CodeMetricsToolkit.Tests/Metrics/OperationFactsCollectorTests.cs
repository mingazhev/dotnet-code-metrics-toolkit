using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Tests.Support;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class OperationFactsCollectorTests
{
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
