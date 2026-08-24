using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Core.Reporting;

public static class SyntaxMetricProjector
{
    public static IReadOnlyList<MetricResultLine> Project(SyntaxAnalysisFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var results = new List<MetricResultLine>();
        FileFacts[] uniqueFiles = facts.Files
            .GroupBy(file => file.FilePath, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(file => file.FilePath, StringComparer.Ordinal)
            .ToArray();
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

        AddLineMetrics(
            results,
            "solution",
            TargetIds.SolutionRoot,
            "syntax_fallback",
            ".",
            1,
            1,
            LineAggregate.FromFiles(uniqueFiles));

        foreach (var projectPath in facts.ProjectPaths.Order(StringComparer.Ordinal))
        {
            var projectKey = ProjectIdentity.Key(projectPath);
            HashSet<string> projectFilePaths = facts.ProjectFileMemberships is null
                ? facts.Files
                    .Where(file => file.ProjectKey == projectKey)
                    .Select(file => file.FilePath)
                    .ToHashSet(StringComparer.Ordinal)
                : facts.ProjectFileMemberships
                    .Where(membership => membership.ProjectKey == projectKey)
                    .Select(membership => membership.FilePath)
                    .ToHashSet(StringComparer.Ordinal);
            AddLineMetrics(
                results,
                "project",
                ProjectIdentity.TargetId(projectPath),
                "syntax_fallback",
                projectPath,
                1,
                1,
                LineAggregate.FromFiles(facts.Files
                    .Where(file => projectFilePaths.Contains(file.FilePath))
                    .GroupBy(file => file.FilePath, StringComparer.Ordinal)
                    .Select(group => group.First())));
        }

        foreach (FileFacts file in uniqueFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddLineMetrics(
                results,
                "file",
                file.TargetId,
                file.TargetIdStability,
                file.FilePath,
                file.StartLine,
                file.EndLine,
                LineAggregate.FromFile(file));
            AddNumericMetric(results, "type_count", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, typeCountsByFile.GetValueOrDefault(file.FilePath), "count");

            IReadOnlyList<MemberFacts> members = membersByFile.GetValueOrDefault(file.FilePath, []);
            AddNumericMetric(results, "member_count", "1.0.0", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, members.Count, "count");
            AddMemberAggregateMetrics(results, "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, members);
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "lines_of_code", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.LinesOfCode, "lines");
            AddNumericMetric(results, "token_line_count", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.NonCommentLinesOfCode, "lines");
            AddNumericMetric(results, "member_count", "1.0.0", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.MemberCount, "count");
            AddMemberAggregateMetrics(results, "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, membersByType.GetValueOrDefault(type.TargetId, []));
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "member_length", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.MethodLength, "lines");
            AddNumericMetric(results, "parameter_count", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ParameterCount, "count");
            AddNumericMetric(results, "cyclomatic_complexity", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.CyclomaticComplexity, "count");
            AddNumericMetric(results, "decision_point_count", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.DecisionPointCount, "count");
            AddNumericMetric(results, "cognitive_complexity", "0.1.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.CognitiveComplexity, "count");
            AddNumericMetric(results, "nesting_depth", "1.0.0", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.NestingDepth, "levels");
        }

        return results;
    }

    private static void AddLineMetrics(
        List<MetricResultLine> results,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        LineAggregate lines)
    {
        AddNumericMetric(results, "lines_of_code", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.LinesOfCode, "lines");
        AddNumericMetric(results, "token_line_count", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.NonCommentLinesOfCode, "lines");
        AddNumericMetric(results, "blank_line_count", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.BlankLineCount, "lines");
        AddNumericMetric(results, "comment_only_line_count", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.CommentOnlyLineCount, "lines");
        AddNumericMetric(results, "commented_line_count", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.CommentedLineCount, "lines");
        AddNumericMetric(results, "mixed_code_comment_line_count", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.MixedCodeCommentLineCount, "lines");
        AddNumericMetric(results, "documentation_comment_line_count", "1.0.0", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.DocumentationCommentLineCount, "lines");

        if (lines.LinesOfCode == 0)
        {
            return;
        }

        AddRatioMetric(results, "token_line_ratio", targetKind, targetId, targetIdStability, filePath, startLine, endLine, (double)lines.NonCommentLinesOfCode / lines.LinesOfCode);
        AddRatioMetric(results, "blank_line_ratio", targetKind, targetId, targetIdStability, filePath, startLine, endLine, (double)lines.BlankLineCount / lines.LinesOfCode);
        AddRatioMetric(results, "comment_only_line_ratio", targetKind, targetId, targetIdStability, filePath, startLine, endLine, (double)lines.CommentOnlyLineCount / lines.LinesOfCode);
        AddRatioMetric(results, "commented_line_ratio", targetKind, targetId, targetIdStability, filePath, startLine, endLine, (double)lines.CommentedLineCount / lines.LinesOfCode);
        AddRatioMetric(results, "documentation_comment_ratio", targetKind, targetId, targetIdStability, filePath, startLine, endLine, (double)lines.DocumentationCommentLineCount / lines.LinesOfCode);
    }

    private static void AddRatioMetric(
        List<MetricResultLine> results,
        string metricId,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        double value)
    {
        results.Add(MetricResultFactory.Number(
            metricId,
            "1.0.0",
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            value,
            "ratio"));
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
        double value,
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

    private sealed record LineAggregate(
        int LinesOfCode,
        int NonCommentLinesOfCode,
        int BlankLineCount,
        int CommentOnlyLineCount,
        int CommentedLineCount,
        int MixedCodeCommentLineCount,
        int DocumentationCommentLineCount)
    {
        public static LineAggregate FromFile(FileFacts file)
        {
            return new LineAggregate(
                file.LinesOfCode,
                file.NonCommentLinesOfCode,
                file.BlankLineCount,
                file.CommentOnlyLineCount,
                file.CommentedLineCount,
                file.MixedCodeCommentLineCount,
                file.DocumentationCommentLineCount);
        }

        public static LineAggregate FromFiles(IEnumerable<FileFacts> files)
        {
            var aggregate = new LineAggregate(0, 0, 0, 0, 0, 0, 0);

            foreach (FileFacts file in files)
            {
                aggregate = new LineAggregate(
                    aggregate.LinesOfCode + file.LinesOfCode,
                    aggregate.NonCommentLinesOfCode + file.NonCommentLinesOfCode,
                    aggregate.BlankLineCount + file.BlankLineCount,
                    aggregate.CommentOnlyLineCount + file.CommentOnlyLineCount,
                    aggregate.CommentedLineCount + file.CommentedLineCount,
                    aggregate.MixedCodeCommentLineCount + file.MixedCodeCommentLineCount,
                    aggregate.DocumentationCommentLineCount + file.DocumentationCommentLineCount);
            }

            return aggregate;
        }
    }
}
