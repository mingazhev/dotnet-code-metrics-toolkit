"""Audit an emitted graph.json; run from the repository root (Python stdlib only)."""
import collections
import hashlib
import json
import pathlib
import sys


def inspect_graph(nodes, edges):
    adjacency = {node: set() for node in nodes}
    for source, target in edges:
        adjacency[source].add(target)

    def reachable(start, skip=None):
        seen, pending = set(), [start]
        while pending:
            source = pending.pop()
            for target in adjacency[source]:
                if (source, target) != skip and target not in seen:
                    seen.add(target)
                    pending.append(target)
        return seen

    reach = {node: reachable(node) for node in nodes}
    remaining, components = set(nodes), []
    while remaining:
        node = min(remaining)
        component = {other for other in remaining if other in reach[node] and node in reach[other]} | {node}
        remaining -= component
        components.append(sorted(component))
    pairs = set(edges)
    return {
        "nodes": len(nodes), "edges": len(pairs),
        "cycles_scc": sorted(c for c in components if len(c) > 1),
        "self_loops": sorted(a for a, b in pairs if a == b),
        "isolates": sorted(n for n in nodes if not adjacency[n] and not any(n in v for v in adjacency.values())),
        "degrees": {n: {"in": sum(n in v for v in adjacency.values()), "out": len(adjacency[n])} for n in sorted(nodes)},
        "alternate_path_edges": sorted([a, b] for a, b in pairs if a != b and b in reachable(a, (a, b))),
    }


def main():
    # Exercise direction, SCCs, self-loops, isolates and transitive alternatives.
    check = inspect_graph(list("abcde"), [("a", "b"), ("b", "c"), ("a", "c"), ("c", "b"), ("d", "d")])
    assert check["cycles_scc"] == [["b", "c"]]
    assert check["self_loops"] == ["d"] and check["isolates"] == ["e"]
    assert check["alternate_path_edges"] == [["a", "b"], ["a", "c"]]
    assert check["degrees"]["b"] == {"in": 2, "out": 1}

    path = pathlib.Path(sys.argv[1])
    raw = path.read_bytes()
    graph = json.loads(raw)
    nodes = {n["id"]: n for n in graph["nodes"]}
    assert len(nodes) == len(graph["nodes"]), "Duplicate node IDs"
    edges = graph["edges"]
    assert all(e["from"] in nodes and e["to"] in nodes for e in edges), "Dangling edge"
    result = {
        "source_sha256": hashlib.sha256(raw).hexdigest(),
        "node_kinds": dict(collections.Counter(n["kind"] for n in nodes.values())),
        "edge_kinds": dict(collections.Counter(e["kind"] for e in edges)),
        "duplicate_edges": len(edges) - len({(e["from"], e["to"], e["kind"], e["confidence"]) for e in edges}),
    }
    for kind, relation in [("project", "depends_on"), ("namespace", "depends_on"), ("member", "calls")]:
        selected = {key: n for key, n in nodes.items() if n["kind"] == kind}
        pairs = [(e["from"], e["to"]) for e in edges if e["kind"] == relation and e["from"] in selected and e["to"] in selected]
        audit = inspect_graph(selected, pairs)
        audit["cycles_scc"] = [[nodes[n]["name"] for n in c] for c in audit["cycles_scc"]]
        audit["self_loops"] = [{"id": n, "name": nodes[n]["name"], "file": nodes[n].get("filePath"), "line": nodes[n].get("startLine")} for n in audit["self_loops"]]
        audit["isolates"] = [nodes[n]["name"] for n in audit["isolates"]]
        audit["alternate_path_edges"] = [[nodes[a]["name"], nodes[b]["name"]] for a, b in audit["alternate_path_edges"]]
        if kind == "member":
            audit["top_out_degree"] = [{"id": n, "name": nodes[n]["name"], **d} for n, d in sorted(audit["degrees"].items(), key=lambda entry: (-entry[1]["out"], entry[0]))[:10]]
            del audit["degrees"]
            # Overload names are ambiguous: keep only the count of transitive candidates.
            audit["alternate_path_edge_count"] = len(audit.pop("alternate_path_edges"))
            audit["isolate_count"] = len(audit.pop("isolates"))
        else:
            audit["degrees"] = {nodes[n]["name"]: d for n, d in audit["degrees"].items()}
            audit["edges_named"] = sorted([nodes[a]["name"], nodes[b]["name"]] for a, b in pairs)
        result[kind] = audit
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
