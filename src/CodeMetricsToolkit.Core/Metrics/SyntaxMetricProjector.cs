using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Discovery;

namespace CodeMetricsToolkit.Core.Metrics;

public static class SyntaxMetricProjector
{
    public static IReadOnlyList<MetricResultLine> Project(AnalysisFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        cancellationToken.ThrowIfCancellationRequested();

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
                group => (IReadOnlyList<MemberFacts>)UniqueMembers(group),
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyList<MemberFacts>> membersByFile = facts.Members
            .GroupBy(member => member.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MemberFacts>)UniqueMembers(group),
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, int> typeCountsByFile = facts.Types
            .SelectMany(type => type.Declarations)
            .GroupBy(declaration => declaration.FilePath, StringComparer.Ordinal)
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
        AddScopeAggregateMetrics(
            results,
            "solution",
            TargetIds.SolutionRoot,
            "syntax_fallback",
            ".",
            1,
            1,
            UniqueMembers(facts.Members),
            UniqueTypes(facts.Types));

        foreach (var projectPath in facts.ProjectPaths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectKey = ProjectIdentity.Key(projectPath);
            var projectFilePaths = facts.GetProjectFileMemberships()
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
            AddScopeAggregateMetrics(
                results,
                "project",
                ProjectIdentity.TargetId(projectPath),
                "syntax_fallback",
                projectPath,
                1,
                1,
                UniqueMembers(facts.Members.Where(member => member.ProjectKey == projectKey)),
                UniqueTypes(facts.Types.Where(type => type.ProjectKey == projectKey)));
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
            AddNumericMetric(results, "type_count", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, typeCountsByFile.GetValueOrDefault(file.FilePath));

            IReadOnlyList<MemberFacts> members = membersByFile.GetValueOrDefault(file.FilePath, []);
            AddNumericMetric(results, "member_count", "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, members.Count);
            AddMemberAggregateMetrics(results, "file", file.TargetId, file.TargetIdStability, file.FilePath, file.StartLine, file.EndLine, members);
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "lines_of_code", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.LinesOfCode);
            AddNumericMetric(results, "token_line_count", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.TokenLineCount);
            AddNumericMetric(results, "member_count", "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, type.MemberCount);
            IReadOnlyList<MemberFacts> typeMembers = membersByType.GetValueOrDefault(type.TargetId, []);
            AddMemberAggregateMetrics(results, "type", type.TargetId, type.TargetIdStability, type.FilePath, type.StartLine, type.EndLine, typeMembers);
            AddNumericMetric(
                results,
                "weighted_member_complexity",
                "type",
                type.TargetId,
                type.TargetIdStability,
                type.FilePath,
                type.StartLine,
                type.EndLine,
                typeMembers.Sum(member => member.ControlFlow.CyclomaticComplexity));
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddNumericMetric(results, "member_length", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.MemberLength);
            AddNumericMetric(results, "parameter_count", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ParameterCount);
            AddNumericMetric(results, "cyclomatic_complexity", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.CyclomaticComplexity);
            AddNumericMetric(results, "decision_point_count", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.DecisionPointCount);
            AddNumericMetric(results, "cognitive_complexity", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.CognitiveComplexity);
            AddNumericMetric(results, "nesting_depth", "member", member.TargetId, member.TargetIdStability, member.FilePath, member.StartLine, member.EndLine, member.ControlFlow.NestingDepth);
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
        AddNumericMetric(results, "lines_of_code", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.LinesOfCode);
        AddNumericMetric(results, "token_line_count", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.TokenLineCount);
        AddNumericMetric(results, "blank_line_count", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.BlankLineCount);
        AddNumericMetric(results, "comment_only_line_count", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.CommentOnlyLineCount);
        AddNumericMetric(results, "commented_line_count", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.CommentedLineCount);
        AddNumericMetric(results, "mixed_code_comment_line_count", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.MixedCodeCommentLineCount);
        AddNumericMetric(results, "documentation_comment_line_count", targetKind, targetId, targetIdStability, filePath, startLine, endLine, lines.DocumentationCommentLineCount);

        if (lines.LinesOfCode == 0)
        {
            return;
        }

        AddRatioMetric(results, "token_line_ratio", targetKind, targetId, targetIdStability, filePath, startLine, endLine, (double)lines.TokenLineCount / lines.LinesOfCode);
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
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            value));
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
        AddNumericMetric(results, "max_member_length", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.MethodLength));
        AddNumericMetric(results, "p95_member_length", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Percentile(members, member => member.MethodLength, 0.95));
        AddNumericMetric(results, "max_member_cyclomatic_complexity", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.ControlFlow.CyclomaticComplexity));
        AddNumericMetric(results, "p95_member_cyclomatic_complexity", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Percentile(members, member => member.ControlFlow.CyclomaticComplexity, 0.95));
        AddNumericMetric(results, "max_member_cognitive_complexity", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.ControlFlow.CognitiveComplexity));
        AddNumericMetric(results, "p95_member_cognitive_complexity", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Percentile(members, member => member.ControlFlow.CognitiveComplexity, 0.95));
        AddNumericMetric(results, "max_member_nesting_depth", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(members, member => member.ControlFlow.NestingDepth));
    }

    private static void AddScopeAggregateMetrics(
        List<MetricResultLine> results,
        string targetKind,
        string targetId,
        string targetIdStability,
        string filePath,
        int startLine,
        int endLine,
        IReadOnlyList<MemberFacts> members,
        IReadOnlyList<TypeFacts> types)
    {
        AddMemberAggregateMetrics(results, targetKind, targetId, targetIdStability, filePath, startLine, endLine, members);
        AddNumericMetric(results, "max_type_lines_of_code", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Max(types, type => type.LinesOfCode));
        AddNumericMetric(results, "p95_type_lines_of_code", targetKind, targetId, targetIdStability, filePath, startLine, endLine, Percentile(types, type => type.LinesOfCode, 0.95));
    }

    private static MemberFacts[] UniqueMembers(IEnumerable<MemberFacts> members)
    {
        return members
            .DistinctBy(member => member.TargetId, StringComparer.Ordinal)
            .ToArray();
    }

    private static TypeFacts[] UniqueTypes(IEnumerable<TypeFacts> types)
    {
        return types
            .DistinctBy(type => type.TargetId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int Max<T>(IReadOnlyList<T> values, Func<T, int> selector)
    {
        return values.Count == 0 ? 0 : values.Max(selector);
    }

    private static int Percentile<T>(IReadOnlyList<T> values, Func<T, int> selector, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values
            .Select(selector)
            .Order()
            .ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;

        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static void AddNumericMetric(
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
        results.Add(MetricResultFactory.Numeric(
            metricId,
            targetKind,
            targetId,
            targetIdStability,
            filePath,
            startLine,
            endLine,
            value));
    }

    private sealed record LineAggregate(
        int LinesOfCode,
        int TokenLineCount,
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
                file.TokenLineCount,
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
                    aggregate.TokenLineCount + file.TokenLineCount,
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
