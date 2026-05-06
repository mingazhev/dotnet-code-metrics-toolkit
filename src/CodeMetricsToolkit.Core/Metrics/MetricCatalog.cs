namespace CodeMetricsToolkit.Core.Metrics;

public static class MetricCatalog
{
    public static IReadOnlyList<MetricDescriptor> All { get; } =
    [
        new("lines_of_code", "1.0.0", ["file", "type"], "syntax", "lines", "inclusive source line span count", "Counts physical source lines in the target span.", ["Counts blank/comment-only lines."]),
        new("non_comment_lines_of_code", "1.0.0", ["file", "type"], "syntax", "lines", "distinct source lines containing syntax tokens", "Counts lines with non-comment C# tokens in the target span.", ["Preprocessor-only lines are not counted unless they contain syntax tokens."]),
        new("method_length", "1.0.0", ["member"], "syntax", "lines", "inclusive source line span count for the member declaration", "Counts source lines occupied by a member declaration.", ["Expression-bodied members can be one line even when semantically dense."]),
        new("parameter_count", "1.0.0", ["member"], "syntax", "count", "number of declared parameters", "Counts method, constructor, operator and indexer parameters.", ["Property setter value parameters are not counted."]),
        new("cyclomatic_complexity", "1.0.0", ["member"], "syntax", "count", "1 + decision_points", "Counts independent control-flow paths using the MVP decision-point table.", ["Not a full NDepend/Sonar compatibility claim."]),
        new("cognitive_complexity", "0.1.0", ["member"], "syntax", "count", "structural decisions plus nesting increments", "Sonar-inspired cognitive complexity baseline.", ["Not Sonar-compatible; version stays 0.1.0 until golden samples prove compatibility."]),
        new("nesting_depth", "1.0.0", ["member"], "syntax", "levels", "max active structural decision depth", "Captures deepest nested structural decision level inside a member.", ["Boolean operators add complexity but do not increase nesting depth."]),
        new("type_count", "1.0.0", ["file"], "syntax", "count", "number of type declarations in the file", "Counts discovered type targets per file.", ["Partial type declarations count where their declaration appears."]),
        new("member_count", "1.0.0", ["file", "type"], "syntax", "count", "number of member targets contained by the target", "Counts discovered member targets per file or type.", ["Generated code is excluded unless --include-generated is set."]),
        new("max_member_cyclomatic_complexity", "1.0.0", ["file", "type"], "syntax", "count", "max(cyclomatic_complexity for contained members)", "Worst member cyclomatic complexity inside a file or type.", ["Returns 0 when the target contains no members."]),
        new("p95_member_cyclomatic_complexity", "1.0.0", ["file", "type"], "syntax", "count", "nearest-rank p95 of contained member cyclomatic_complexity", "High-percentile member cyclomatic complexity inside a file or type.", ["Small samples collapse to the maximum value."]),
        new("max_member_cognitive_complexity", "1.0.0", ["file", "type"], "syntax", "count", "max(cognitive_complexity for contained members)", "Worst member cognitive complexity inside a file or type.", ["Uses cognitive_complexity@0.1.0 as its component formula."]),
        new("p95_member_cognitive_complexity", "1.0.0", ["file", "type"], "syntax", "count", "nearest-rank p95 of contained member cognitive_complexity", "High-percentile member cognitive complexity inside a file or type.", ["Uses cognitive_complexity@0.1.0 as its component formula."]),
        new("max_member_nesting_depth", "1.0.0", ["file", "type"], "syntax", "levels", "max(nesting_depth for contained members)", "Worst member nesting depth inside a file or type.", ["Returns 0 when the target contains no members."]),
        new("outgoing_type_dependency_count", "1.0.0", ["type"], "semantic", "count", "distinct internal type targets referenced by outgoing inherits/implements/uses_type edges", "Counts how many internal types a type depends on.", ["uses_type edges are best-effort and depend on semantic availability."]),
        new("incoming_type_dependency_count", "1.0.0", ["type"], "semantic", "count", "distinct internal source types with dependency edges to this type", "Counts internal types that depend on the target type.", ["Best-effort under partial semantic mode."]),
        new("dependency_cycle_count", "1.0.0", ["type"], "semantic", "count", "1 when the type belongs to a dependency SCC, otherwise 0", "Flags type dependency cycles from internal type dependency edges.", ["The MVP reports SCC membership, not an exact count of all simple cycles."]),
        new("diagnostic_count", "1.0.0", ["file", "type", "member"], "syntax/semantic", "count", "number of diagnostics whose span overlaps the target", "Counts syntax, compiler, nullable and analyzer diagnostics for source-linked targets.", ["Emitted only when summary.analysisHealth.trustedDiagnostics is true.", "Project-load diagnostics without source spans are emitted in diagnostics.ndjson but not attributed to source targets."]),
        new("hotspot_rank", "1.0.0", ["file", "type", "member"], "derived", "rank", "weighted percentile rank over target-kind-specific component metrics", "Explainable ranking for top-N autoresearch targets.", ["Ranking is prioritization, not a proof of code quality.", "Diagnostic components are excluded when summary.analysisHealth.diagnosticsIncludedInHotspotRank is false."])
    ];

    public static MetricDescriptor? Find(string idOrVersionedId)
    {
        string id = idOrVersionedId;
        string? version = null;
        int separator = idOrVersionedId.IndexOf('@', StringComparison.Ordinal);

        if (separator >= 0)
        {
            id = idOrVersionedId[..separator];
            version = idOrVersionedId[(separator + 1)..];
        }

        return All.FirstOrDefault(metric =>
            string.Equals(metric.Id, id, StringComparison.Ordinal) &&
            (version is null || string.Equals(metric.Version, version, StringComparison.Ordinal)));
    }
}
