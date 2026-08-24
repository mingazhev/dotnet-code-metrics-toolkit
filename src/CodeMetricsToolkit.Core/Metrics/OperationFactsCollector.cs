using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace CodeMetricsToolkit.Core.Metrics;

internal static class OperationFactsCollector
{
    public static OperationFacts? Collect(
        MemberDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        List<IOperation> roots = FindOperationRoots(
            declaration,
            semanticModel,
            cancellationToken);
        if (roots.Count == 0)
        {
            return null;
        }

        var operationCount = 0;
        var allocationCount = 0;
        var awaitCount = 0;
        var graphCount = 0;
        var blockCount = 0;
        var reachableBlockCount = 0;
        var edgeCount = 0;

        foreach (IOperation root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (IOperation operation in DescendantsAndSelf(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!operation.IsImplicit &&
                    operation is not IMethodBodyOperation &&
                    operation is not IConstructorBodyOperation &&
                    operation is not IBlockOperation)
                {
                    operationCount++;
                }

                if (!operation.IsImplicit &&
                    operation is IObjectCreationOperation or
                        IArrayCreationOperation or
                        IAnonymousObjectCreationOperation or
                        IDelegateCreationOperation)
                {
                    allocationCount++;
                }

                if (!operation.IsImplicit && operation is IAwaitOperation)
                {
                    awaitCount++;
                }
            }

            ControlFlowGraph? graph = TryCreateControlFlowGraph(root, cancellationToken);
            if (graph is null)
            {
                continue;
            }

            graphCount++;
            blockCount += graph.Blocks.Length;
            reachableBlockCount += graph.Blocks.Count(block => block.IsReachable);

            foreach (BasicBlock block in graph.Blocks)
            {
                if (block.FallThroughSuccessor?.Destination is not null)
                {
                    edgeCount++;
                }

                if (block.ConditionalSuccessor?.Destination is not null)
                {
                    edgeCount++;
                }
            }
        }

        return new OperationFacts
        {
            OperationCount = operationCount,
            AllocationCount = allocationCount,
            AwaitCount = awaitCount,
            ControlFlowGraphCount = graphCount,
            BasicBlockCount = blockCount,
            ReachableBasicBlockCount = reachableBlockCount,
            UnreachableBasicBlockCount = blockCount - reachableBlockCount,
            ControlFlowEdgeCount = edgeCount,
            CfgCyclomaticComplexity = graphCount == 0
                ? 0
                : Math.Max(1, edgeCount - blockCount + (2 * graphCount))
        };
    }

    private static List<IOperation> FindOperationRoots(
        MemberDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        IEnumerable<SyntaxNode> candidates = new[] { declaration }
            .Concat(declaration.DescendantNodes()
                .Where(node => node is AccessorDeclarationSyntax or ArrowExpressionClauseSyntax));
        var roots = new List<IOperation>();
        var seen = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);

        foreach (SyntaxNode candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IOperation? operation = semanticModel.GetOperation(candidate, cancellationToken);
            while (operation?.Parent is not null)
            {
                operation = operation.Parent;
            }

            if (operation is not null && seen.Add(operation))
            {
                roots.Add(operation);
            }
        }

        return roots;
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            IOperation current = pending.Pop();
            yield return current;

            foreach (IOperation child in current.ChildOperations.Reverse())
            {
                pending.Push(child);
            }
        }
    }

    private static ControlFlowGraph? TryCreateControlFlowGraph(
        IOperation root,
        CancellationToken cancellationToken)
    {
        try
        {
            return root switch
            {
                IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody, cancellationToken),
                IConstructorBodyOperation constructorBody => ControlFlowGraph.Create(constructorBody, cancellationToken),
                IBlockOperation block => ControlFlowGraph.Create(block, cancellationToken),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
        {
            return null;
        }
    }
}
