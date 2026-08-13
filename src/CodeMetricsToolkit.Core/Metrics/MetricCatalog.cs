namespace CodeMetricsToolkit.Core.Metrics;

public static class MetricCatalog
{
    public static IReadOnlyList<MetricDescriptor> All { get; } =
    [
        new("lines_of_code", "1.0.0", ["solution", "project", "file", "type"], "syntax", "lines", "inclusive source line span count", "Counts physical source lines in the target span or unique file population.", ["Counts blank/comment-only lines.", "Project and solution totals aggregate unique file paths; type spans must not be summed into repository LOC."]),
        new("non_comment_lines_of_code", "1.0.0", ["solution", "project", "file", "type"], "syntax", "lines", "distinct source lines containing syntax tokens", "Counts lines with non-comment C# tokens in the target span or unique file population.", ["Preprocessor-only lines are not counted unless they contain syntax tokens."]),
        new("blank_line_count", "1.0.0", ["solution", "project", "file"], "syntax", "lines", "physical lines containing only whitespace and no comment trivia", "Counts source lines with no tokens or comment trivia.", ["A visually empty line inside a multiline comment is a comment-only line, not a blank line."]),
        new("comment_only_line_count", "1.0.0", ["solution", "project", "file"], "syntax", "lines", "lines intersecting comment trivia and containing no syntax token", "Counts standalone ordinary and documentation comment lines.", ["Includes visually empty lines spanned by a multiline comment."]),
        new("commented_line_count", "1.0.0", ["solution", "project", "file"], "syntax", "lines", "distinct lines intersecting ordinary or documentation comment trivia", "Counts all comment-bearing lines.", ["Overlaps non_comment_lines_of_code for mixed code/comment lines."]),
        new("mixed_code_comment_line_count", "1.0.0", ["solution", "project", "file"], "syntax", "lines", "lines containing both a syntax token and comment trivia", "Makes the deliberate overlap between code-bearing and comment-bearing lines explicit.", []),
        new("documentation_comment_line_count", "1.0.0", ["solution", "project", "file"], "syntax", "lines", "distinct lines intersecting XML documentation trivia", "Counts XML documentation comment lines.", ["Does not imply that the documented symbol is public."]),
        new("token_line_ratio", "1.0.0", ["solution", "project", "file"], "derived", "ratio", "non_comment_lines_of_code / lines_of_code", "Reports the share of physical lines containing C# syntax tokens.", ["Descriptive only; not monotonic code quality."]),
        new("blank_line_ratio", "1.0.0", ["solution", "project", "file"], "derived", "ratio", "blank_line_count / lines_of_code", "Reports the whitespace-only share of physical lines.", ["Descriptive only; not monotonic code quality."]),
        new("comment_only_line_ratio", "1.0.0", ["solution", "project", "file"], "derived", "ratio", "comment_only_line_count / lines_of_code", "Reports the standalone-comment share of physical lines.", ["Descriptive only; not documentation quality."]),
        new("commented_line_ratio", "1.0.0", ["solution", "project", "file"], "derived", "ratio", "commented_line_count / lines_of_code", "Reports the comment-bearing share of physical lines.", ["Mixed lines also contribute to token_line_ratio."]),
        new("documentation_comment_ratio", "1.0.0", ["solution", "project", "file"], "derived", "ratio", "documentation_comment_line_count / lines_of_code", "Reports the XML-documentation share of physical lines.", ["Use public_api_documentation_ratio to measure documented API symbols."]),
        new("method_length", "1.0.0", ["member"], "syntax", "lines", "inclusive source line span count for the member declaration", "Counts source lines occupied by a member declaration.", ["Expression-bodied members can be one line even when semantically dense."]),
        new("parameter_count", "1.0.0", ["member"], "syntax", "count", "number of declared parameters", "Counts method, constructor, operator and indexer parameters.", ["Property setter value parameters are not counted."]),
        new("cyclomatic_complexity", "1.0.0", ["member"], "syntax", "count", "1 + decision_points", "Counts independent control-flow paths using the MVP decision-point table.", ["Not a full NDepend/Sonar compatibility claim."]),
        new("decision_point_count", "1.0.0", ["member"], "syntax", "count", "syntax decision points used by cyclomatic_complexity", "Publishes the non-baseline component of syntax cyclomatic complexity.", ["Useful as a transparent density numerator; not a CFG edge count."]),
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
        new("outgoing_call_count", "1.0.0", ["member", "type"], "semantic", "count", "distinct internal member callees", "Counts resolved internal callees from call graph edges.", ["Type targets aggregate distinct member callees across their members.", "Reflection, dynamic dispatch, framework callbacks and external calls are not represented."]),
        new("incoming_call_count", "1.0.0", ["member", "type"], "semantic", "count", "distinct internal member callers", "Counts resolved internal callers from call graph edges.", ["Type targets aggregate distinct calling members across their members."]),
        new("recursive_component_size", "1.0.0", ["member"], "semantic", "count", "size of the call-graph strongly connected component, or 0 outside recursion", "Reports direct or mutual recursion component size.", ["A self-recursive member reports 1."]),
        new("dependency_component_size", "1.0.0", ["type"], "semantic", "count", "size of the type-dependency strongly connected component, or 0 outside a cycle", "Reports the size of a cyclic internal type dependency component.", ["Does not enumerate all simple cycles."]),
        new("transitive_type_dependency_count", "1.0.0", ["type"], "semantic", "count", "distinct internal type targets reachable through dependency edges", "Counts transitive internal dependencies excluding the source type.", ["Depends on complete trusted semantic graph coverage."]),
        new("transitive_type_dependent_count", "1.0.0", ["type"], "semantic", "count", "distinct internal type targets that can reach the target", "Counts transitive internal dependents excluding the target type.", ["Depends on complete trusted semantic graph coverage."]),
        new("inheritance_depth", "1.0.0", ["type"], "semantic", "levels", "number of non-System.Object base classes", "Counts class inheritance levels above the target; direct System.Object inheritance is depth 0.", ["Interfaces and value types report 0."]),
        new("class_coupling", "1.0.0", ["type"], "semantic", "count", "distinct non-special named types in bases, interfaces, attributes, signatures and type syntax", "Counts semantic type coupling across internal and external named types.", ["Primitive special types, type parameters, error types and the target type itself are excluded.", "Not a compatibility claim with Visual Studio or another CBO implementation."]),
        new("public_api_count", "1.0.0", ["project", "type"], "semantic", "count", "effectively public/protected declared API symbols", "Counts externally accessible source types and explicit members.", ["Implicit compiler-generated members and accessor methods are excluded.", "A public member inside an inaccessible containing type is not public API."]),
        new("documented_public_api_count", "1.0.0", ["project", "type"], "semantic", "count", "public API symbols with non-empty XML documentation", "Counts public API symbols carrying XML documentation.", ["Documentation content quality is not evaluated."]),
        new("public_api_documentation_ratio", "1.0.0", ["project", "type"], "derived", "ratio", "documented_public_api_count / public_api_count", "Reports XML documentation coverage of the public API population.", ["Omitted when public_api_count is zero."]),
        new("operation_count", "1.0.0", ["member"], "semantic", "count", "explicit IOperation nodes excluding body/block wrappers", "Counts language-normalized explicit operations in executable member bodies.", ["Implicit compiler operations are excluded."]),
        new("allocation_count", "1.0.0", ["member"], "semantic", "count", "explicit object, array, anonymous-object and delegate creation operations", "Counts static allocation sites represented by Roslyn operations.", ["Not allocated bytes or runtime frequency."]),
        new("await_count", "1.0.0", ["member"], "semantic", "count", "explicit IAwaitOperation nodes", "Counts await operations in executable member bodies.", []),
        new("control_flow_graph_count", "1.0.0", ["member"], "semantic", "count", "Roslyn control-flow graphs aggregated into the member observation", "Publishes the component count used by CFG aggregate formulas.", ["Properties can contain multiple accessor graphs."]),
        new("basic_block_count", "1.0.0", ["member"], "semantic", "count", "all Roslyn CFG blocks including entry and exit", "Counts basic blocks across executable graphs for the member.", []),
        new("reachable_basic_block_count", "1.0.0", ["member"], "semantic", "count", "Roslyn CFG blocks where IsReachable is true", "Counts reachable basic blocks including reachable entry/exit blocks.", []),
        new("unreachable_basic_block_count", "1.0.0", ["member"], "semantic", "count", "basic_block_count - reachable_basic_block_count", "Counts CFG blocks Roslyn marks unreachable.", []),
        new("control_flow_edge_count", "1.0.0", ["member"], "semantic", "count", "non-null fall-through and conditional CFG successors", "Counts directed Roslyn control-flow branches across member graphs.", []),
        new("cfg_cyclomatic_complexity", "1.0.0", ["member"], "semantic", "count", "E - N + 2P over Roslyn CFG edges, blocks and graph components", "Computes graph-based cyclomatic complexity separately from the syntax metric.", ["E is control_flow_edge_count, N is basic_block_count, and P is control_flow_graph_count.", "Omitted when no CFG can be constructed."]),
        new("diagnostic_count", "1.0.0", ["file", "type", "member"], "syntax/semantic", "count", "number of diagnostics whose span overlaps the target", "Counts syntax, compiler and nullable diagnostics for source-linked targets.", ["Emitted only when summary.analysisHealth.trustedDiagnostics is true.", "Project-load diagnostics without source spans are emitted in diagnostics.ndjson but not attributed to source targets.", "The toolkit does not execute third-party DiagnosticAnalyzer instances."]),
        new("hotspot_rank", "1.0.0", ["file", "type", "member"], "derived", "rank", "weighted empirical-percentile rank over target-kind-specific component metrics", "Built-in, opinionated navigation heuristic for finding review candidates.", ["Ranking is prioritization, not a proof of code quality or a universal objective.", "Diagnostic components are excluded when summary.analysisHealth.diagnosticsIncludedInHotspotRank is false."])
    ];

    public static MetricDescriptor? Find(string idOrVersionedId)
    {
        var id = idOrVersionedId;
        string? version = null;
        var separator = idOrVersionedId.IndexOf('@', StringComparison.Ordinal);

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
