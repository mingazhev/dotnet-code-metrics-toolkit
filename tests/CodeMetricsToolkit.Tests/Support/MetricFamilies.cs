namespace CodeMetricsToolkit.Tests.Support;

internal static class MetricFamilies
{
    public static IReadOnlySet<string> Syntax { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "lines_of_code",
        "token_line_count",
        "blank_line_count",
        "comment_only_line_count",
        "commented_line_count",
        "mixed_code_comment_line_count",
        "documentation_comment_line_count",
        "token_line_ratio",
        "blank_line_ratio",
        "comment_only_line_ratio",
        "commented_line_ratio",
        "documentation_comment_ratio",
        "member_length",
        "parameter_count",
        "cyclomatic_complexity",
        "decision_point_count",
        "cognitive_complexity",
        "nesting_depth",
        "type_count",
        "member_count",
        "max_member_cyclomatic_complexity",
        "p95_member_cyclomatic_complexity",
        "max_member_cognitive_complexity",
        "p95_member_cognitive_complexity",
        "max_member_nesting_depth"
    };

    public static IReadOnlySet<string> Graph { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "outgoing_type_dependency_count",
        "incoming_type_dependency_count",
        "dependency_cycle_membership",
        "distinct_outgoing_callee_count",
        "distinct_incoming_caller_count",
        "recursive_component_size",
        "dependency_component_size",
        "transitive_type_dependency_count",
        "transitive_type_dependent_count"
    };

    public static IReadOnlySet<string> Semantic { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "inheritance_depth",
        "type_coupling",
        "public_api_count",
        "documented_public_api_count",
        "public_api_documentation_ratio",
        "operation_count",
        "allocation_count",
        "await_count",
        "control_flow_graph_count",
        "basic_block_count",
        "reachable_basic_block_count",
        "unreachable_basic_block_count",
        "control_flow_edge_count",
        "cfg_cyclomatic_complexity"
    };

    public static IReadOnlySet<string> Diagnostics { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "diagnostic_count" };

    public static IReadOnlySet<string> Hotspots { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "hotspot_rank" };

    public static IReadOnlySet<string> All { get; } = Syntax
        .Concat(Graph)
        .Concat(Semantic)
        .Concat(Diagnostics)
        .Concat(Hotspots)
        .ToHashSet(StringComparer.Ordinal);
}
