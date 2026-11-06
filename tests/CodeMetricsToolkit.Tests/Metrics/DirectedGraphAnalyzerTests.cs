using CodeMetricsToolkit.Core.Metrics;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class DirectedGraphAnalyzerTests
{
    [Fact]
    public void ComponentsAndReachabilityMatchIndependentClosureForEveryThreeNodeGraph()
    {
        for (var mask = 0; mask < 512; mask++)
        {
            var edges = new bool[3, 3];
            for (var source = 0; source < 3; source++)
            {
                for (var target = 0; target < 3; target++)
                {
                    edges[source, target] = (mask & (1 << ((source * 3) + target))) != 0;
                }
            }

            AssertMatchesClosure(edges);
        }
    }

    [Fact]
    public void ComponentsAndReachabilityMatchIndependentClosureForSeededLargerGraphs()
    {
        var random = new Random(20261107);
        for (var sample = 0; sample < 200; sample++)
        {
            var size = random.Next(1, 17);
            var edges = new bool[size, size];
            for (var source = 0; source < size; source++)
            {
                for (var target = 0; target < size; target++)
                {
                    edges[source, target] = random.NextDouble() < 0.15;
                }
            }

            AssertMatchesClosure(edges);
        }
    }

    private static void AssertMatchesClosure(bool[,] edges)
    {
        var size = edges.GetLength(0);
        var nodes = Enumerable.Range(0, size).Select(index => $"node:{index}").ToArray();
        Dictionary<string, HashSet<string>> outgoing = nodes.ToDictionary(node => node, _ => new HashSet<string>());
        Dictionary<string, HashSet<string>> incoming = nodes.ToDictionary(node => node, _ => new HashSet<string>());
        for (var source = 0; source < size; source++)
        {
            for (var target = 0; target < size; target++)
            {
                if (edges[source, target])
                {
                    outgoing[nodes[source]].Add(nodes[target]);
                    incoming[nodes[target]].Add(nodes[source]);
                }
            }
        }

        // Floyd–Warshall is deliberately independent of the production SCC condensation algorithm.
        var reachable = (bool[,])edges.Clone();
        for (var via = 0; via < size; via++)
        {
            for (var source = 0; source < size; source++)
            {
                for (var target = 0; target < size; target++)
                {
                    reachable[source, target] |= reachable[source, via] && reachable[via, target];
                }
            }
        }

        DirectedGraphAnalysis actual = DirectedGraphAnalyzer.Analyze(outgoing, incoming, true, CancellationToken.None);
        DirectedGraphAnalysis componentsOnly = DirectedGraphAnalyzer.Analyze(outgoing, incoming, false, CancellationToken.None);
        for (var node = 0; node < size; node++)
        {
            var componentSize = reachable[node, node]
                ? Enumerable.Range(0, size).Count(other => reachable[node, other] && reachable[other, node])
                : 0;
            Assert.Equal(componentSize, actual.GetCyclicComponentSize(nodes[node]));
            Assert.Equal(componentSize, componentsOnly.GetCyclicComponentSize(nodes[node]));
            Assert.Equal(Enumerable.Range(0, size).Count(other => other != node && reachable[node, other]),
                actual.GetTransitiveOutgoingCount(nodes[node]));
            Assert.Equal(Enumerable.Range(0, size).Count(other => other != node && reachable[other, node]),
                actual.GetTransitiveIncomingCount(nodes[node]));
        }
    }
}
