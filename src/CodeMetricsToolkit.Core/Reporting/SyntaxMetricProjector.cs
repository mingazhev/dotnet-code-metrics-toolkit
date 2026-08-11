using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class SyntaxMetricProjector
{
    public static IReadOnlyList<MetricResultLine> Project(SyntaxAnalysisFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var results = new List<MetricResultLine>();
        IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> membersByType = facts.Members
            .GroupBy(member => member.ParentTypeTargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MemberFacts>)group.ToArray(),
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> membersByFile = facts.Members
            .GroupBy(member => member.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MemberFacts>)group.ToArray(),
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, int> typeCountsByFile = facts.Types
            .GroupBy(type => type.FilePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (FileFacts file in facts.Files.OrderBy(file => file.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "lines_of_code", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, file.LinesOfCode, "lines");
            AddNumericMetric(results, "non_comment_lines_of_code", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, file.NonCommentLinesOfCode, "lines");
            AddNumericMetric(results, "type_count", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, typeCountsByFile.GetValueOrDefault(file.FilePath), "count");

            IReadOnlyList<MemberFacts> members = membersByFile.GetValueOrDefault(file.FilePath, []);
            AddNumericMetric(results, "member_count", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, members.Count, "count");
            AddMemberAggregateMetrics(results, "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, members);
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "lines_of_code", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.LinesOfCode, "lines");
            AddNumericMetric(results, "non_comment_lines_of_code", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.NonCommentLinesOfCode, "lines");
            AddNumericMetric(results, "member_count", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.MemberCount, "count");
            AddMemberAggregateMetrics(results, "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, membersByType.GetValueOrDefault(type.TargetId, []));
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "method_length", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.MethodLength, "lines");
            AddNumericMetric(results, "parameter_count", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ParameterCount, "count");
            AddNumericMetric(results, "cyclomatic_complexity", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.CyclomaticComplexity, "count");
            AddNumericMetric(results, "cognitive_complexity", "0.1.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.CognitiveComplexity, "count");
            AddNumericMetric(results, "nesting_depth", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.NestingDepth, "levels");
        }

        return results;
    }

    private static void AddMemberAggregateMetrics(
        List<MetricResultLine> results,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        IReadOnlyList<MemberFacts> members)
    {
        AddNumericMetric(results, "max_member_cyclomatic_complexity", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.ControlFlow.CyclomaticComplexity), "count");
        AddNumericMetric(results, "p95_member_cyclomatic_complexity", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Percentile(members, member => member.ControlFlow.CyclomaticComplexity, 0.95), "count");
        AddNumericMetric(results, "max_member_cognitive_complexity", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.ControlFlow.CognitiveComplexity), "count");
        AddNumericMetric(results, "p95_member_cognitive_complexity", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Percentile(members, member => member.ControlFlow.CognitiveComplexity, 0.95), "count");
        AddNumericMetric(results, "max_member_nesting_depth", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.ControlFlow.NestingDepth), "levels");
    }

    private static int Max(IReadOnlyList<MemberFacts> members, Func<MemberFacts, int> selector)
    {
        return members.Count == 0 ? 0 : members.Max(selector);
    }

    private static int Percentile(IReadOnlyList<MemberFacts> members, Func<MemberFacts, int> selector, double percentile)
    {
        if (members.Count == 0)
        {
            return 0;
        }

        var ordered = members
            .Select(selector)
            .Order()
            .ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;

        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static void AddNumericMetric(
        List<MetricResultLine> results,
        string metricId,
        string metricVersion,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        int value,
        string unit)
    {
        results.Add(MetricResultFactory.Numeric(
            metricId,
            metricVersion,
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            value,
            unit));
    }
}
