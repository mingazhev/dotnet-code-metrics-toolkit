using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Core.Reporting;

public static class GraphProjector
{
    public static GraphArtifact Project(SyntaxAnalysisFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        GraphNodeLine[] nodes = new[] { CreateSolutionNode() }
            .Concat(facts.ProjectPaths
            .Order(StringComparer.Ordinal)
            .Select(CreateProjectNode))
            .Concat(facts.Files
            .OrderBy(file => file.TargetId, StringComparer.Ordinal)
            .Select(CreateFileNode))
            .Concat(facts.Types
                .OrderBy(type => type.TargetId, StringComparer.Ordinal)
                .Select(CreateTypeNode))
            .Concat(facts.Members
                .OrderBy(member => member.TargetId, StringComparer.Ordinal)
                .Select(CreateMemberNode))
            .ToArray();

        GraphEdgeLine[] edges = CreateSolutionProjectEdges(facts)
            .Concat(CreateProjectFileEdges(facts))
            .Concat(facts.GraphEdges.Select(edge =>
                new GraphEdgeLine(edge.From, edge.To, edge.Kind, edge.Confidence)))
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();

        return new GraphArtifact(ContractVersion.Current, nodes, edges);
    }

    private static GraphNodeLine CreateSolutionNode()
    {
        return new GraphNodeLine
        {
            Id = TargetIds.SolutionRoot,
            Kind = "solution",
            Name = "analysis-root",
            TargetIdStability = "syntax_fallback",
            FilePath = ".",
            StartLine = 1,
            EndLine = 1
        };
    }

    private static GraphNodeLine CreateProjectNode(string projectPath)
    {
        return new GraphNodeLine
        {
            Id = ProjectIdentity.TargetId(projectPath),
            Kind = "project",
            Name = Path.GetFileNameWithoutExtension(projectPath),
            TargetIdStability = "syntax_fallback",
            FilePath = projectPath,
            StartLine = 1,
            EndLine = 1
        };
    }

    private static IEnumerable<GraphEdgeLine> CreateSolutionProjectEdges(SyntaxAnalysisFacts facts)
    {
        return facts.ProjectPaths.Select(projectPath =>
            new GraphEdgeLine(
                TargetIds.SolutionRoot,
                ProjectIdentity.TargetId(projectPath),
                "contains",
                "exact"));
    }

    private static IEnumerable<GraphEdgeLine> CreateProjectFileEdges(SyntaxAnalysisFacts facts)
    {
        foreach (var projectPath in facts.ProjectPaths)
        {
            var projectKey = ProjectIdentity.Key(projectPath);
            var projectTargetId = ProjectIdentity.TargetId(projectPath);

            foreach (FileFacts file in facts.Files.Where(file => file.ProjectKey == projectKey))
            {
                yield return new GraphEdgeLine(projectTargetId, file.TargetId, "contains", "exact");
            }
        }
    }

    private static GraphNodeLine CreateFileNode(FileFacts file)
    {
        return new GraphNodeLine
        {
            Id = file.TargetId,
            Kind = "file",
            Name = Path.GetFileName(file.FilePath),
            TargetIdStability = file.TargetIdStability,
            FilePath = file.FilePath,
            StartLine = file.StartLine,
            EndLine = file.EndLine
        };
    }

    private static GraphNodeLine CreateTypeNode(TypeFacts type)
    {
        return new GraphNodeLine
        {
            Id = type.TargetId,
            Kind = "type",
            Name = type.Name,
            TargetIdStability = type.TargetIdStability,
            FilePath = type.FilePath,
            StartLine = type.StartLine,
            EndLine = type.EndLine,
            Declarations = type.Declarations
                .Select(span => new GraphSourceSpanLine(span.FilePath, span.StartLine, span.EndLine))
                .ToArray()
        };
    }

    private static GraphNodeLine CreateMemberNode(MemberFacts member)
    {
        return new GraphNodeLine
        {
            Id = member.TargetId,
            Kind = "member",
            Name = member.Name,
            TargetIdStability = member.TargetIdStability,
            FilePath = member.FilePath,
            StartLine = member.StartLine,
            EndLine = member.EndLine,
            Declarations = member.Declarations
                .Select(span => new GraphSourceSpanLine(span.FilePath, span.StartLine, span.EndLine))
                .ToArray()
        };
    }
}
