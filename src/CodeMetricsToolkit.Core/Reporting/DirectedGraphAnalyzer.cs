namespace CodeMetricsToolkit.Core.Reporting;

internal static class DirectedGraphAnalyzer
{
    public static DirectedGraphAnalysis Analyze(
        Dictionary<string, HashSet<string>> outgoing,
        Dictionary<string, HashSet<string>> incoming,
        bool includeReachability,
        CancellationToken cancellationToken)
    {
        (Dictionary<string, int> ComponentByNode, int[] Sizes, bool[] Cyclic) components =
            FindComponents(outgoing, incoming, cancellationToken);
        if (!includeReachability)
        {
            return new DirectedGraphAnalysis(components, null, null);
        }

        HashSet<int>[] dag = BuildCondensationDag(outgoing, components.ComponentByNode, components.Sizes.Length);
        var outgoingCounts = CountReachableNodes(dag, components.Sizes, cancellationToken);
        var incomingCounts = CountReachableNodes(Reverse(dag), components.Sizes, cancellationToken);
        return new DirectedGraphAnalysis(components, outgoingCounts, incomingCounts);
    }

    private static (Dictionary<string, int> ComponentByNode, int[] Sizes, bool[] Cyclic) FindComponents(
        Dictionary<string, HashSet<string>> outgoing,
        Dictionary<string, HashSet<string>> incoming,
        CancellationToken cancellationToken)
    {
        var finishingOrder = new List<string>(outgoing.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in outgoing.Keys.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(node))
            {
                continue;
            }

            var traversal = new Stack<(string Node, IEnumerator<string> Targets)>();
            traversal.Push((node, outgoing[node].GetEnumerator()));
            while (traversal.TryPeek(out (string Node, IEnumerator<string> Targets) frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (frame.Targets.MoveNext())
                {
                    var target = frame.Targets.Current;
                    if (visited.Add(target))
                    {
                        traversal.Push((target, outgoing[target].GetEnumerator()));
                    }
                }
                else
                {
                    traversal.Pop();
                    frame.Targets.Dispose();
                    finishingOrder.Add(frame.Node);
                }
            }
        }

        var componentByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var components = new List<List<string>>();
        for (var orderIndex = finishingOrder.Count - 1; orderIndex >= 0; orderIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = finishingOrder[orderIndex];
            if (componentByNode.ContainsKey(root))
            {
                continue;
            }

            var componentIndex = components.Count;
            var component = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            componentByNode[root] = componentIndex;
            while (pending.TryPop(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                component.Add(current);
                foreach (var source in incoming[current])
                {
                    if (componentByNode.TryAdd(source, componentIndex))
                    {
                        pending.Push(source);
                    }
                }
            }

            components.Add(component);
        }

        var sizes = components.Select(component => component.Count).ToArray();
        var cyclic = components
            .Select(component => component.Count > 1 || outgoing[component[0]].Contains(component[0]))
            .ToArray();
        return (componentByNode, sizes, cyclic);
    }

    private static HashSet<int>[] BuildCondensationDag(
        Dictionary<string, HashSet<string>> outgoing,
        Dictionary<string, int> componentByNode,
        int componentCount)
    {
        HashSet<int>[] dag = Enumerable.Range(0, componentCount).Select(_ => new HashSet<int>()).ToArray();
        foreach (KeyValuePair<string, HashSet<string>> edge in outgoing)
        {
            var source = edge.Key;
            HashSet<string> targets = edge.Value;
            var sourceComponent = componentByNode[source];
            foreach (var target in targets)
            {
                var targetComponent = componentByNode[target];
                if (sourceComponent != targetComponent)
                {
                    dag[sourceComponent].Add(targetComponent);
                }
            }
        }

        return dag;
    }

    private static HashSet<int>[] Reverse(HashSet<int>[] dag)
    {
        HashSet<int>[] reversed = Enumerable.Range(0, dag.Length).Select(_ => new HashSet<int>()).ToArray();
        for (var source = 0; source < dag.Length; source++)
        {
            foreach (var target in dag[source])
            {
                reversed[target].Add(source);
            }
        }

        return reversed;
    }

    private static int[] CountReachableNodes(
        HashSet<int>[] dag,
        int[] componentSizes,
        CancellationToken cancellationToken)
    {
        var indegrees = new int[dag.Length];
        foreach (HashSet<int> targets in dag)
        {
            foreach (var target in targets)
            {
                indegrees[target]++;
            }
        }

        var ready = new Queue<int>(Enumerable.Range(0, dag.Length).Where(node => indegrees[node] == 0));
        var order = new List<int>(dag.Length);
        while (ready.TryDequeue(out var source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            order.Add(source);
            foreach (var target in dag[source])
            {
                if (--indegrees[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        if (order.Count != dag.Length)
        {
            throw new InvalidOperationException("The SCC condensation graph must be acyclic.");
        }

        var reachability = new ReachabilitySet[dag.Length];
        var counts = new int[dag.Length];
        for (var orderIndex = order.Count - 1; orderIndex >= 0; orderIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = order[orderIndex];
            if (dag[source].Count == 0)
            {
                reachability[source] = ReachabilitySet.Empty;
            }
            else if (dag[source].Count == 1)
            {
                var target = dag[source].Single();
                reachability[source] = ReachabilitySet.Prepend(
                    target,
                    componentSizes[target],
                    reachability[target]);
                counts[source] = reachability[source].WeightedCount;
            }
            else
            {
                (reachability[source], counts[source]) = MergeReachability(
                    dag[source],
                    reachability,
                    componentSizes,
                    cancellationToken);
            }
        }

        return counts;
    }

    private static (ReachabilitySet Set, int Count) MergeReachability(
        HashSet<int> targets,
        ReachabilitySet[] reachability,
        int[] componentSizes,
        CancellationToken cancellationToken)
    {
        var components = new HashSet<int>();
        var count = 0;
        foreach (var target in targets)
        {
            if (components.Add(target))
            {
                count += componentSizes[target];
            }

            foreach (var descendant in reachability[target].Enumerate())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (components.Add(descendant))
                {
                    count += componentSizes[descendant];
                }
            }
        }

        return (ReachabilitySet.Materialized(components, count), count);
    }

    private sealed class ReachabilitySet
    {
        private ReachabilitySet(int? head, ReachabilitySet? tail, HashSet<int>? values, int weightedCount)
        {
            Head = head;
            Tail = tail;
            Values = values;
            WeightedCount = weightedCount;
        }

        public static ReachabilitySet Empty { get; } = new(null, null, null, 0);
        private int? Head { get; }
        private ReachabilitySet? Tail { get; }
        private HashSet<int>? Values { get; }
        public int WeightedCount { get; }

        public static ReachabilitySet Prepend(int component, int size, ReachabilitySet tail) =>
            new(component, tail, null, checked(size + tail.WeightedCount));

        public static ReachabilitySet Materialized(HashSet<int> values, int count) =>
            new(null, null, values, count);

        public IEnumerable<int> Enumerate()
        {
            ReachabilitySet? current = this;
            while (current?.Head is int head)
            {
                yield return head;
                current = current.Tail;
            }

            if (current?.Values is not null)
            {
                foreach (var value in current.Values)
                {
                    yield return value;
                }
            }
        }
    }
}

internal sealed class DirectedGraphAnalysis
{
    private readonly Dictionary<string, int> componentByNode;
    private readonly int[] componentSizes;
    private readonly bool[] cyclicComponents;
    private readonly int[]? outgoingCounts;
    private readonly int[]? incomingCounts;

    public DirectedGraphAnalysis(
        (Dictionary<string, int> ComponentByNode, int[] Sizes, bool[] Cyclic) components,
        int[]? outgoingCounts,
        int[]? incomingCounts)
    {
        componentByNode = components.ComponentByNode;
        componentSizes = components.Sizes;
        cyclicComponents = components.Cyclic;
        this.outgoingCounts = outgoingCounts;
        this.incomingCounts = incomingCounts;
    }

    public int GetCyclicComponentSize(string node)
    {
        var component = componentByNode[node];
        return cyclicComponents[component] ? componentSizes[component] : 0;
    }

    public int GetTransitiveOutgoingCount(string node) => GetTransitiveCount(node, outgoingCounts);

    public int GetTransitiveIncomingCount(string node) => GetTransitiveCount(node, incomingCounts);

    private int GetTransitiveCount(string node, int[]? counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var component = componentByNode[node];
        return counts[component] + componentSizes[component] - 1;
    }
}
